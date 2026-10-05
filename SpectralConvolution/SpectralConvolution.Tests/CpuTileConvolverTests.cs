namespace SpectralConvolution.Tests;

public sealed class CpuTileConvolverTests
{
    static (byte[] Output, float[] Convolved) Run(ConvolutionScene scene, int threads, float gain = 1f)
    {
        var output = new byte[scene.RegionLength];
        var convolved = new float[scene.RegionLength];
        using var convolver = new CpuTileConvolver(threads);
        convolver.Convolve(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, scene.Plan, output, gain, convolved);
        return (output, convolved);
    }

    [Theory]
    [InlineData(64, 5, 150, 97, 11)]
    [InlineData(128, 23, 140, 90, 24)]
    [InlineData(64, 0, 70, 70, 3)]
    public void TheResultStaysWithinTheBoundOfTheExactCorrelation(int size, int radius, int width, int height, int margin)
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(width, height, size + radius, 0.4), width, height, margin, radius, size);

        var (_, convolved) = Run(scene, 3);

        Assert.True(scene.Plan.TileCount > 1);
        Assert.InRange(scene.WorstRatio(convolved, ConvolutionBound.CpuOperationError), 0d, 1d);
    }

    [Fact]
    public void AnOpaqueWhiteSceneStaysWithinTheBound()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.Uniform(120, 120, 255), 120, 120, 8, 7, 64);

        var (output, convolved) = Run(scene, 2);

        Assert.InRange(scene.WorstRatio(convolved, ConvolutionBound.CpuOperationError), 0d, 1d);
        var center = ((scene.Plan.RegionHeight / 2) * scene.Plan.RegionWidth + scene.Plan.RegionWidth / 2) * 4;
        Assert.Equal([255, 255, 255, 255], output.AsSpan(center, 4).ToArray());
    }

    [Fact]
    public void TheResultDoesNotDependOnTheNumberOfThreads()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(300, 170, 5, 0.6), 300, 170, 16, 9, 64);

        var single = Run(scene, 1, 1.75f);
        var several = Run(scene, 3, 1.75f);
        var many = Run(scene, 16, 1.75f);

        Assert.Equal(single.Output, several.Output);
        Assert.Equal(single.Output, many.Output);
        Assert.Equal(single.Convolved, several.Convolved);
        Assert.Equal(single.Convolved, many.Convolved);
    }

    [Fact]
    public void TheOutputIsTheGainAppliedToTheConvolution()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(90, 60, 9, 0.7), 90, 60, 8, 4, 64);

        var (output, convolved) = Run(scene, 2, 2.5f);

        for (var index = 0; index < output.Length; index += 4)
        {
            Assert.Equal(CpuTileConvolver.ToUnorm(convolved[index + 2] * 2.5f), output[index]);
            Assert.Equal(CpuTileConvolver.ToUnorm(convolved[index + 1] * 2.5f), output[index + 1]);
            Assert.Equal(CpuTileConvolver.ToUnorm(convolved[index] * 2.5f), output[index + 2]);
            Assert.Equal(CpuTileConvolver.ToUnorm(convolved[index + 3] * 2.5f), output[index + 3]);
        }
    }

    [Fact]
    public void ARegionAwayFromTheSourceIsClearedToZero()
    {
        var spectrum = new KernelSpectrum();
        spectrum.Update(ConvolutionScene.AsymmetricKernel(3), 3, 64);
        var plan = TilePlan.Create(64, 3, 500, 500, 130, 70);
        var output = new byte[plan.RegionWidth * plan.RegionHeight * 4];
        var convolved = new float[output.Length];
        Array.Fill(output, (byte)7);
        Array.Fill(convolved, 7f);
        using var convolver = new CpuTileConvolver(2);

        convolver.Convolve(ConvolutionScene.Uniform(40, 40, 255), 0, 0, 40, 40, spectrum, plan, output, 1f, convolved);

        Assert.All(output, value => Assert.Equal(0, value));
        Assert.All(convolved, value => Assert.Equal(0f, value));
    }

    [Fact]
    public void TheStoreIsOptional()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(80, 50, 3, 0.5), 80, 50, 8, 4, 64);
        var expected = Run(scene, 2).Output;
        var output = new byte[scene.RegionLength];
        using var convolver = new CpuTileConvolver(2);

        convolver.Convolve(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, scene.Plan, output, 1f, null);

        Assert.Equal(expected, output);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2.75f)]
    [InlineData(0.3f)]
    public void RenderingTheStoreMatchesTheConvolutionBitForBit(float gain)
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(130, 90, 13, 0.6), 130, 90, 8, 4, 64);
        var (expected, _) = Run(scene, 3, gain);
        var (_, stored) = Run(scene, 3, 1f);
        var output = new byte[scene.RegionLength];
        using var convolver = new CpuTileConvolver(3);

        convolver.RenderStored(stored, scene.Plan.RegionWidth * scene.Plan.RegionHeight, gain, output);

        Assert.Equal(expected, output);
    }

    [Fact]
    public void RenderingTheStoreDoesNotDependOnTheNumberOfThreads()
    {
        var pixels = CpuTileConvolver.StoredChunkPixels * 3 + 17;
        var random = new Random(3);
        var stored = new float[pixels * 4];
        for (var index = 0; index < stored.Length; index++)
            stored[index] = (float)(random.NextDouble() * 1.2 - 0.1);
        var single = new byte[stored.Length];
        var several = new byte[stored.Length];
        using (var convolver = new CpuTileConvolver(1))
            convolver.RenderStored(stored, pixels, 1.3f, single);
        using (var convolver = new CpuTileConvolver(6))
            convolver.RenderStored(stored, pixels, 1.3f, several);

        Assert.Equal(single, several);
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            Assert.Equal(CpuTileConvolver.ToUnorm(stored[pixel * 4 + 2] * 1.3f), single[pixel * 4]);
            Assert.Equal(CpuTileConvolver.ToUnorm(stored[pixel * 4] * 1.3f), single[pixel * 4 + 2]);
        }
    }

    [Fact]
    public void RenderingTheStoreRejectsShortBuffers()
    {
        using var convolver = new CpuTileConvolver(2);

        Assert.Throws<ArgumentException>(() => convolver.RenderStored(new float[7], 2, 1f, new byte[8]));
        Assert.Throws<ArgumentException>(() => convolver.RenderStored(new float[8], 2, 1f, new byte[7]));
        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.RenderStored(new float[8], 2, float.PositiveInfinity, new byte[8]));
        convolver.RenderStored([], 0, 1f, []);
    }

    [Fact]
    public void AWarmStoredRenderAllocatesNothingOnTheCallingThread()
    {
        var pixels = CpuTileConvolver.StoredChunkPixels * 4;
        var stored = new float[pixels * 4];
        var output = new byte[pixels * 4];
        using var convolver = new CpuTileConvolver(4);
        for (var warmUp = 0; warmUp < 3; warmUp++)
            convolver.RenderStored(stored, pixels, 1f, output);

        var minimum = AllocationProbe.MinimumAllocatedBytes(() => convolver.RenderStored(stored, pixels, 1f, output), 20);

        Assert.Equal(0L, minimum);
    }

    [Theory]
    [InlineData(float.NaN, 0)]
    [InlineData(-0.25f, 0)]
    [InlineData(0f, 0)]
    [InlineData(1f / 255f, 1)]
    [InlineData(0.5f, 128)]
    [InlineData(1f, 255)]
    [InlineData(3f, 255)]
    [InlineData(float.PositiveInfinity, 255)]
    public void ConversionFollowsTheDirect3DRule(float value, byte expected)
    {
        Assert.Equal(expected, CpuTileConvolver.ToUnorm(value));
    }

    [Fact]
    public void MismatchedArgumentsAreRejected()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.Uniform(20, 20, 9), 20, 20, 4, 2, 64);
        using var convolver = new CpuTileConvolver(1);
        var output = new byte[scene.RegionLength];
        var otherSpectrum = new KernelSpectrum();
        otherSpectrum.Update(ConvolutionScene.AsymmetricKernel(3), 3, 64);

        Assert.Throws<ArgumentException>(() => convolver.Convolve(scene.Source, 4, 4, 20, 20, otherSpectrum, scene.Plan, output, 1f, null));
        Assert.Throws<ArgumentException>(() => convolver.Convolve(scene.Source, 4, 4, 20, 21, scene.Spectrum, scene.Plan, output, 1f, null));
        Assert.Throws<ArgumentException>(() => convolver.Convolve(scene.Source, 4, 4, 20, 20, scene.Spectrum, scene.Plan, new byte[output.Length - 1], 1f, null));
        Assert.Throws<ArgumentException>(() => convolver.Convolve(scene.Source, 4, 4, 20, 20, scene.Spectrum, scene.Plan, output, 1f, new float[output.Length - 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.Convolve(scene.Source, 4, 4, 20, 20, scene.Spectrum, scene.Plan, output, float.NaN, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CpuTileConvolver(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AWarmConvolutionAllocatesNothingOnTheCallingThread(int threads)
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(200, 120, 1, 0.3), 200, 120, 12, 6, 64);
        var output = new byte[scene.RegionLength];
        var convolved = new float[scene.RegionLength];
        using var convolver = new CpuTileConvolver(threads);
        for (var warmUp = 0; warmUp < 3; warmUp++)
            convolver.Convolve(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, scene.Plan, output, 1f, convolved);

        var minimum = AllocationProbe.MinimumAllocatedBytes(
            () => convolver.Convolve(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, scene.Plan, output, 1f, convolved),
            40);

        Assert.Equal(0L, minimum);
    }

    [Fact]
    public void ADisposedConvolverRefusesWork()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.Uniform(20, 20, 9), 20, 20, 4, 2, 64);
        var convolver = new CpuTileConvolver(3);
        convolver.Convolve(scene.Source, 4, 4, 20, 20, scene.Spectrum, scene.Plan, new byte[scene.RegionLength], 1f, null);

        convolver.Dispose();
        convolver.Dispose();

        Assert.Throws<ObjectDisposedException>(() => convolver.Convolve(scene.Source, 4, 4, 20, 20, scene.Spectrum, scene.Plan, new byte[scene.RegionLength], 1f, null));
    }

    [Theory]
    [InlineData(128, 256)]
    [InlineData(512, 16)]
    public void TheWorkingSetLimitsTheThreads(int size, int expected)
    {
        Assert.Equal(expected, CpuTileConvolver.ActiveThreads(1000, size));
        Assert.Equal(3, CpuTileConvolver.ActiveThreads(3, size));
    }

    [Fact]
    public void OnlyTheThreadsThatComputeKeepAWorkingAreaOfTheCurrentSize()
    {
        static long PerThread(int size) => ((long)size * size * 4 + size * 4 * 2) * sizeof(float);
        static void Convolve(CpuTileConvolver convolver, int size, int radius, int regionSide)
        {
            var spectrum = new KernelSpectrum();
            spectrum.Update(ConvolutionScene.AsymmetricKernel(radius), radius, size);
            var plan = TilePlan.Create(size, radius, 0, 0, regionSide, regionSide);
            var source = ConvolutionScene.Uniform(16, 16, 200);
            for (var run = 0; run < 4; run++)
                convolver.Convolve(source, 40, 40, 16, 16, spectrum, plan, new byte[plan.RegionWidth * plan.RegionHeight * 4], 1f, null);
        }

        using var convolver = new CpuTileConvolver(24);

        Convolve(convolver, 512, 24, 600);
        Assert.Equal(4 * PerThread(512), convolver.WorkingBytes);

        Convolve(convolver, 512, 24, 2000);
        Assert.Equal(16 * PerThread(512), convolver.WorkingBytes);

        Convolve(convolver, 128, 3, 610);
        Assert.Equal(24 * PerThread(128), convolver.WorkingBytes);
    }
}
