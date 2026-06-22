using WaveOptics.Abstractions;
using WaveOptics.Optics;

namespace WaveOptics.Tests.Psf;

public sealed class FraunhoferPsfGeneratorTests
{
    readonly FraunhoferPsfGenerator generator = new();

    [Fact]
    public void IdealCircularPsfIsFiniteNonnegativeAndNormalized()
    {
        var result = generator.Generate(CreateDescriptor());

        Assert.Equal(1, result.Kernel.Sum(), 12);
        foreach (var value in result.Kernel.Values.Span)
        {
            Assert.True(double.IsFinite(value));
            Assert.True(value >= 0);
        }
    }

    [Fact]
    public void IdealCircularPsfIsCentrallySymmetric()
    {
        var kernel = generator.Generate(CreateDescriptor()).Kernel;
        var last = kernel.Size - 1;
        var maximumError = 0d;

        for (var y = 0; y < kernel.Size; y++)
        {
            for (var x = 0; x < kernel.Size; x++)
                maximumError = Math.Max(maximumError, Math.Abs(kernel[x, y] - kernel[last - x, last - y]));
        }

        Assert.True(maximumError < 1e-10, $"{maximumError:R}");
    }

    [Fact]
    public void FirstAiryMinimumMatchesDiffractionFormula()
    {
        const double wavelengthMicrometers = 0.55;
        const double fNumber = 8;
        const double pixelPitch = 2;
        var descriptor = CreateDescriptor(
            wavelengthNanometers: wavelengthMicrometers * 1000,
            fNumber: fNumber,
            pixelPitch: pixelPitch);
        var kernel = generator.Generate(descriptor).Kernel;
        var center = kernel.Size / 2;
        var measured = FindFirstMinimum(kernel, center);
        var expected = 1.22 * wavelengthMicrometers * fNumber / pixelPitch;

        Assert.InRange(measured, expected - 1, expected + 1);
    }

    [Fact]
    public void HorizontalComaBreaksCentralSymmetry()
    {
        var aberration = new WavefrontAberration(comaHorizontalWaves: 0.75);
        var kernel = generator.Generate(CreateDescriptor(aberration: aberration)).Kernel;
        var last = kernel.Size - 1;
        var difference = 0d;

        for (var y = 0; y < kernel.Size; y++)
        {
            for (var x = 0; x < kernel.Size; x++)
                difference += Math.Abs(kernel[x, y] - kernel[last - x, last - y]);
        }

        Assert.True(difference > 1e-4, $"{difference:R}");
    }

    [Fact]
    public void CentralObstructionReducesCentralPeak()
    {
        var unobstructed = generator.Generate(CreateDescriptor()).Kernel;
        var obstructed = generator.Generate(CreateDescriptor(obstruction: 0.5)).Kernel;
        var center = unobstructed.Size / 2;

        Assert.True(obstructed[center, center] < unobstructed[center, center]);
    }

    static int FindFirstMinimum(PsfKernel kernel, int center)
    {
        for (var radius = 1; radius < center - 1; radius++)
        {
            var previous = kernel[center + radius - 1, center];
            var current = kernel[center + radius, center];
            var next = kernel[center + radius + 1, center];
            if (current <= previous && current <= next)
                return radius;
        }
        throw new InvalidOperationException();
    }

    static PsfDescriptor CreateDescriptor(
        double wavelengthNanometers = 550,
        double fNumber = 8,
        double pixelPitch = 2,
        double obstruction = 0,
        WavefrontAberration aberration = default)
    {
        return new PsfDescriptor(
            512,
            128,
            31,
            wavelengthNanometers,
            fNumber,
            pixelPitch,
            ApertureShape.Circular,
            6,
            0,
            obstruction,
            aberration);
    }
}
