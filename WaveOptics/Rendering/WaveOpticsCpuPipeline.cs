using System.Runtime.InteropServices;
using SpectralConvolution;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsCpuPipeline : IDisposable
{
    private const int Channels = 4;

    private readonly CpuTileConvolver _convolver;
    private readonly WaveOpticsKernel _kernel = new();
    private readonly WaveOpticsRenderTracker _tracker = new();
    private WaveOpticsConvolutionKey? _convolutionKey;
    private WaveOpticsSourceHash _sourceHash = WaveOpticsSourceHash.Empty;
    private WaveOpticsPipeline.PixelRect _sourceRect;
    private int _canvasWidth;
    private int _canvasHeight;
    private byte[] _source = [];
    private byte[] _packed = [];
    private byte[] _output = [];
    private float[] _store = [];

    public WaveOpticsCpuPipeline()
        : this(Environment.ProcessorCount)
    {
    }

    public WaveOpticsCpuPipeline(int threads)
    {
        _convolver = new CpuTileConvolver(threads);
    }

    public bool HasKernel => _kernel.IsValid;

    public bool HasStore => _tracker.HasStore;

    public WaveOpticsSourceHash SourceHash => _sourceHash;

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in WaveOpticsPipeline.Parameters parameters)
    {
        var length = checked(width * height * Channels);
        if (!_kernel.TryUpdate(parameters.Psf))
            throw new InvalidOperationException();
        if (_packed.Length < length)
            _packed = new byte[length];
        if (_output.Length < length)
            _output = new byte[length];
        MemoryMarshal.AsBytes(source[..(width * height)]).CopyTo(_packed);
        var plan = TilePlan.Create(_kernel.Spectrum.Size, _kernel.Spectrum.Radius, 0, 0, width, height);
        _convolver.Convolve(_packed, 0, 0, width, height, _kernel.Spectrum, plan, _output, parameters.Gain, null);
        _output.AsSpan(0, length).CopyTo(MemoryMarshal.AsBytes(destination[..(width * height)]));
    }

    public bool Simulate(
        byte[] source,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in WaveOpticsPipeline.Parameters parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_canvasWidth != canvasWidth || _canvasHeight != canvasHeight)
        {
            _canvasWidth = canvasWidth;
            _canvasHeight = canvasHeight;
            _convolutionKey = null;
            if (_tracker.Reset())
                _store = [];
        }

        _source = source;
        _sourceHash = WaveOpticsSourceHash.Compute(source, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight);
        var sourceRect = new WaveOpticsPipeline.PixelRect(sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight);
        var key = new WaveOpticsConvolutionKey(_sourceHash.Sum, _sourceHash.Mix, canvasWidth, canvasHeight, sourceRect, WaveOpticsKernel.KeyOf(parameters.Psf));
        if (_convolutionKey == key || !TryGetVisibleBounds(canvasWidth, canvasHeight, in parameters, out _))
            return false;

        if (!_kernel.TryUpdate(parameters.Psf))
        {
            _convolutionKey = null;
            return false;
        }

        _convolutionKey = key;
        _sourceRect = sourceRect;
        return true;
    }

    public bool TryGetVisibleBounds(int canvasWidth, int canvasHeight, in WaveOpticsPipeline.Parameters parameters, out WaveOpticsPipeline.PixelRect rect)
        => _sourceHash.TryGetVisibleBounds(canvasWidth, canvasHeight, parameters.Psf.KernelRadius, out rect);

    public ReadOnlySpan<byte> RenderVisible(WaveOpticsPipeline.PixelRect rect, in WaveOpticsPipeline.Parameters parameters)
    {
        if (_convolutionKey is not { } key || !_kernel.IsValid)
            throw new InvalidOperationException();

        var mode = _tracker.Next(key, rect, parameters.Gain, out var releaseStore);
        if (releaseStore)
            _store = [];

        var length = checked(rect.Width * rect.Height * Channels);
        if (_output.Length < length)
            _output = new byte[length];

        if (mode == WaveOpticsRenderMode.Stored)
        {
            _convolver.RenderStored(_store, rect.Width * rect.Height, parameters.Gain, _output);
            return _output.AsSpan(0, length);
        }

        float[]? store = null;
        if (mode == WaveOpticsRenderMode.ConvolveAndStore)
        {
            if (_store.Length < length)
                _store = new float[length];
            store = _store;
        }

        var plan = TilePlan.Create(_kernel.Spectrum.Size, _kernel.Spectrum.Radius, rect.X, rect.Y, rect.Width, rect.Height);
        _convolver.Convolve(_source, _sourceRect.X, _sourceRect.Y, _sourceRect.Width, _sourceRect.Height, _kernel.Spectrum, plan, _output, parameters.Gain, store);
        return _output.AsSpan(0, length);
    }

    public void Dispose()
    {
        _convolver.Dispose();
        _source = [];
        _packed = [];
        _output = [];
        _store = [];
        _convolutionKey = null;
        _tracker.Reset();
    }
}
