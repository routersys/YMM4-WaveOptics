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

    [Theory]
    [InlineData(256, 15, 8d, 4d, 1d)]
    [InlineData(512, 40, 8d, 4d, 0.8d)]
    [InlineData(128, 40, 8d, 4d, 1.31d)]
    [InlineData(128, 63, 0.5d, 100d, 1d)]
    [InlineData(256, 3, 64d, 0.25d, 0.5d)]
    [InlineData(64, 40, 8d, 4d, 1d)]
    public void TheTransposedSamplingIsTheRowMajorSampling(int gridSize, int radius, double fNumber, double pixelPitch, double positionScale)
    {
        var specification = PsfSpecification.Of(Descriptor(gridSize: gridSize, diameter: gridSize / 4, kernelRadius: radius, fNumber: fNumber, pixelPitch: pixelPitch));
        var random = new Random(7);
        var rowMajor = new double[gridSize * gridSize];
        var transposed = new double[gridSize * gridSize];
        for (var row = 0; row < gridSize; row++)
        {
            for (var column = 0; column < gridSize; column++)
            {
                var value = random.NextDouble() * random.NextDouble();
                rowMajor[row * gridSize + column] = value;
                transposed[column * gridSize + row] = value;
            }
        }

        var size = specification.KernelSize;
        var expected = new double[size * size];
        var actual = new double[size * size];
        actual.AsSpan().Fill(double.NaN);

        var expectedEnergy = FraunhoferPsfGenerator.SampleKernel(in specification, rowMajor, expected, positionScale);
        var actualEnergy = FraunhoferPsfGenerator.SampleKernelTransposed(in specification, transposed, actual, positionScale);

        Assert.Equal(BitConverter.DoubleToInt64Bits(expectedEnergy), BitConverter.DoubleToInt64Bits(actualEnergy));
        Assert.True(expected.AsSpan().SequenceEqual(actual));
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
        void SampleEveryDefocusStep()
        {
            for (var step = 0; step < 4; step++)
            {
                var animated = specification with { Aberration = new WavefrontAberration(defocusWaves: step * 0.25) };
                sampler.TrySample(in animated, kernel.AsSpan(0, 31 * 31));
            }
        }

        SampleEveryDefocusStep();
        SampleEveryDefocusStep();
        AllocationProbe.Settle();

        Assert.Equal(0, AllocationProbe.MinimumAllocatedBytes(SampleEveryDefocusStep, 16));
    }

    [Fact]
    public void ASamplerHoldsOnlyTheRowsTheApertureReaches()
    {
        const int GridSize = 512;
        var specification = PsfSpecification.Of(Descriptor(gridSize: GridSize, diameter: 16));
        Assert.True(new FraunhoferKernelSampler().TrySample(in specification, kernel.AsSpan(0, 31 * 31)));
        var fresh = new FraunhoferKernelSampler();

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(fresh.TrySample(in specification, kernel.AsSpan(0, 31 * 31)));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < (long)GridSize * GridSize * sizeof(double) * 2, allocated.ToString());
    }
}
