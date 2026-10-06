using System.ComponentModel;
using System.Runtime.InteropServices;
using ComputeGuard;
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
    private readonly WaveOpticsRenderTracker _tracker = new();
    private int _uploadedKernelVersion;
    private WaveOpticsConvolutionKey? _convolutionKey;
    private GpuTileJob _storedJob;
    private PixelRect _sourceRect;
    private WaveOpticsSourceHash _sourceHash = WaveOpticsSourceHash.Empty;
    private int _canvasWidth;
    private int _canvasHeight;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;
    private Func<Float2[], Float2[]>? _spectrumTamper;
    private bool _deviceLost;

    private WaveOpticsPipeline(GraphicsDevice device, WaveOpticsPipelineHost host)
    {
        _device = device;
        _host = host;
        _scratch = device.AllocateReadWriteBuffer<int>(WaveOpticsSettings.ScratchLength);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(WaveOpticsSettings.ScratchLength);
        _convolver = new GpuTileConvolver(device);
        device.DeviceLost += OnDeviceLost;
    }

    public static int MeasurementCount => GpuTileCheck.MeasurementCount(GpuTileConvolver.MaximumSamples);

    internal bool IsDeviceLost => Volatile.Read(ref _deviceLost);

    internal Func<Float2[], Float2[]>? SpectrumTamper
    {
        get => _spectrumTamper;
        set
        {
            _spectrumTamper = value;
            _uploadedKernelVersion = 0;
        }
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
        _sourceHash = WaveOpticsSourceHash.FromScratch(_scratchReadBack.Span);

        var key = new WaveOpticsConvolutionKey(
            _sourceHash.Sum,
            _sourceHash.Mix,
            _canvasWidth,
            _canvasHeight,
            sourceRect,
            WaveOpticsKernel.KeyOf(parameters.Psf),
            parameters.Light.ForConvolution());
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

    internal WaveOpticsSourceHash SourceHash => _sourceHash;

    internal bool TryGetVisibleBounds(int canvasWidth, int canvasHeight, in Parameters parameters, out PixelRect rect)
        => _sourceHash.TryGetVisibleBounds(canvasWidth, canvasHeight, parameters.Psf.KernelRadius, out rect);

    internal void RenderVisible(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        PixelRect rect,
        in Parameters parameters)
        => RenderVisible(source, output, rect, in parameters, []);

    internal int RenderVisible(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        PixelRect rect,
        in Parameters parameters,
        Span<ConvolutionMeasurement> measurements)
    {
        var mode = BeginRender(rect, parameters.Gain, parameters.Light.Dither);
        if (mode == WaveOpticsRenderMode.Stored)
        {
            _host.RecordStoredRender(_convolver.StoreFor(_storedJob), output, in rect, parameters.Gain, parameters.Light).Wait();
            return 0;
        }

        Span<Int2> samples = stackalloc Int2[GpuTileConvolver.MaximumSamples];
        var job = PrepareConvolution(rect, parameters.Gain, mode == WaveOpticsRenderMode.ConvolveAndStore, measurements.IsEmpty ? [] : samples, parameters.Light);
        _host.RecordConvolution(
            source, output, _convolver.Tiles, _convolver.Twiddles, _convolver.Spectrum,
            _convolver.Report, _convolver.Samples, _convolver.StoreFor(job), in job).Wait();
        return Measure(in job, samples[..job.SampleCount], measurements);
    }

    internal void RenderVisible(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> source,
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> output,
        PixelRect rect,
        in Parameters parameters)
        => RenderVisible(source, output, rect, in parameters, []);

    internal int RenderVisible(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> source,
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> output,
        PixelRect rect,
        in Parameters parameters,
        Span<ConvolutionMeasurement> measurements)
    {
        var mode = BeginRender(rect, parameters.Gain, parameters.Light.Dither);
        if (mode == WaveOpticsRenderMode.Stored)
        {
            _host.RecordSharedStoredRender(_convolver.StoreFor(_storedJob), output, in rect, parameters.Gain, parameters.Light).Wait();
            return 0;
        }

        Span<Int2> samples = stackalloc Int2[GpuTileConvolver.MaximumSamples];
        var job = PrepareConvolution(rect, parameters.Gain, mode == WaveOpticsRenderMode.ConvolveAndStore, measurements.IsEmpty ? [] : samples, parameters.Light);
        _host.RecordSharedConvolution(
            source, output, _convolver.Tiles, _convolver.Twiddles, _convolver.Spectrum,
            _convolver.Report, _convolver.Samples, _convolver.StoreFor(job), in job).Wait();
        return Measure(in job, samples[..job.SampleCount], measurements);
    }

    private int Measure(in GpuTileJob job, ReadOnlySpan<Int2> samples, Span<ConvolutionMeasurement> measurements)
    {
        if (measurements.IsEmpty)
            return 0;

        var count = GpuTileCheck.MeasurementCount(job.SampleCount);
        GpuTileCheck.Evaluate(_convolver.ReadReport(in job), in job, samples, _kernel.Spectrum.Kernel, measurements[..count]);
        return count;
    }

    private WaveOpticsRenderMode BeginRender(PixelRect rect, float gain, bool dither)
    {
        if (_convolutionKey is not { } key || !_kernel.IsValid)
            throw new InvalidOperationException();

        var mode = _tracker.Next(key, rect, gain, dither, out var releaseStore);
        if (releaseStore)
            ReleaseStoreBuffer();
        return mode;
    }

    private GpuTileJob PrepareConvolution(PixelRect rect, float gain, bool store, Span<Int2> samples, in LightOptions light)
    {
        if (_uploadedKernelVersion != _kernel.Version)
        {
            _convolver.Upload(_kernel.Spectrum);
            if (_spectrumTamper is { } tamper)
                _convolver.Spectrum.CopyFrom(tamper(_kernel.Spectrum.Spectrum.ToArray()));
            _uploadedKernelVersion = _kernel.Version;
        }

        if (!samples.IsEmpty)
        {
            Span<ComputeSample> picks = stackalloc ComputeSample[GpuTileConvolver.MaximumSamples];
            ComputeSampling.Fill(_convolutionKey!.Value.SamplingKey, rect.Width, rect.Height, picks);
            for (var index = 0; index < picks.Length; index++)
                samples[index] = new Int2(picks[index].X, picks[index].Y);
        }

        var plan = TilePlan.Create(_kernel.Spectrum.Size, _kernel.Spectrum.Radius, rect.X, rect.Y, rect.Width, rect.Height);
        var job = _convolver.Prepare(plan, _sourceRect.X, _sourceRect.Y, _sourceRect.Width, _sourceRect.Height, gain, samples, store, light);
        if (store)
            _storedJob = job;

        return job;
    }

    private void ResetRendering()
    {
        if (_tracker.Reset())
            ReleaseStoreBuffer();
    }

    private void ReleaseStoreBuffer()
    {
        _convolver.ReleaseStore();
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
        ResetRendering();
        _sourceRect = new PixelRect(0, 0, width, height);
        var job = PrepareConvolution(_sourceRect, parameters.Gain, false, [], parameters.Light);
        return _host.RecordConvolution(
            source, output, _convolver.Tiles, _convolver.Twiddles, _convolver.Spectrum,
            _convolver.Report, _convolver.Samples, _convolver.StoreFor(job), in job);
    }

    private void EnsureCanvas(int canvasWidth, int canvasHeight)
    {
        if (_canvasWidth == canvasWidth && _canvasHeight == canvasHeight)
            return;

        _sourceHash = WaveOpticsSourceHash.Empty;
        _convolutionKey = null;
        ResetRendering();
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

    private void OnDeviceLost(object? sender, DeviceLostEventArgs e)
        => Volatile.Write(ref _deviceLost, true);

    public void Dispose()
    {
        _device.DeviceLost -= OnDeviceLost;
        _host.Dispose();
        _host.WaitForDisposal();
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
        _convolutionKey = null;
        _tracker.Reset();
        _canvasWidth = 0;
        _canvasHeight = 0;
        _convolver.Dispose();
        _scratchReadBack.Dispose();
        _scratch.Dispose();
    }

    internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

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

    internal readonly record struct Parameters(float Gain, PsfParameters Psf, LightOptions Light = default);
}
