using System.Runtime.InteropServices;
using WaveOptics.Effects;
using WaveOptics.Rendering;

namespace WaveOptics.Tests;

[Collection("Direct3D12")]
public sealed class WaveOpticsCpuPipelineTests
{
    const int Opaque = unchecked((int)0xFFC0C0C0);

    static WaveOpticsPipeline.Parameters Parameters(float gain = 1f, float defocus = 0f)
        => new(gain, new WaveOpticsPipeline.PsfParameters(
            WaveOpticsQuality.Standard, 15, 550f, 8f, 4f, WaveOpticsApertureShape.Circular, 6, 0f, 0f, defocus, 0f, 0f, 0f, 0f, 0f));

    static int[] Square(int width, int height, int left, int top, int size)
    {
        var pixels = new int[width * height];
        for (var y = top; y < top + size; y++)
        {
            for (var x = left; x < left + size; x++)
                pixels[y * width + x] = Opaque;
        }

        return pixels;
    }

    static byte[] Bytes(int[] pixels) => MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();

    static byte[] RenderFrame(WaveOpticsCpuPipeline pipeline, byte[] source, int width, int height, int margin, in WaveOpticsPipeline.Parameters parameters, out WaveOpticsPipeline.PixelRect rect)
    {
        pipeline.Simulate(source, width + margin * 2, height + margin * 2, margin, margin, width, height, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(width + margin * 2, height + margin * 2, in parameters, out rect));
        return pipeline.RenderVisible(rect, in parameters).ToArray();
    }

    [Fact]
    public void TheCpuAndTheGpuDrawTheSameImageWithinTwoLevels()
    {
        using var cpu = new WaveOpticsCpuPipeline(3);
        using var gpu = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(gpu);
        var source = Square(128, 128, 40, 44, 40);
        var parameters = Parameters(defocus: 0.7f);
        var expected = new int[source.Length];
        var actual = new int[source.Length];

        gpu.Process(source, expected, 128, 128, in parameters);
        cpu.Process(source, actual, 128, 128, in parameters);

        var expectedBytes = MemoryMarshal.AsBytes(expected.AsSpan());
        var actualBytes = MemoryMarshal.AsBytes(actual.AsSpan());
        for (var index = 0; index < expectedBytes.Length; index++)
            Assert.InRange(actualBytes[index] - expectedBytes[index], -2, 2);
        Assert.Contains(actualBytes.ToArray(), value => value > 0);
    }

