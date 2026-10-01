using WaveOptics.Abstractions;
using WaveOptics.Optics;

namespace WaveOptics.Tests.Psf;

public sealed class FraunhoferKernelSamplerTests
{
    readonly FraunhoferPsfGenerator generator = new();
    readonly FraunhoferKernelSampler sampler = new();
    readonly double[] kernel = new double[255 * 255];

    static PsfDescriptor Descriptor(
        int gridSize = 256,
        int diameter = 64,
        int kernelRadius = 15,
        double wavelength = 550,
        double fNumber = 8,
        double pixelPitch = 4,
        ApertureShape shape = ApertureShape.Circular,
        int blades = 6,
        double rotation = 0,
        double obstruction = 0,
        WavefrontAberration aberration = default)
        => new(gridSize, diameter, kernelRadius * 2 + 1, wavelength, fNumber, pixelPitch, shape, blades, rotation, obstruction, aberration);

    static IEnumerable<(string Name, PsfDescriptor Descriptor)> Cases()
    {
        yield return ("default", Descriptor());
        yield return ("high", Descriptor(gridSize: 512, diameter: 128));
        yield return ("draft after high", Descriptor(gridSize: 128, diameter: 32));
        yield return ("smallest grid", Descriptor(gridSize: 64, diameter: 16, kernelRadius: 3));
        yield return ("odd diameter", Descriptor(gridSize: 128, diameter: 17, aberration: new WavefrontAberration(comaVerticalWaves: 0.7)));
        yield return ("widest diameter", Descriptor(gridSize: 128, diameter: 64, kernelRadius: 9));
        yield return ("samples beyond the grid", Descriptor(pixelPitch: 100, fNumber: 0.5));
        yield return ("samples near the center", Descriptor(pixelPitch: 0.25, fNumber: 64, wavelength: 780));
        yield return ("polygon", Descriptor(shape: ApertureShape.RegularPolygon, blades: 5, rotation: 17, obstruction: 0.3));
        yield return ("closed aperture", Descriptor(gridSize: 128, diameter: 32, shape: ApertureShape.RegularPolygon, blades: 4, rotation: -357.5, obstruction: 0.95));
        yield return ("strong defocus", Descriptor(aberration: new WavefrontAberration(defocusWaves: -10)));
        yield return ("every term", Descriptor(gridSize: 512, diameter: 128, kernelRadius: 40, aberration: new WavefrontAberration(1.5, -0.8, 0.6, 2.2, -1.1, 0, 0, -0.9)));

        var random = new Random(29);
        for (var trial = 0; trial < 120; trial++)
        {
            var gridSize = 64 << random.Next(4);
            var aberration = new WavefrontAberration(
                defocusWaves: random.NextDouble() * 6 - 3,
                astigmatismVerticalWaves: random.NextDouble() * 4 - 2,
                astigmatismObliqueWaves: random.NextDouble() * 4 - 2,
                comaHorizontalWaves: random.NextDouble() * 4 - 2,
                comaVerticalWaves: random.NextDouble() * 4 - 2,
                sphericalWaves: random.NextDouble() * 4 - 2);
            yield return ($"random {trial}", Descriptor(
                gridSize,
                16 + random.Next(gridSize / 2 - 15),
                1 + random.Next(30),
                380 + random.NextDouble() * 400,
                0.5 + random.NextDouble() * 63.5,
                0.25 + random.NextDouble() * 30,
                random.Next(2) == 0 ? ApertureShape.Circular : ApertureShape.RegularPolygon,
                3 + random.Next(30),
                random.NextDouble() * 720 - 360,
                random.NextDouble() * 0.95,
                aberration));
        }
    }

    [Fact]
    public void TheSampledKernelIsTheGeneratedKernel()
    {
        var mismatches = new List<string>();
        foreach (var (name, descriptor) in Cases())
        {
            var size = descriptor.KernelSize;
            var sampled = kernel.AsSpan(0, size * size);
            var sampledOk = sampler.TrySample(PsfSpecification.Of(descriptor), sampled);
            var generatedOk = generator.TryGenerate(descriptor, out var result);

            if (sampledOk != generatedOk || (generatedOk && !sampled.SequenceEqual(result!.Kernel.Values.Span)))
                mismatches.Add(name);
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void AClosedApertureIsNotSampled()
    {
        var descriptor = Descriptor(gridSize: 128, diameter: 32, shape: ApertureShape.RegularPolygon, blades: 4, rotation: -357.5, obstruction: 0.95);

        Assert.False(sampler.TrySample(PsfSpecification.Of(descriptor), kernel.AsSpan(0, 31 * 31)));
    }

    [Fact]
    public void AWarmSamplerAllocatesNoManagedMemory()
    {
        var specification = PsfSpecification.Of(Descriptor());
        sampler.TrySample(in specification, kernel.AsSpan(0, 31 * 31));

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var step = 0; step < 4; step++)
        {
            var animated = specification with { Aberration = new WavefrontAberration(defocusWaves: step * 0.25) };
            sampler.TrySample(in animated, kernel.AsSpan(0, 31 * 31));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
