namespace SpectralConvolution.Tests;

public sealed class ChromaticCpuConvolverTests
{
    static (byte[] Output, float[] Convolved) Run(ChromaticConvolutionScene scene, int threads, float gain = 1f)
    {
        var output = new byte[scene.RegionLength];
        var convolved = new float[scene.RegionLength];
        using var convolver = new CpuTileConvolver(threads);
        convolver.Convolve(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, scene.Plan, output, gain, convolved, scene.Light);
        return (output, convolved);
    }

    [Fact]
    public void ASharedConvolverGivesTheResultOfADedicatedOne()
    {
        var scene = ChromaticConvolutionScene.Create(ConvolutionScene.RandomSource(200, 120, 17, 0.5), 200, 120, 14, 9, 64);

        var dedicated = Run(scene, Environment.ProcessorCount, 1.5f);
        var output = new byte[scene.RegionLength];
        var convolved = new float[scene.RegionLength];
        using var shared = CpuTileConvolver.CreateShared();
        shared.Convolve(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, scene.Plan, output, 1.5f, convolved, scene.Light);

        Assert.Equal(dedicated.Output, output);
        Assert.Equal(dedicated.Convolved, convolved);
    }

    [Theory]
    [InlineData(64, 5, 150, 97, 11)]
    [InlineData(128, 23, 140, 90, 24)]
    [InlineData(256, 40, 110, 70, 44)]
    [InlineData(64, 0, 70, 70, 3)]
    public void TheResultStaysWithinTheBoundOfTheExactCorrelationOfEachChannel(int size, int radius, int width, int height, int margin)
    {
        var scene = ChromaticConvolutionScene.Create(ConvolutionScene.RandomSource(width, height, size + radius, 0.4), width, height, margin, radius, size);

        var (_, convolved) = Run(scene, 3);

        Assert.True(scene.Plan.TileCount > 1);
        Assert.InRange(scene.WorstRatio(convolved, ConvolutionBound.CpuOperationError), 0d, 1d);
    }

    [Theory]
    [InlineData(false, 0f, 0f)]
    [InlineData(true, 0f, 0f)]
    [InlineData(true, 0.8f, 20f)]
    public void TheLinearLightResultStaysWithinTheBound(bool linear, float threshold, float boost)
    {
        var light = new LightOptions(linear, false, threshold, boost);
        var scene = ChromaticConvolutionScene.Create(ConvolutionScene.RandomSource(130, 90, 21, 0.5), 130, 90, 12, 9, 64, light);

        var (_, convolved) = Run(scene, 2);

        Assert.InRange(scene.WorstRatio(convolved, ConvolutionBound.CpuOperationError), 0d, 1d);
    }

    [Fact]
    public void IdenticalKernelsGiveTheResultOfASingleKernelWithinTheBound()
    {
        const int radius = 7;
        var kernel = ConvolutionScene.AsymmetricKernel(radius);
        var single = new KernelSpectrum();
        single.Update(kernel, radius, 64);
        var chromatic = new ChromaticKernelSpectrum();
        chromatic.Update(kernel, kernel, kernel, radius, 64);
        var source = ConvolutionScene.RandomSource(140, 100, 17, 0.5);
        var plan = TilePlan.Create(64, radius, 0, 0, 140 + 20, 100 + 20);
        var expected = new byte[plan.RegionWidth * plan.RegionHeight * 4];
        var actual = new byte[expected.Length];
        var expectedValues = new float[expected.Length];
        var actualValues = new float[expected.Length];
        using var convolver = new CpuTileConvolver(2);

        convolver.Convolve(source, 10, 10, 140, 100, single, plan, expected, 1f, expectedValues);
        convolver.Convolve(source, 10, 10, 140, 100, chromatic, plan, actual, 1f, actualValues);

        var tolerance = ConvolutionBound.Absolute(64, ConvolutionBound.CpuOperationError, 1e4) + ConvolutionBound.ChromaticAbsolute(64, ConvolutionBound.CpuOperationError, 1e4);
        for (var index = 0; index < expectedValues.Length; index++)
            Assert.True(Math.Abs(expectedValues[index] - actualValues[index]) <= tolerance, $"{index}: {expectedValues[index]} {actualValues[index]}");
    }

    [Fact]
    public void AnOpaqueWhiteSceneStaysWhiteAwayFromTheEdges()
    {
        var scene = ChromaticConvolutionScene.Create(ConvolutionScene.Uniform(120, 120, 255), 120, 120, 8, 7, 64);

        var (output, convolved) = Run(scene, 2);

        Assert.InRange(scene.WorstRatio(convolved, ConvolutionBound.CpuOperationError), 0d, 1d);
        var center = ((scene.Plan.RegionHeight / 2) * scene.Plan.RegionWidth + scene.Plan.RegionWidth / 2) * 4;
        Assert.Equal([255, 255, 255, 255], output.AsSpan(center, 4).ToArray());
    }

    [Fact]
    public void TheResultDoesNotDependOnTheNumberOfThreads()
    {
        var scene = ChromaticConvolutionScene.Create(ConvolutionScene.RandomSource(300, 170, 5, 0.6), 300, 170, 16, 9, 64);

        var single = Run(scene, 1, 1.75f);
        var several = Run(scene, 3, 1.75f);
        var many = Run(scene, 16, 1.75f);

        Assert.Equal(single.Output, several.Output);
        Assert.Equal(single.Output, many.Output);
        Assert.Equal(single.Convolved, several.Convolved);
        Assert.Equal(single.Convolved, many.Convolved);
    }

    [Fact]
    public void ARegionAwayFromTheSourceIsClearedToZero()
    {
        var spectrum = new ChromaticKernelSpectrum();
        spectrum.Update(ConvolutionScene.AsymmetricKernel(3), ConvolutionScene.AsymmetricKernel(3), ConvolutionScene.AsymmetricKernel(3), 3, 64);
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
    public void ASpectrumOfAnotherSizeOrRadiusIsRejected()
    {
        var spectrum = new ChromaticKernelSpectrum();
        var kernel = ConvolutionScene.AsymmetricKernel(3);
        spectrum.Update(kernel, kernel, kernel, 3, 64);
        using var convolver = new CpuTileConvolver(1);
        var source = ConvolutionScene.Uniform(10, 10, 9);
        var output = new byte[40 * 40 * 4];

        Assert.Throws<ArgumentException>(() => convolver.Convolve(source, 0, 0, 10, 10, spectrum, TilePlan.Create(128, 3, 0, 0, 40, 40), output, 1f, null));
        Assert.Throws<ArgumentException>(() => convolver.Convolve(source, 0, 0, 10, 10, spectrum, TilePlan.Create(64, 4, 0, 0, 40, 40), output, 1f, null));
        Assert.Throws<ArgumentException>(() => convolver.Convolve(source, 0, 0, 10, 10, new ChromaticKernelSpectrum(), TilePlan.Create(64, 3, 0, 0, 40, 40), output, 1f, null));
    }
}
