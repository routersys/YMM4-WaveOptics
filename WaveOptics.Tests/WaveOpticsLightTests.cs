using ComputeWeave;
using System.Runtime.InteropServices;
using SpectralConvolution;
using WaveOptics.Effects;
using WaveOptics.Rendering;

namespace WaveOptics.Tests;

[Collection("Direct3D12")]
public sealed class WaveOpticsLightTests
{
    const int Black = unchecked((int)0xFF000000);
    const int White = unchecked((int)0xFFFFFFFF);

    static WaveOpticsPipeline.Parameters Parameters(LightOptions light = default, float gain = 1f, float defocus = 0f, int kernelRadius = 15)
        => new(gain, new WaveOpticsPipeline.PsfParameters(
            WaveOpticsQuality.Standard, kernelRadius, 550f, 8f, 4f, WaveOpticsApertureShape.Circular, 6, 0f, 0f, defocus, 0f, 0f, 0f, 0f, 0f), light);

    static int[] Checkerboard(int width, int height)
    {
        var pixels = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                pixels[y * width + x] = (x + y & 1) == 0 ? White : Black;
        }

        return pixels;
    }

    static int[] BrightPoint(int width, int height)
    {
        var pixels = new int[width * height];
        Array.Fill(pixels, Black);
        pixels[(height / 2) * width + width / 2] = White;
        return pixels;
    }

    static byte[] Bytes(int[] pixels) => MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();

    static int GreenAt(int[] pixels, int width, int x, int y) => (pixels[y * width + x] >> 8) & 255;

    static int LitCount(int[] pixels) => pixels.Count(pixel => ((pixel >> 8) & 255) > 0);

    static int[] RenderGpu(int[] source, int width, int height, in WaveOpticsPipeline.Parameters parameters)
    {
        using var pipeline = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(pipeline);
        var destination = new int[source.Length];
        pipeline.Process(source, destination, width, height, in parameters);
        return destination;
    }

    static int[] RenderCpu(int[] source, int width, int height, in WaveOpticsPipeline.Parameters parameters)
    {
        using var pipeline = new WaveOpticsCpuPipeline(3);
        var destination = new int[source.Length];
        pipeline.Process(source, destination, width, height, in parameters);
        return destination;
    }

    [Fact]
    public void LinearLightAveragesABlackAndWhiteCheckerboardByLightIntensity()
    {
        var source = Checkerboard(160, 160);
        var plain = Parameters(defocus: 3f, kernelRadius: 40);
        var linear = Parameters(new LightOptions(true, false, 0f, 0f), defocus: 3f, kernelRadius: 40);

        foreach (var render in new Func<int[], WaveOpticsPipeline.Parameters, int[]>[]
        {
            (pixels, parameters) => RenderGpu(pixels, 160, 160, in parameters),
            (pixels, parameters) => RenderCpu(pixels, 160, 160, in parameters),
        })
        {
            var gamma = GreenAt(render(source, plain), 160, 80, 80);
            var light = GreenAt(render(source, linear), 160, 80, 80);

            Assert.InRange(gamma, 116, 140);
            Assert.InRange(light, 176, 200);
        }
    }

    [Fact]
    public void HighlightsSpreadABrightPointFartherOnBothRoutes()
    {
        var source = BrightPoint(121, 121);
        var plain = Parameters(new LightOptions(true, false, 0f, 0f), kernelRadius: 20);
        var boosted = Parameters(new LightOptions(true, false, 0.9f, 400f), kernelRadius: 20);

        var gpuPlain = LitCount(RenderGpu(source, 121, 121, in plain));
        var gpuBoosted = LitCount(RenderGpu(source, 121, 121, in boosted));
        var cpuPlain = LitCount(RenderCpu(source, 121, 121, in plain));
        var cpuBoosted = LitCount(RenderCpu(source, 121, 121, in boosted));

        Assert.True(gpuBoosted > gpuPlain * 2, $"{gpuPlain} {gpuBoosted}");
        Assert.True(cpuBoosted > cpuPlain * 2, $"{cpuPlain} {cpuBoosted}");
    }

    [Theory]
    [InlineData(false, true, 0f, 0f)]
    [InlineData(true, false, 0f, 0f)]
    [InlineData(true, true, 0.8f, 10f)]
    public void TheCpuAndTheGpuDrawTheSameLightImageWithinTwoLevels(bool linear, bool dither, float threshold, float boost)
    {
        var random = new Random(9);
        var source = new int[128 * 128];
        for (var index = 0; index < source.Length; index++)
        {
            if (random.NextDouble() < 0.25)
            {
                var value = random.Next(40, 256);
                source[index] = unchecked((int)(0xFF000000u | (uint)value << 16 | (uint)Math.Max(0, value - random.Next(60)) << 8 | (uint)Math.Max(0, value - random.Next(120))));
            }
            else
                source[index] = Black;
        }

        var parameters = Parameters(new LightOptions(linear, dither, threshold, boost), 1.3f, 0.7f);

        var expected = Bytes(RenderGpu(source, 128, 128, in parameters));
        var actual = Bytes(RenderCpu(source, 128, 128, in parameters));

        for (var index = 0; index < expected.Length; index++)
            Assert.InRange(actual[index] - expected[index], -2, 2);
        Assert.Contains(actual, value => value > 0);
    }

    [Fact]
    public void DitheringChangesTheOutputByAtMostOneLevelAndRepeats()
    {
        var source = BrightPoint(121, 121);
        var plain = Parameters(defocus: 1f);
        var dithered = Parameters(new LightOptions(false, true, 0f, 0f), defocus: 1f);

        var rounded = Bytes(RenderCpu(source, 121, 121, in plain));
        var first = Bytes(RenderCpu(source, 121, 121, in dithered));
        var second = Bytes(RenderCpu(source, 121, 121, in dithered));

        Assert.Equal(first, second);
        Assert.NotEqual(rounded, first);
        for (var index = 0; index < rounded.Length; index++)
            Assert.InRange(first[index] - rounded[index], -1, 1);
    }

    [Fact]
    public void ChangingOnlyTheDitherDrawsFromTheStoreAndMatchesAFreshConvolution()
    {
        using var pipeline = new WaveOpticsCpuPipeline(2);
        var source = Bytes(BrightPoint(96, 96));
        var plain = Parameters(new LightOptions(true, false, 0.9f, 30f), defocus: 0.5f);
        pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in plain);
        Assert.True(pipeline.TryGetVisibleBounds(128, 128, in plain, out var rect));
        pipeline.RenderVisible(rect, in plain);

        foreach (var dither in new[] { true, false, true })
        {
            var changed = plain with { Light = plain.Light with { Dither = dither } };
            Assert.False(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in changed));
            var reused = pipeline.RenderVisible(rect, in changed).ToArray();
            using var fresh = new WaveOpticsCpuPipeline(2);
            fresh.Simulate(source, 128, 128, 16, 16, 96, 96, in changed);
            fresh.TryGetVisibleBounds(128, 128, in changed, out var freshRect);
            var expected = fresh.RenderVisible(freshRect, in changed).ToArray();

            Assert.True(pipeline.HasStore);
            Assert.Equal(expected, reused);
        }
    }

    [Fact]
    public void TheConvolutionIsRecomputedOnlyWhenTheLightSettingsAffectTheInput()
    {
        using var pipeline = new WaveOpticsCpuPipeline(2);
        var source = Bytes(BrightPoint(96, 96));
        var parameters = Parameters();

        Assert.True(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in parameters));
        var dither = parameters with { Light = new LightOptions(false, true, 0.9f, 20f) };
        Assert.False(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in dither));
        var linear = parameters with { Light = new LightOptions(true, false, 0.9f, 20f) };
        Assert.True(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in linear));
        var dithered = linear with { Light = linear.Light with { Dither = true } };
        Assert.False(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in dithered));
        var otherBoost = linear with { Light = linear.Light with { Boost = 40f } };
        Assert.True(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in otherBoost));
        var unusedBoost = otherBoost with { Light = new LightOptions(true, false, 0.9f, 1f) };
        Assert.True(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in unusedBoost));
        var unusedThreshold = unusedBoost with { Light = unusedBoost.Light with { Threshold = 0.5f } };
        Assert.False(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in unusedThreshold));
    }

    [Fact]
    public void TheGpuRedrawsFromTheStoreWhenOnlyTheDitherChanges()
    {
        using var pipeline = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(pipeline);
        var device = ComputeWeave.GraphicsDevice.GetDefault();
        var source = BrightPoint(96, 96);
        using var texture = device.AllocateReadWriteTexture2D<ComputeWeave.Bgra32, ComputeWeave.Float4>(96, 96);
        texture.CopyFrom(source.Select(pixel => new ComputeWeave.Bgra32 { PackedValue = unchecked((uint)pixel) }).ToArray());
        var plain = Parameters(new LightOptions(true, false, 0.9f, 30f), defocus: 0.5f);

        int[] Draw(WaveOpticsPipeline.Parameters parameters, out bool convolved)
        {
            convolved = pipeline.Simulate(texture, 128, 128, 16, 16, 96, 96, in parameters);
            Assert.True(pipeline.TryGetVisibleBounds(128, 128, in parameters, out var rect));
            using var output = device.AllocateReadWriteTexture2D<ComputeWeave.Bgra32, ComputeWeave.Float4>(rect.Width, rect.Height);
            pipeline.RenderVisible(texture, output, rect, in parameters);
            var pixels = new ComputeWeave.Bgra32[rect.Width * rect.Height];
            output.CopyTo(pixels);
            return pixels.Select(pixel => unchecked((int)pixel.PackedValue)).ToArray();
        }

        var first = Draw(plain, out var firstConvolved);
        var dithered = plain with { Light = plain.Light with { Dither = true } };
        var second = Draw(dithered, out var secondConvolved);
        var third = Draw(plain, out var thirdConvolved);

        Assert.True(firstConvolved);
        Assert.False(secondConvolved);
        Assert.False(thirdConvolved);
        Assert.NotEqual(first, second);
        Assert.Equal(first, third);
    }

    [Fact]
    public void AWarmLightPipelineAllocatesNoManagedMemory()
    {
        using var pipeline = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(pipeline);
        var source = BrightPoint(64, 64);
        var destination = new int[source.Length];
        var parameters = Parameters(new LightOptions(true, true, 0.9f, 30f));
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, 64, 64, in parameters);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, 64, 64, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, minimum);
    }
}
