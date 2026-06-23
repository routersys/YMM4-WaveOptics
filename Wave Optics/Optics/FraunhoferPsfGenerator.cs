using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WaveOptics.Abstractions;
using WaveOptics.Fourier;

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
        ArgumentNullException.ThrowIfNull(descriptor);

        var gridSize = descriptor.PupilGridSize;
        var area = gridSize * gridSize;
        var real = ArrayPool<double>.Shared.Rent(area);
        var imaginary = ArrayPool<double>.Shared.Rent(area);
        var intensity = ArrayPool<double>.Shared.Rent(area);
        try
        {
            var openSampleCount = BuildPupil(descriptor, real, imaginary);
            if (openSampleCount == 0)
                throw new InvalidOperationException();

            FastFourierTransform.Forward2D(real, imaginary, gridSize, gridSize);

            var fullEnergy = ComputeShiftedIntensity(real, imaginary, intensity, gridSize);
            if (!double.IsFinite(fullEnergy) || fullEnergy <= 0)
                throw new InvalidOperationException();

            var wavelengthMicrometers = descriptor.WavelengthNanometers / 1000d;
            var focalPlaneSamplePitch = wavelengthMicrometers * descriptor.FNumber * descriptor.PupilDiameterSamples / gridSize;
            var center = gridSize / 2;
            var kernelSize = descriptor.KernelSize;
            var kernelRadius = kernelSize / 2;
            var kernel = new double[kernelSize * kernelSize];
            var rawKernelEnergy = 0d;

            for (var y = 0; y < kernelSize; y++)
            {
                var sampleY = center + (y - kernelRadius) * descriptor.SensorPixelPitchMicrometers / focalPlaneSamplePitch;
                for (var x = 0; x < kernelSize; x++)
                {
                    var sampleX = center + (x - kernelRadius) * descriptor.SensorPixelPitchMicrometers / focalPlaneSamplePitch;
                    var value = SampleBilinear(intensity, gridSize, sampleX, sampleY);
                    kernel[y * kernelSize + x] = value;
                    rawKernelEnergy += value;
                }
            }

            if (!double.IsFinite(rawKernelEnergy) || rawKernelEnergy <= 0)
                throw new InvalidOperationException();

            var psfKernel = new PsfKernel(kernelSize, kernel);
            var diagnostics = new PsfDiagnostics(
                openSampleCount,
                focalPlaneSamplePitch,
                rawKernelEnergy / fullEnergy,
                Peak(psfKernel.Values.Span));
            return new PsfGenerationResult(psfKernel, diagnostics);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(real);
            ArrayPool<double>.Shared.Return(imaginary);
            ArrayPool<double>.Shared.Return(intensity);
        }
    }

    static int BuildPupil(PsfDescriptor descriptor, double[] real, double[] imaginary)
    {
        var gridSize = descriptor.PupilGridSize;
        var center = gridSize / 2;
        var pupilRadius = descriptor.PupilDiameterSamples / 2d;
        var rotation = descriptor.BladeRotationDegrees * Math.PI / 180d;
        var openSampleCount = 0;

        for (var y = 0; y < gridSize; y++)
        {
            var normalizedY = (y - center) / pupilRadius;
            var row = y * gridSize;
            for (var x = 0; x < gridSize; x++)
            {
                var index = row + x;
                var normalizedX = (x - center) / pupilRadius;
                if (IsInsideAperture(normalizedX, normalizedY, descriptor, rotation))
                {
                    var waves = ZernikeWavefront.Evaluate(normalizedX, normalizedY, descriptor.Aberration);
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

    static bool IsInsideAperture(double x, double y, PsfDescriptor descriptor, double rotation)
    {
        var radiusSquared = x * x + y * y;
        var obstructionSquared = descriptor.CentralObstructionRatio * descriptor.CentralObstructionRatio;
        if (radiusSquared > 1d || radiusSquared < obstructionSquared)
            return false;
        if (descriptor.ApertureShape == ApertureShape.Circular)
            return true;

        var radius = Math.Sqrt(radiusSquared);
        if (radius == 0)
            return descriptor.CentralObstructionRatio == 0;
        var sector = TwoPi / descriptor.BladeCount;
        var angle = Math.Atan2(y, x) - rotation;
        var folded = angle - sector * Math.Round(angle / sector);
        var boundary = Math.Cos(Math.PI / descriptor.BladeCount) / Math.Cos(folded);
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
