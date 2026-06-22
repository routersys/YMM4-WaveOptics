using System.Numerics;
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
        var pupilDiameter = descriptor.PupilDiameterSamples;
        var pupil = new Complex[gridSize * gridSize];
        var center = gridSize / 2;
        var pupilRadius = pupilDiameter / 2d;
        var rotation = descriptor.BladeRotationDegrees * Math.PI / 180d;
        var openSampleCount = 0;

        for (var y = 0; y < gridSize; y++)
        {
            var normalizedY = (y - center) / pupilRadius;
            for (var x = 0; x < gridSize; x++)
            {
                var normalizedX = (x - center) / pupilRadius;
                if (!IsInsideAperture(normalizedX, normalizedY, descriptor, rotation))
                    continue;

                var waves = ZernikeWavefront.Evaluate(normalizedX, normalizedY, descriptor.Aberration);
                pupil[y * gridSize + x] = Complex.FromPolarCoordinates(1d, TwoPi * waves);
                openSampleCount++;
            }
        }

        if (openSampleCount == 0)
            throw new InvalidOperationException();

        FastFourierTransform.Forward2D(pupil, gridSize, gridSize);
        var intensity = new double[pupil.Length];
        var fullEnergy = 0d;
        for (var y = 0; y < gridSize; y++)
        {
            var sourceY = (y + center) % gridSize;
            for (var x = 0; x < gridSize; x++)
            {
                var sourceX = (x + center) % gridSize;
                var value = pupil[sourceY * gridSize + sourceX].Magnitude;
                value *= value;
                intensity[y * gridSize + x] = value;
                fullEnergy += value;
            }
        }

        if (!double.IsFinite(fullEnergy) || fullEnergy <= 0)
            throw new InvalidOperationException();

        var inverseEnergy = 1d / fullEnergy;
        for (var index = 0; index < intensity.Length; index++)
            intensity[index] *= inverseEnergy;

        var wavelengthMicrometers = descriptor.WavelengthNanometers / 1000d;
        var focalPlaneSamplePitch = wavelengthMicrometers * descriptor.FNumber * pupilDiameter / gridSize;
        var kernel = new double[descriptor.KernelSize * descriptor.KernelSize];
        var kernelRadius = descriptor.KernelSize / 2;
        var unnormalizedKernelEnergy = 0d;

        for (var y = 0; y < descriptor.KernelSize; y++)
        {
            var physicalY = (y - kernelRadius) * descriptor.SensorPixelPitchMicrometers;
            var sampleY = center + physicalY / focalPlaneSamplePitch;
            for (var x = 0; x < descriptor.KernelSize; x++)
            {
                var physicalX = (x - kernelRadius) * descriptor.SensorPixelPitchMicrometers;
                var sampleX = center + physicalX / focalPlaneSamplePitch;
                var value = SampleBilinear(intensity, gridSize, sampleX, sampleY);
                kernel[y * descriptor.KernelSize + x] = value;
                unnormalizedKernelEnergy += value;
            }
        }

        if (!double.IsFinite(unnormalizedKernelEnergy) || unnormalizedKernelEnergy <= 0)
            throw new InvalidOperationException();

        var psfKernel = new PsfKernel(descriptor.KernelSize, kernel);
        var diagnostics = new PsfDiagnostics(
            openSampleCount,
            focalPlaneSamplePitch,
            unnormalizedKernelEnergy,
            psfKernel.Values.Span.ToArray().Max());
        return new PsfGenerationResult(psfKernel, diagnostics);
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
