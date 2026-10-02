using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpectralConvolution;
using WaveOptics.Abstractions;

namespace WaveOptics.Optics;

public sealed class FraunhoferPsfGenerator : IPsfGenerator
{
    const double TwoPi = 2 * Math.PI;

    public OpticalApiVersion ApiVersion => OpticalApiVersion.Current;

    public OpticalCapabilities Capabilities => OpticalCapabilities.MonochromaticPsf
        | OpticalCapabilities.CircularAperture
        | OpticalCapabilities.RegularPolygonAperture
        | OpticalCapabilities.CentralObstruction
        | OpticalCapabilities.ZernikeAberration
        | OpticalCapabilities.DirectConvolution;

    public PsfGenerationResult Generate(PsfDescriptor descriptor)
    {
        if (!TryGenerate(descriptor, out var result))
            throw new InvalidOperationException();
        return result;
    }

    public bool TryGenerate(PsfDescriptor descriptor, [NotNullWhen(true)] out PsfGenerationResult? result)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        result = null;
        var specification = PsfSpecification.Of(descriptor);
        var gridSize = specification.PupilGridSize;
        var area = gridSize * gridSize;
        var real = ArrayPool<double>.Shared.Rent(area);
        var imaginary = ArrayPool<double>.Shared.Rent(area);
        var intensity = ArrayPool<double>.Shared.Rent(area);
        try
        {
            var openSampleCount = BuildPupil(in specification, real, imaginary, 0, gridSize - 1);
            if (openSampleCount == 0)
                return false;

            FastFourierTransform.Forward2D(real, imaginary, gridSize, gridSize);

            var fullEnergy = ComputeShiftedIntensity(real, imaginary, intensity, gridSize);
            if (!double.IsFinite(fullEnergy) || fullEnergy <= 0)
                return false;

            var kernelSize = specification.KernelSize;
            var kernel = new double[kernelSize * kernelSize];
            var rawKernelEnergy = SampleKernel(in specification, intensity, kernel);
            if (!double.IsFinite(rawKernelEnergy) || rawKernelEnergy <= 0)
                return false;

            var psfKernel = new PsfKernel(kernelSize, kernel);
            var diagnostics = new PsfDiagnostics(
                openSampleCount,
                FocalPlaneSamplePitch(in specification),
                rawKernelEnergy / fullEnergy,
                Peak(psfKernel.Values.Span));
            result = new PsfGenerationResult(psfKernel, diagnostics);
            return true;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(real);
            ArrayPool<double>.Shared.Return(imaginary);
            ArrayPool<double>.Shared.Return(intensity);
        }
    }

    internal static int BuildPupil(in PsfSpecification specification, double[] real, double[] imaginary, int firstRow, int lastRow)
    {
        var gridSize = specification.PupilGridSize;
        var center = gridSize / 2;
        var pupilRadius = specification.PupilDiameterSamples / 2d;
        var rotation = specification.BladeRotationDegrees * Math.PI / 180d;
        var openSampleCount = 0;

        for (var y = firstRow; y <= lastRow; y++)
        {
            var normalizedY = (y - center) / pupilRadius;
            var row = y * gridSize;
            for (var x = 0; x < gridSize; x++)
            {
                var index = row + x;
                var normalizedX = (x - center) / pupilRadius;
                if (IsInsideAperture(normalizedX, normalizedY, in specification, rotation))
                {
                    var waves = ZernikeWavefront.Evaluate(normalizedX, normalizedY, specification.Aberration);
                    var (sin, cos) = Math.SinCos(TwoPi * waves);
                    real[index] = cos;
                    imaginary[index] = sin;
                    openSampleCount++;
                }
                else
                {
                    real[index] = 0;
                    imaginary[index] = 0;
                }
            }
        }

        return openSampleCount;
    }

    internal static double FocalPlaneSamplePitch(in PsfSpecification specification)
    {
        var wavelengthMicrometers = specification.WavelengthNanometers / 1000d;
        return wavelengthMicrometers * specification.FNumber * specification.PupilDiameterSamples / specification.PupilGridSize;
    }

    internal static double SamplePosition(int index, int kernelRadius, int center, double pixelPitch, double focalPlaneSamplePitch)
        => center + (index - kernelRadius) * pixelPitch / focalPlaneSamplePitch;

    internal static double SampleKernel(in PsfSpecification specification, double[] intensity, Span<double> kernel)
    {
        var gridSize = specification.PupilGridSize;
        var focalPlaneSamplePitch = FocalPlaneSamplePitch(in specification);
        var center = gridSize / 2;
        var kernelSize = specification.KernelSize;
        var kernelRadius = kernelSize / 2;
        var rawKernelEnergy = 0d;

        for (var y = 0; y < kernelSize; y++)
        {
            var sampleY = SamplePosition(y, kernelRadius, center, specification.SensorPixelPitchMicrometers, focalPlaneSamplePitch);
            for (var x = 0; x < kernelSize; x++)
            {
                var sampleX = SamplePosition(x, kernelRadius, center, specification.SensorPixelPitchMicrometers, focalPlaneSamplePitch);
                var value = SampleBilinear(intensity, gridSize, sampleX, sampleY);
                kernel[y * kernelSize + x] = value;
                rawKernelEnergy += value;
            }
        }

        return rawKernelEnergy;
    }

    static double ComputeShiftedIntensity(double[] real, double[] imaginary, double[] intensity, int gridSize)
    {
        var center = gridSize / 2;
        var fullEnergy = 0d;
        for (var y = 0; y < gridSize; y++)
        {
            var sourceRow = ((y + center) % gridSize) * gridSize;
            var destinationRow = y * gridSize;
            fullEnergy += Power(
                real.AsSpan(sourceRow + center, gridSize - center),
                imaginary.AsSpan(sourceRow + center, gridSize - center),
                intensity.AsSpan(destinationRow, gridSize - center));
            fullEnergy += Power(
                real.AsSpan(sourceRow, center),
                imaginary.AsSpan(sourceRow, center),
                intensity.AsSpan(destinationRow + gridSize - center, center));
        }
        return fullEnergy;
    }

    static double Power(ReadOnlySpan<double> real, ReadOnlySpan<double> imaginary, Span<double> intensity)
    {
        var count = real.Length;
        ref var realBase = ref MemoryMarshal.GetReference(real);
        ref var imaginaryBase = ref MemoryMarshal.GetReference(imaginary);
        ref var intensityBase = ref MemoryMarshal.GetReference(intensity);
        var sum = 0d;
        var index = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var accumulator = Vector<double>.Zero;
            var width = Vector<double>.Count;
            for (; index + width <= count; index += width)
            {
                var a = Vector.LoadUnsafe(ref Unsafe.Add(ref realBase, index));
                var b = Vector.LoadUnsafe(ref Unsafe.Add(ref imaginaryBase, index));
                var power = a * a + b * b;
                power.StoreUnsafe(ref Unsafe.Add(ref intensityBase, index));
                accumulator += power;
            }
            sum += Vector.Sum(accumulator);
        }
        for (; index < count; index++)
        {
            var a = Unsafe.Add(ref realBase, index);
            var b = Unsafe.Add(ref imaginaryBase, index);
            var power = a * a + b * b;
            Unsafe.Add(ref intensityBase, index) = power;
            sum += power;
        }
        return sum;
    }

    static double Peak(ReadOnlySpan<double> values)
    {
        var peak = 0d;
        foreach (var value in values)
        {
            if (value > peak)
                peak = value;
        }
        return peak;
    }

    static bool IsInsideAperture(double x, double y, in PsfSpecification specification, double rotation)
    {
        var radiusSquared = x * x + y * y;
        var obstructionSquared = specification.CentralObstructionRatio * specification.CentralObstructionRatio;
        if (radiusSquared > 1d || radiusSquared < obstructionSquared)
            return false;
        if (specification.ApertureShape == ApertureShape.Circular)
            return true;

        var radius = Math.Sqrt(radiusSquared);
        if (radius == 0)
            return specification.CentralObstructionRatio == 0;
        var sector = TwoPi / specification.BladeCount;
        var angle = Math.Atan2(y, x) - rotation;
        var folded = angle - sector * Math.Round(angle / sector);
        var boundary = Math.Cos(Math.PI / specification.BladeCount) / Math.Cos(folded);
        return radius <= boundary;
    }

    static double SampleBilinear(double[] values, int size, double x, double y)
    {
        if (x < 0 || y < 0 || x > size - 1 || y > size - 1)
            return 0;

        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = Math.Min(x0 + 1, size - 1);
        var y1 = Math.Min(y0 + 1, size - 1);
        var tx = x - x0;
        var ty = y - y0;
        var top = values[y0 * size + x0] * (1 - tx) + values[y0 * size + x1] * tx;
        var bottom = values[y1 * size + x0] * (1 - tx) + values[y1 * size + x1] * tx;
        return top * (1 - ty) + bottom * ty;
    }
}
