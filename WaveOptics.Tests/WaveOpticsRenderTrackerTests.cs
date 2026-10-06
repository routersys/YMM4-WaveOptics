using WaveOptics.Effects;
using WaveOptics.Rendering;

namespace WaveOptics.Tests;

public sealed class WaveOpticsRenderTrackerTests
{
    static readonly WaveOpticsPipeline.PsfParameters Psf = new(
        WaveOpticsQuality.Standard, 15, 550f, 8f, 4f, WaveOpticsApertureShape.Circular, 6, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

    static readonly WaveOpticsConvolutionKey First = new(1, 2, 100, 100, new WaveOpticsPipeline.PixelRect(16, 16, 68, 68), Psf);

    static readonly WaveOpticsConvolutionKey Second = First with { HashSum = 3 };

    static readonly WaveOpticsPipeline.PixelRect Rect = new(0, 0, 100, 100);

    [Fact]
    public void ANewConvolutionIsComputedWithoutAStore()
    {
        var tracker = new WaveOpticsRenderTracker();

        Assert.Equal(WaveOpticsRenderMode.Convolve, tracker.Next(First, Rect, 1f, out var release));

        Assert.False(release);
        Assert.False(tracker.HasStore);
    }

    [Fact]
    public void TheFirstGainChangeStoresAndLaterOnesDrawFromTheStore()
    {
        var tracker = new WaveOpticsRenderTracker();
        tracker.Next(First, Rect, 1f, out _);

        Assert.Equal(WaveOpticsRenderMode.ConvolveAndStore, tracker.Next(First, Rect, 1.5f, out _));
        Assert.True(tracker.HasStore);
        Assert.Equal(WaveOpticsRenderMode.Stored, tracker.Next(First, Rect, 2f, out _));
        Assert.Equal(WaveOpticsRenderMode.Stored, tracker.Next(First, Rect, 2f, out _));
    }

    [Fact]
    public void RedrawingTheSameFrameWithoutAStoreConvolvesAgain()
    {
        var tracker = new WaveOpticsRenderTracker();
        tracker.Next(First, Rect, 1f, out _);

        Assert.Equal(WaveOpticsRenderMode.Convolve, tracker.Next(First, Rect, 1f, out _));
        Assert.False(tracker.HasStore);
    }

    [Fact]
    public void ANewSourceOrRectangleLetsTheStoreGo()
    {
        var tracker = new WaveOpticsRenderTracker();
        tracker.Next(First, Rect, 1f, out _);
        tracker.Next(First, Rect, 2f, out _);

        Assert.Equal(WaveOpticsRenderMode.Convolve, tracker.Next(Second, Rect, 2f, out var releasedForSource));
        Assert.True(releasedForSource);
        Assert.False(tracker.HasStore);

        tracker.Next(Second, Rect, 3f, out _);
        Assert.Equal(WaveOpticsRenderMode.Convolve, tracker.Next(Second, Rect with { Width = 96 }, 3f, out var releasedForRect));
        Assert.True(releasedForRect);
    }

    [Fact]
    public void AResetForgetsTheFrameAndReportsTheStore()
    {
        var tracker = new WaveOpticsRenderTracker();
        tracker.Next(First, Rect, 1f, out _);
        tracker.Next(First, Rect, 2f, out _);

        Assert.True(tracker.Reset());
        Assert.False(tracker.Reset());
        Assert.Equal(WaveOpticsRenderMode.Convolve, tracker.Next(First, Rect, 2f, out var release));
        Assert.False(release);
    }

    [Fact]
    public void TheSamplingKeyComesFromTheSourceHash()
    {
        Assert.Equal(1UL << 32 | 2UL, First.SamplingKey);
        Assert.NotEqual(First.SamplingKey, Second.SamplingKey);
        Assert.Equal(First.SamplingKey, (First with { Psf = Psf with { Defocus = 1f } }).SamplingKey);
    }

    [Fact]
    public void ChangingTheDitherStoresLikeAGainChange()
    {
        var tracker = new WaveOpticsRenderTracker();
        tracker.Next(First, Rect, 1f, false, out _);

        Assert.Equal(WaveOpticsRenderMode.ConvolveAndStore, tracker.Next(First, Rect, 1f, true, out _));
        Assert.True(tracker.HasStore);
        Assert.Equal(WaveOpticsRenderMode.Stored, tracker.Next(First, Rect, 1f, false, out _));
        Assert.Equal(WaveOpticsRenderMode.Stored, tracker.Next(First, Rect, 1f, false, out _));
    }

    [Fact]
    public void TheSameDitherWithoutAStoreConvolvesAgain()
    {
        var tracker = new WaveOpticsRenderTracker();
        tracker.Next(First, Rect, 1f, true, out _);

        Assert.Equal(WaveOpticsRenderMode.Convolve, tracker.Next(First, Rect, 1f, true, out _));
        Assert.False(tracker.HasStore);
    }

    [Fact]
    public void AnotherLightSettingIsAnotherConvolution()
    {
        var tracker = new WaveOpticsRenderTracker();
        tracker.Next(First, Rect, 1f, out _);
        tracker.Next(First, Rect, 1.5f, out _);
        var light = First with { Light = new SpectralConvolution.LightOptions(true, false, 0.9f, 10f) };

        Assert.Equal(WaveOpticsRenderMode.Convolve, tracker.Next(light, Rect, 1.5f, out var release));
        Assert.True(release);
        Assert.False(tracker.HasStore);
    }
}