    [Fact]
    public void TheImageDoesNotDependOnTheNumberOfThreads()
    {
        using var single = new WaveOpticsCpuPipeline(1);
        using var several = new WaveOpticsCpuPipeline(5);
        var source = Bytes(Square(150, 120, 30, 20, 70));
        var parameters = Parameters(gain: 1.5f, defocus: 1f);

        var first = RenderFrame(single, source, 150, 120, 16, in parameters, out _);
        var second = RenderFrame(several, source, 150, 120, 16, in parameters, out _);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheConvolutionIsRecomputedOnlyWhenTheSourceOrTheOpticsChange()
    {
        using var pipeline = new WaveOpticsCpuPipeline(2);
        var source = Bytes(Square(96, 96, 32, 32, 32));
        var parameters = Parameters();

        Assert.True(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in parameters));
        Assert.False(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in parameters));
        var gainChanged = parameters with { Gain = 2f };
        Assert.False(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in gainChanged));
        var bladesChanged = parameters with { Psf = parameters.Psf with { BladeCount = 9 } };
        Assert.False(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in bladesChanged));
        var opticsChanged = parameters with { Psf = parameters.Psf with { Defocus = 0.5f } };
        Assert.True(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in opticsChanged));
        var moved = Bytes(Square(96, 96, 31, 32, 32));
        Assert.True(pipeline.Simulate(moved, 128, 128, 16, 16, 96, 96, in opticsChanged));
    }

    [Fact]
    public void ChangingOnlyTheGainDrawsTheSameImageAsAFreshConvolution()
    {
        using var pipeline = new WaveOpticsCpuPipeline(2);
        var source = Bytes(Square(96, 96, 30, 28, 36));
        var first = Parameters(defocus: 0.5f);
        RenderFrame(pipeline, source, 96, 96, 16, in first, out var rect);

        foreach (var gain in new[] { 2.5f, 0.75f, 1f })
        {
            var changed = first with { Gain = gain };
            Assert.False(pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in changed));
            var reused = pipeline.RenderVisible(rect, in changed).ToArray();
            using var fresh = new WaveOpticsCpuPipeline(2);
            var expected = RenderFrame(fresh, source, 96, 96, 16, in changed, out _);

            Assert.True(pipeline.HasStore);
            Assert.Equal(expected, reused);
        }
    }

    [Fact]
    public void ANewSourceLetsTheStoreGo()
    {
        using var pipeline = new WaveOpticsCpuPipeline(2);
        var source = Bytes(Square(96, 96, 30, 28, 36));
        var parameters = Parameters();
        RenderFrame(pipeline, source, 96, 96, 16, in parameters, out var rect);
        var brighter = parameters with { Gain = 2f };
        pipeline.Simulate(source, 128, 128, 16, 16, 96, 96, in brighter);
        pipeline.RenderVisible(rect, in brighter);
        Assert.True(pipeline.HasStore);

        RenderFrame(pipeline, Bytes(Square(96, 96, 20, 28, 36)), 96, 96, 16, in brighter, out _);

        Assert.False(pipeline.HasStore);
    }

    [Fact]
    public void NothingIsVisibleForATransparentSource()
    {
        using var pipeline = new WaveOpticsCpuPipeline(1);
        var parameters = Parameters();

        Assert.False(pipeline.Simulate(new byte[64 * 64 * 4], 96, 96, 16, 16, 64, 64, in parameters));
        Assert.False(pipeline.TryGetVisibleBounds(96, 96, in parameters, out _));
    }

    [Fact]
    public void AClosedApertureLeavesNoKernel()
    {
        using var pipeline = new WaveOpticsCpuPipeline(1);
        var source = Bytes(Square(64, 64, 20, 20, 20));
        var open = Parameters();
        var closed = open with { Psf = open.Psf with { Quality = WaveOpticsQuality.Draft, ApertureShape = WaveOpticsApertureShape.RegularPolygon, BladeCount = 4, BladeRotation = -357.5f, Obstruction = 0.95f } };

        Assert.False(pipeline.Simulate(source, 96, 96, 16, 16, 64, 64, in closed));
        Assert.False(pipeline.HasKernel);
    }

    [Fact]
    public void AWarmFrameAllocatesNothingOnTheCallingThread()
    {
        using var pipeline = new WaveOpticsCpuPipeline(3);
        var sources = new[] { Bytes(Square(96, 96, 30, 28, 36)), Bytes(Square(96, 96, 31, 28, 36)) };
        var parameters = Parameters();
        for (var warmUp = 0; warmUp < 4; warmUp++)
            RenderFrame(pipeline, sources[warmUp % 2], 96, 96, 16, in parameters, out _);

        void RenderFrames()
        {
            for (var frame = 0; frame < 8; frame++)
            {
                pipeline.Simulate(sources[frame % 2], 128, 128, 16, 16, 96, 96, in parameters);
                pipeline.TryGetVisibleBounds(128, 128, in parameters, out var rect);
                pipeline.RenderVisible(rect, in parameters);
            }
        }

        RenderFrames();
        AllocationProbe.Settle();

        Assert.Equal(0L, AllocationProbe.MinimumAllocatedBytes(RenderFrames, 8));
    }

    [Fact]
    public void RenderingBeforeAConvolutionIsRejected()
    {
        using var pipeline = new WaveOpticsCpuPipeline(1);
        var parameters = Parameters();

        Assert.Throws<InvalidOperationException>(() => pipeline.RenderVisible(new WaveOpticsPipeline.PixelRect(0, 0, 4, 4), in parameters));
    }
}
