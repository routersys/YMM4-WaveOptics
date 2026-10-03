using System.ComponentModel;
using System.Runtime.InteropServices;
using ComputeWeave;
using SpectralConvolution;
using WaveOptics.Effects;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly WaveOpticsPipelineHost _host;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private readonly GpuTileConvolver _convolver;
    private readonly WaveOpticsKernel _kernel = new();
    private int _uploadedKernelVersion;
    private ConvolutionKey? _convolutionKey;
    private ConvolutionKey? _renderedKey;
    private PixelRect _renderedRect;
    private float _renderedGain;
    private bool _storeIsValid;
    private GpuTileJob _storedJob;
    private PixelRect _sourceRect;
    private int _cachedLitCount;
    private int _cachedBoundsMinX;
    private int _cachedBoundsMinY;
    private int _cachedBoundsMaxX;
    private int _cachedBoundsMaxY;
    private int _canvasWidth;
    private int _canvasHeight;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;

    private WaveOpticsPipeline(GraphicsDevice device, WaveOpticsPipelineHost host)
    {
        _device = device;
        _host = host;
        _scratch = device.AllocateReadWriteBuffer<int>(WaveOpticsSettings.ScratchLength);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(WaveOpticsSettings.ScratchLength);
        _convolver = new GpuTileConvolver(device);
    }

    public static WaveOpticsPipeline? TryCreate()
    {
        try
        {
            return TryCreate(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static WaveOpticsPipeline? TryCreate(GraphicsDevice device)
    {
        WaveOpticsPipelineHost? host = null;
        try
        {
            host = WaveOpticsPipelineHost.Create(device, WaveOpticsSettings.MaximumPendingSubmissions);
            var pipeline = new WaveOpticsPipeline(device, host);
            host = null;
            return pipeline;
        }
        catch (Win32Exception)
        {
            return null;
        }
        finally
        {
            host?.Dispose();
            host?.WaitForDisposal();
        }
    }

    internal void WaitForCompletion()
    {
        _device.For(1, new FillIntShader(_scratch, 0, 0));
    }

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in Parameters parameters)
    {
        var pixelCount = checked(width * height);
        EnsurePackedTextures(width, height);
        var sourceTexture = _packedSource!;
        var outputTexture = _packedOutput!;
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source[..pixelCount]));
        SubmitFullPipeline(sourceTexture, outputTexture, width, height, in parameters).Wait();
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(destination[..pixelCount]));
    }

    public void Process(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        _ = SubmitFullPipeline(source, destination, width, height, in parameters);
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        SubmitFullPipeline(source, destination, width, height, in parameters).Wait();
    }

    internal bool Simulate(
        ReadWriteTexture2D<Bgra32, Float4> source,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in Parameters parameters)
    {
        var sourceRect = BeginSimulate(canvasWidth, canvasHeight, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight);
        _host.RecordSourceHash(source, _scratch, in sourceRect).Wait();
        return TryBeginConvolution(in sourceRect, in parameters);
    }

    internal bool Simulate(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> source,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in Parameters parameters)
    {
        var sourceRect = BeginSimulate(canvasWidth, canvasHeight, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight);
        _host.RecordSharedSourceHash(source, _scratch, in sourceRect).Wait();
        return TryBeginConvolution(in sourceRect, in parameters);
    }

    private PixelRect BeginSimulate(int canvasWidth, int canvasHeight, int sourceOffsetX, int sourceOffsetY, int sourceWidth, int sourceHeight)
    {
        EnsureCanvas(canvasWidth, canvasHeight);
        return new PixelRect(sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight);
    }

    private bool TryBeginConvolution(in PixelRect sourceRect, in Parameters parameters)
    {
        _scratchReadBack.CopyFrom(_scratch);
        var hashed = _scratchReadBack.Span;
        _cachedLitCount = hashed[WaveOpticsSettings.ScratchLitCount];
        _cachedBoundsMinX = hashed[WaveOpticsSettings.ScratchBoundsMinX];
        _cachedBoundsMinY = hashed[WaveOpticsSettings.ScratchBoundsMinY];
        _cachedBoundsMaxX = hashed[WaveOpticsSettings.ScratchBoundsMaxX];
        _cachedBoundsMaxY = hashed[WaveOpticsSettings.ScratchBoundsMaxY];

        var key = new ConvolutionKey(
            hashed[WaveOpticsSettings.ScratchHashSum],
            hashed[WaveOpticsSettings.ScratchHashMix],
            _canvasWidth,
            _canvasHeight,
            sourceRect,
            WaveOpticsKernel.KeyOf(parameters.Psf));
        if (_convolutionKey == key || !TryGetVisibleBounds(_canvasWidth, _canvasHeight, in parameters, out _))
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

    internal bool HasKernel => _kernel.IsValid;

    internal bool TryGetVisibleBounds(int canvasWidth, int canvasHeight, in Parameters parameters, out PixelRect rect)
    {
        rect = default;
        if (_cachedLitCount <= 0 || _cachedBoundsMinX > _cachedBoundsMaxX)
            return false;

        var radius = parameters.Psf.KernelRadius;
        var left = Math.Clamp((_cachedBoundsMinX - radius) & ~3, 0, canvasWidth);
        var top = Math.Clamp((_cachedBoundsMinY - radius) & ~3, 0, canvasHeight);
        var right = Math.Clamp(_cachedBoundsMaxX + 1 + radius, 0, canvasWidth);
        var bottom = Math.Clamp(_cachedBoundsMaxY + 1 + radius, 0, canvasHeight);
        var width = Math.Min((right - left + 3) & ~3, canvasWidth - left);
        var height = Math.Min((bottom - top + 3) & ~3, canvasHeight - top);
        if (width <= 0 || height <= 0)
            return false;

        rect = new PixelRect(left, top, width, height);
        return true;
    }

    internal void RenderVisible(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        PixelRect rect,
        in Parameters parameters)
    {
        var mode = BeginRender(rect, parameters.Gain);
        if (mode == RenderMode.Stored)
            _host.RecordStoredRender(_convolver.StoreFor(_storedJob), output, in rect, parameters.Gain).Wait();
        else
        {
            var job = PrepareConvolution(rect, parameters.Gain, mode == RenderMode.ConvolveAndStore);
            _host.RecordConvolution(
                source, output, _convolver.Tiles, _convolver.Twiddles, _convolver.Spectrum,
                _convolver.Report, _convolver.Samples, _convolver.StoreFor(job), in job).Wait();
        }
    }

    internal void RenderVisible(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> source,
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> output,
        PixelRect rect,
        in Parameters parameters)
    {
        var mode = BeginRender(rect, parameters.Gain);
        if (mode == RenderMode.Stored)
            _host.RecordSharedStoredRender(_convolver.StoreFor(_storedJob), output, in rect, parameters.Gain).Wait();
        else
        {
            var job = PrepareConvolution(rect, parameters.Gain, mode == RenderMode.ConvolveAndStore);
            _host.RecordSharedConvolution(
                source, output, _convolver.Tiles, _convolver.Twiddles, _convolver.Spectrum,
                _convolver.Report, _convolver.Samples, _convolver.StoreFor(job), in job).Wait();
        }
    }

    private RenderMode BeginRender(PixelRect rect, float gain)
    {
        if (_convolutionKey is not { } key || !_kernel.IsValid)
            throw new InvalidOperationException();

        RenderMode mode;
        if (_renderedKey != key || _renderedRect != rect)
        {
            ReleaseStore();
            mode = RenderMode.Convolve;
        }
        else if (_storeIsValid)
            mode = RenderMode.Stored;
        else
            mode = gain != _renderedGain ? RenderMode.ConvolveAndStore : RenderMode.Convolve;

        _renderedKey = key;
        _renderedRect = rect;
        _renderedGain = gain;
        return mode;
    }

    private GpuTileJob PrepareConvolution(PixelRect rect, float gain, bool store)
    {
        if (_uploadedKernelVersion != _kernel.Version)
        {
            _convolver.Upload(_kernel.Spectrum);
            _uploadedKernelVersion = _kernel.Version;
        }

        var plan = TilePlan.Create(_kernel.Spectrum.Size, _kernel.Spectrum.Radius, rect.X, rect.Y, rect.Width, rect.Height);
        var job = _convolver.Prepare(plan, _sourceRect.X, _sourceRect.Y, _sourceRect.Width, _sourceRect.Height, gain, [], store);
        if (store)
        {
            _storedJob = job;
            _storeIsValid = true;
        }

        return job;
    }

    private void ReleaseStore()
    {
        if (!_storeIsValid)
            return;
        _convolver.ReleaseStore();
        _storeIsValid = false;
        _storedJob = default;
    }

    private ComputeSubmission SubmitFullPipeline(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureCanvas(width, height);
        if (!_kernel.TryUpdate(parameters.Psf))
            throw new InvalidOperationException();
        _convolutionKey = null;
        _renderedKey = null;
        ReleaseStore();
        _sourceRect = new PixelRect(0, 0, width, height);
        var job = PrepareConvolution(_sourceRect, parameters.Gain, false);
        return _host.RecordConvolution(
            source, output, _convolver.Tiles, _convolver.Twiddles, _convolver.Spectrum,
            _convolver.Report, _convolver.Samples, _convolver.StoreFor(job), in job);
    }

    private void EnsureCanvas(int canvasWidth, int canvasHeight)
    {
        if (_canvasWidth == canvasWidth && _canvasHeight == canvasHeight)
            return;

        _cachedLitCount = 0;
        _cachedBoundsMinX = int.MaxValue;
        _cachedBoundsMinY = int.MaxValue;
        _cachedBoundsMaxX = int.MinValue;
        _cachedBoundsMaxY = int.MinValue;
        _convolutionKey = null;
        _renderedKey = null;
        ReleaseStore();
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
    }

    private void EnsurePackedTextures(int width, int height)
    {
        if (_packedWidth == width && _packedHeight == height)
            return;

        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedOutput = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedWidth = width;
        _packedHeight = height;
    }

    public void Dispose()
    {
        _host.Dispose();
        _host.WaitForDisposal();
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
        _convolutionKey = null;
        _renderedKey = null;
        _storeIsValid = false;
        _canvasWidth = 0;
        _canvasHeight = 0;
        _convolver.Dispose();
        _scratchReadBack.Dispose();
        _scratch.Dispose();
    }

    private enum RenderMode
    {
        Convolve,
        ConvolveAndStore,
        Stored,
    }

    internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

    private readonly record struct ConvolutionKey(
        int HashSum,
        int HashMix,
        int CanvasWidth,
        int CanvasHeight,
        PixelRect Source,
        PsfParameters Psf);

    internal readonly record struct PsfParameters(
        WaveOpticsQuality Quality,
        int KernelRadius,
        float Wavelength,
        float FNumber,
        float PixelPitch,
        WaveOpticsApertureShape ApertureShape,
        int BladeCount,
        float BladeRotation,
        float Obstruction,
        float Defocus,
        float AstigmatismVertical,
        float AstigmatismOblique,
        float ComaHorizontal,
        float ComaVertical,
        float Spherical);

    internal readonly record struct Parameters(float Gain, PsfParameters Psf);
}
