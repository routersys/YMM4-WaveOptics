using ComputeWeave;

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

    static (byte[] Output, float[] Convolved) RunLight(ConvolutionScene scene, int threads, float gain)
    {
        var output = new byte[scene.RegionLength];
        var convolved = new float[scene.RegionLength];
        using var convolver = new CpuTileConvolver(threads);
        convolver.Convolve(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, scene.Plan, output, gain, convolved, scene.Light);
        return (output, convolved);
    }

    [Theory]
    [InlineData(64, 5, 150, 97, 11, 0f, 0f)]
    [InlineData(128, 23, 140, 90, 24, 0.9f, 20f)]
    [InlineData(64, 0, 70, 70, 3, 0.5f, 1000f)]
    public void ALinearLightConvolutionStaysWithinTheBoundOfTheExactCorrelation(int size, int radius, int width, int height, int margin, float threshold, float boost)
    {
        var light = new LightOptions(true, false, threshold, boost);
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(width, height, size + radius, 0.4), width, height, margin, radius, size, light);

        var (_, convolved) = RunLight(scene, 3, 1f);

        Assert.True(scene.Plan.TileCount > 1);
        Assert.InRange(scene.WorstRatio(convolved, ConvolutionBound.CpuOperationError), 0d, 1d);
    }

    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, 1f)]
    [InlineData(true, 2.5f)]
    public void TheLinearOutputIsTheTransformOfTheConvolution(bool dither, float gain)
    {
        var light = new LightOptions(true, dither, 0.8f, 8f);
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(90, 60, 9, 0.7), 90, 60, 8, 4, 64, light);

        var (output, convolved) = RunLight(scene, 2, gain);

        for (var y = 0; y < scene.Plan.RegionHeight; y++)
        {
            for (var x = 0; x < scene.Plan.RegionWidth; x++)
            {
                var index = (y * scene.Plan.RegionWidth + x) * 4;
                var expected = LightTransform.ToBytes(convolved[index], convolved[index + 1], convolved[index + 2], convolved[index + 3], gain, light, scene.Plan.RegionX + x, scene.Plan.RegionY + y);
                Assert.Equal((expected.Blue, expected.Green, expected.Red, expected.Alpha), (output[index], output[index + 1], output[index + 2], output[index + 3]));
            }
        }
    }

    [Theory]
    [InlineData(false, false, 1f)]
    [InlineData(true, false, 2.75f)]
    [InlineData(true, true, 0.3f)]
    [InlineData(false, true, 1.5f)]
    public void RenderingTheStoreMatchesTheLightConvolutionBitForBit(bool linear, bool dither, float gain)
    {
        var light = new LightOptions(linear, dither, 0.85f, linear ? 12f : 0f);
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(130, 90, 13, 0.6), 130, 90, 8, 4, 64, light);
        var (expected, _) = RunLight(scene, 3, gain);
        var (_, stored) = RunLight(scene, 3, 1f);
        var output = new byte[scene.RegionLength];
        using var convolver = new CpuTileConvolver(3);

        convolver.RenderStored(stored, scene.Plan.RegionWidth * scene.Plan.RegionHeight, gain, output, light, scene.Plan.RegionWidth, scene.Plan.RegionX, scene.Plan.RegionY);

        Assert.Equal(expected, output);
    }

    [Fact]
    public void TheLinearResultDoesNotDependOnTheNumberOfThreads()
    {
        var light = new LightOptions(true, true, 0.9f, 30f);
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(300, 170, 5, 0.6), 300, 170, 16, 9, 64, light);

        var single = RunLight(scene, 1, 1.75f);
        var many = RunLight(scene, 16, 1.75f);

        Assert.Equal(single.Output, many.Output);
        Assert.Equal(single.Convolved, many.Convolved);
    }

    [Fact]
    public void AnOpaqueWhiteSceneStaysWhiteInLinearLight()
    {
        var light = new LightOptions(true, true, 0f, 0f);
        var scene = ConvolutionScene.Create(ConvolutionScene.Uniform(120, 120, 255), 120, 120, 8, 7, 64, light);

        var (output, convolved) = RunLight(scene, 2, 1f);

        Assert.InRange(scene.WorstRatio(convolved, ConvolutionBound.CpuOperationError), 0d, 1d);
        var center = ((scene.Plan.RegionHeight / 2) * scene.Plan.RegionWidth + scene.Plan.RegionWidth / 2) * 4;
        Assert.Equal([255, 255, 255, 255], output.AsSpan(center, 4).ToArray());
    }

    [Fact]
    public void ABrightPointSpreadsFartherWithHighlights()
    {
        var pixels = new byte[61 * 61 * 4];
        var center = (30 * 61 + 30) * 4;
        pixels[center] = 255;
        pixels[center + 1] = 255;
        pixels[center + 2] = 255;
        pixels[center + 3] = 255;
        var plain = new LightOptions(true, false, 0f, 0f);
        var boosted = new LightOptions(true, false, 0.9f, 400f);
        var spectrum = new KernelSpectrum();
        spectrum.Update(ConvolutionScene.AsymmetricKernel(20), 20, 128);
        var plan = TilePlan.Create(128, 20, 0, 0, 101, 101);
        using var convolver = new CpuTileConvolver(2);
        var plainOutput = new byte[101 * 101 * 4];
        var boostedOutput = new byte[101 * 101 * 4];

        convolver.Convolve(pixels, 20, 20, 61, 61, spectrum, plan, plainOutput, 1f, null, plain);
        convolver.Convolve(pixels, 20, 20, 61, 61, spectrum, plan, boostedOutput, 1f, null, boosted);

        var plainLit = plainOutput.Where((value, index) => index % 4 == 1 && value > 0).Count();
        var boostedLit = boostedOutput.Where((value, index) => index % 4 == 1 && value > 0).Count();
        Assert.True(boostedLit > plainLit * 2, $"{plainLit} {boostedLit}");
    }

    [Fact]
    public void ARegionWithDitheringNeedsAWidthToRenderTheStore()
    {
        using var convolver = new CpuTileConvolver(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.RenderStored(new float[8], 2, 1f, new byte[8], new LightOptions(false, true, 0f, 0f)));
        convolver.RenderStored(new float[8], 2, 1f, new byte[8], new LightOptions(false, true, 0f, 0f), 2);
    }

    [Fact]
    public void InvalidLightOptionsAreRejected()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(40, 40, 1, 0.5), 40, 40, 4, 2, 64);
        using var convolver = new CpuTileConvolver(1);
        var output = new byte[scene.RegionLength];

        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.Convolve(
            scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, scene.Plan, output, 1f, null, new LightOptions(true, false, 2f, 5f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.RenderStored(new float[8], 2, 1f, output, new LightOptions(true, false, 0.5f, 5000f)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADitheredOpaqueWhiteSceneNeverDropsToBlackAnywhere(bool linear)
    {
        var light = new LightOptions(linear, true, 0f, 0f);
        var scene = ConvolutionScene.Create(ConvolutionScene.Uniform(1100, 700, 255), 1100, 700, 8, 3, 512, light);

        var (output, _) = RunLight(scene, 4, 1f);

        var wrong = 0;
        for (var y = 8 + 4; y < 8 + 700 - 4; y++)
        {
            for (var x = 8 + 4; x < 8 + 1100 - 4; x++)
            {
                var index = (y * scene.Plan.RegionWidth + x) * 4;
                if (output[index] < 250 || output[index + 1] < 250 || output[index + 2] < 250 || output[index + 3] < 250)
                    wrong++;
            }
        }

        Assert.Equal(0, wrong);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void TheVectorButterfliesMatchTheScalarOnesBitForBit(int log2)
    {
        var size = 1 << log2;
        var twiddles = new Float2[Math.Max(size / 2, 1)];
        for (var index = 0; index < twiddles.Length; index++)
        {
            var angle = -2d * Math.PI * index / size;
            twiddles[index] = new Float2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        var random = new Random(log2 * 101);
        foreach (var direction in new[] { 1f, -1f })
        {
            var expected = new float[size * 4];
            for (var index = 0; index < expected.Length; index++)
            {
                expected[index] = random.Next(7) switch
                {
                    0 => 0f,
                    1 => -0f,
                    2 => (float)(random.NextDouble() * 1e-30),
                    3 => (float)(random.NextDouble() * 1e6 - 5e5),
                    _ => (float)(random.NextDouble() * 2 - 1),
                };
            }

            var actual = (float[])expected.Clone();

            var narrow = (float[])expected.Clone();

            CpuTileConvolver.ButterfliesScalar(expected, log2, twiddles, direction);
            CpuTileConvolver.Butterflies(actual, log2, twiddles, direction);
            CpuTileConvolver.Butterflies(narrow, log2, twiddles, direction, allowWide: false);

            Assert.Equal(expected.Select(BitConverter.SingleToInt32Bits), actual.Select(BitConverter.SingleToInt32Bits));
            Assert.Equal(expected.Select(BitConverter.SingleToInt32Bits), narrow.Select(BitConverter.SingleToInt32Bits));
        }
    }

    [Fact]
    public void TheButterfliesRefuseALineThatIsTooShort()
    {
        var twiddles = new Float2[4];

        Assert.Throws<ArgumentException>(() => CpuTileConvolver.Butterflies(new float[8 * 4 - 1], 3, twiddles, 1f));
        Assert.Throws<ArgumentException>(() => CpuTileConvolver.Butterflies(new float[8 * 4], 3, twiddles.AsSpan(0, 3), 1f));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(0f)]
    [InlineData(1.75f)]
    [InlineData(400f)]
    public void TheStoredValuesBecomeBytesLikeTheScalarConversion(float gain)
    {
        float[] edges =
        [
            0f, -0f, 1e-12f, 0.5f / 255f, 0.5f / 255f + 1e-7f, 1f / 255f, 127.5f / 255f, 254.5f / 255f, 254.5f / 255f + 1e-7f,
            0.999f, 1f, 1.0000001f, 2f, 1e9f, -1e-12f, -0.25f, -1e9f, float.NaN, float.PositiveInfinity, float.NegativeInfinity,
            float.Epsilon, float.MaxValue, float.MinValue,
        ];
        var pixels = edges.Length;
        var stored = new float[pixels * 4];
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            for (var channel = 0; channel < 4; channel++)
                stored[pixel * 4 + channel] = edges[(pixel + channel * 5) % edges.Length];
        }

        var output = new byte[stored.Length];
        using var convolver = new CpuTileConvolver(2);

        convolver.RenderStored(stored, pixels, gain, output);

        for (var pixel = 0; pixel < pixels; pixel++)
        {
            Assert.Equal(CpuTileConvolver.ToUnorm(stored[pixel * 4 + 2] * gain), output[pixel * 4]);
            Assert.Equal(CpuTileConvolver.ToUnorm(stored[pixel * 4 + 1] * gain), output[pixel * 4 + 1]);
            Assert.Equal(CpuTileConvolver.ToUnorm(stored[pixel * 4] * gain), output[pixel * 4 + 2]);
            Assert.Equal(CpuTileConvolver.ToUnorm(stored[pixel * 4 + 3] * gain), output[pixel * 4 + 3]);
        }
    }

    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, 1f)]
    [InlineData(true, 1.8f)]
    [InlineData(false, 2.5f)]
    public void ADitheredTranslucentSceneKeepsEveryColorBelowItsOpacity(bool linear, float gain)
    {
        var light = new LightOptions(linear, true, 0f, 0f);
        var scene = ConvolutionScene.Create(ConvolutionScene.Uniform(240, 160, 128), 240, 160, 8, 3, 64, light);

        var (output, _) = RunLight(scene, 3, gain);

        var exceeding = 0;
        var translucent = 0;
        for (var index = 0; index < output.Length; index += 4)
        {
            if (output[index + 3] is > 0 and < 255)
                translucent++;
            if (output[index] > output[index + 3] || output[index + 1] > output[index + 3] || output[index + 2] > output[index + 3])
                exceeding++;
        }

        Assert.True(translucent > 1000, $"{translucent}");
        Assert.Equal(0, exceeding);
    }

    [Theory]
    [InlineData(true, true, 1.3f)]
    [InlineData(false, true, 2f)]
    [InlineData(true, false, 0.8f)]
    public void RenderingTheStoreMatchesTheLightConvolutionForARegionAwayFromTheOrigin(bool linear, bool dither, float gain)
    {
        var light = new LightOptions(linear, dither, 0.85f, linear ? 12f : 0f);
        var source = ConvolutionScene.RandomSource(120, 80, 78, 0.5);
        var scene = ConvolutionScene.CreateShifted(source, 43, 29, 120, 80, 37, 23, 132, 92, 4, 64, light);
        var plan = scene.Plan;
        var (expected, _) = RunLight(scene, 3, gain);
        var (_, stored) = RunLight(scene, 3, 1f);
        var output = new byte[scene.RegionLength];
        using var convolver = new CpuTileConvolver(3);

        convolver.RenderStored(stored, plan.RegionWidth * plan.RegionHeight, gain, output, light, plan.RegionWidth, plan.RegionX, plan.RegionY);

        Assert.Equal(expected, output);
    }

    [Theory]
    [InlineData(true, 1f)]
    [InlineData(false, 1f)]
    public void ADitheredRegionAwayFromTheOriginUsesTheCanvasPosition(bool linear, float gain)
    {
        var light = new LightOptions(linear, true, 0f, 0f);
        var source = ConvolutionScene.RandomSource(120, 80, 79, 0.5);
        var scene = ConvolutionScene.CreateShifted(source, 43, 29, 120, 80, 37, 23, 132, 92, 4, 64, light);
        var plan = scene.Plan;
        var (output, convolved) = RunLight(scene, 3, gain);

        for (var y = 0; y < plan.RegionHeight; y += 3)
        {
            for (var x = 0; x < plan.RegionWidth; x += 2)
            {
                var index = (y * plan.RegionWidth + x) * 4;
                var expected = LightTransform.ToBytes(convolved[index], convolved[index + 1], convolved[index + 2], convolved[index + 3], gain, light, plan.RegionX + x, plan.RegionY + y);
                Assert.Equal((expected.Blue, expected.Green, expected.Red, expected.Alpha), (output[index], output[index + 1], output[index + 2], output[index + 3]));
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenderingASeveralChunkStoreAwayFromTheOriginKeepsEveryPosition(bool linear)
    {
        var light = new LightOptions(linear, true, 0.85f, linear ? 12f : 0f);
        var source = ConvolutionScene.RandomSource(100, 60, 80, 0.5);
        var scene = ConvolutionScene.CreateShifted(source, 103, 59, 100, 60, 97, 53, 313, 207, 4, 64, light);
        var plan = scene.Plan;
        Assert.True(plan.RegionWidth * plan.RegionHeight > CpuTileConvolver.StoredChunkPixels * 3);
        Assert.NotEqual(0, CpuTileConvolver.StoredChunkPixels % plan.RegionWidth);
        var (expected, _) = RunLight(scene, 3, 1.2f);
        var (_, stored) = RunLight(scene, 3, 1f);
        var output = new byte[scene.RegionLength];
        using var convolver = new CpuTileConvolver(4);

        convolver.RenderStored(stored, plan.RegionWidth * plan.RegionHeight, 1.2f, output, light, plan.RegionWidth, plan.RegionX, plan.RegionY);

        Assert.Equal(expected, output);
    }
}
