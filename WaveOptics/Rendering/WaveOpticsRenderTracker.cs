namespace WaveOptics.Rendering;

internal readonly record struct WaveOpticsConvolutionKey(
    int HashSum,
    int HashMix,
    int CanvasWidth,
    int CanvasHeight,
    WaveOpticsPipeline.PixelRect Source,
    WaveOpticsPipeline.PsfParameters Psf,
    SpectralConvolution.LightOptions Light = default)
{
    public ulong SamplingKey => (ulong)(uint)HashSum << 32 | (uint)HashMix;
}

internal enum WaveOpticsRenderMode
{
    Convolve,
    ConvolveAndStore,
    Stored,
}

internal sealed class WaveOpticsRenderTracker
{
    private WaveOpticsConvolutionKey? _renderedKey;
    private WaveOpticsPipeline.PixelRect _renderedRect;
    private float _renderedGain;
    private bool _renderedDither;

    public bool HasStore { get; private set; }

    public WaveOpticsRenderMode Next(in WaveOpticsConvolutionKey key, WaveOpticsPipeline.PixelRect rect, float gain, out bool releaseStore)
        => Next(in key, rect, gain, false, out releaseStore);

    public WaveOpticsRenderMode Next(in WaveOpticsConvolutionKey key, WaveOpticsPipeline.PixelRect rect, float gain, bool dither, out bool releaseStore)
    {
        WaveOpticsRenderMode mode;
        releaseStore = false;
        if (_renderedKey != key || _renderedRect != rect)
        {
            releaseStore = HasStore;
            HasStore = false;
            mode = WaveOpticsRenderMode.Convolve;
        }
        else if (HasStore)
            mode = WaveOpticsRenderMode.Stored;
        else if (gain != _renderedGain || dither != _renderedDither)
        {
            HasStore = true;
            mode = WaveOpticsRenderMode.ConvolveAndStore;
        }
        else
            mode = WaveOpticsRenderMode.Convolve;

        _renderedKey = key;
        _renderedRect = rect;
        _renderedGain = gain;
        _renderedDither = dither;
        return mode;
    }

    public bool Reset()
    {
        var hadStore = HasStore;
        _renderedKey = null;
        HasStore = false;
        return hadStore;
    }
}
