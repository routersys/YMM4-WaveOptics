using System.ComponentModel;
using System.Runtime.InteropServices;
using ComputeWeave;
using WaveOptics.Abstractions;
using WaveOptics.Effects;
using WaveOptics.Optics;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly WaveOpticsPipelineHost _host;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private readonly ReadOnlyBuffer<float> _weights;
    private readonly float[] _weightValues = new float[WaveOpticsSettings.WeightsLength];
    private readonly FraunhoferPsfGenerator _generator = new();
    private PsfParameters? _kernelPsf;
    private Kernel _kernel;
    private ConvolutionKey? _convolutionKey;
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
        _weights = device.AllocateReadOnlyBuffer<float>(WaveOpticsSettings.WeightsLength);
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
        if (!TryBeginConvolution(in sourceRect, in parameters, out var rect))
            return false;

        _host.RecordConvolution(source, _weights, in sourceRect, in rect, _canvasWidth, in _kernel).Wait();
        return true;
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
        if (!TryBeginConvolution(in sourceRect, in parameters, out var rect))
            return false;

        _host.RecordSharedConvolution(source, _weights, in sourceRect, in rect, _canvasWidth, in _kernel).Wait();
        return true;
    }

    private PixelRect BeginSimulate(int canvasWidth, int canvasHeight, int sourceOffsetX, int sourceOffsetY, int sourceWidth, int sourceHeight)
    {
        EnsureCanvas(canvasWidth, canvasHeight);
        return new PixelRect(sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight);
    }

    private bool TryBeginConvolution(in PixelRect sourceRect, in Parameters parameters, out PixelRect rect)
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
            parameters.Psf);
        if (_convolutionKey == key || !TryGetVisibleBounds(_canvasWidth, _canvasHeight, in parameters, out rect))
        {
            rect = default;
            return false;
        }

        EnsureKernel(parameters.Psf);
        _convolutionKey = key;
        return true;
    }

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

    internal void RenderVisible(ReadWriteTexture2D<Bgra32, Float4> output, PixelRect rect, in Parameters parameters)
    {
        _host.RecordRender(output, in rect, _canvasWidth, parameters.Gain).Wait();
    }

    internal void RenderVisible(ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> output, PixelRect rect, in Parameters parameters)
    {
        _host.RecordSharedRender(output, in rect, _canvasWidth, parameters.Gain).Wait();
    }

    private ComputeSubmission SubmitFullPipeline(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureCanvas(width, height);
        EnsureKernel(parameters.Psf);
        _convolutionKey = null;
        var sourceRect = new PixelRect(0, 0, width, height);
        return _host.RecordFullPipeline(source, output, _weights, in sourceRect, in _kernel, parameters.Gain);
    }

    private void EnsureKernel(in PsfParameters psf)
    {
        if (_kernelPsf == psf)
            return;

        var pupilGridSize = WaveOpticsSettings.GetPupilGridSize(psf.Quality);
        var aberration = new WavefrontAberration(
            defocusWaves: psf.Defocus,
            astigmatismVerticalWaves: psf.AstigmatismVertical,
            astigmatismObliqueWaves: psf.AstigmatismOblique,
            comaHorizontalWaves: psf.ComaHorizontal,
            comaVerticalWaves: psf.ComaVertical,
            sphericalWaves: psf.Spherical);
        var descriptor = new PsfDescriptor(
            pupilGridSize,
            WaveOpticsSettings.GetPupilDiameterSamples(pupilGridSize),
            WaveOpticsSettings.GetKernelSize(psf.KernelRadius),
            psf.Wavelength,
            psf.FNumber,
            psf.PixelPitch,
            psf.ApertureShape == WaveOpticsApertureShape.Circular ? ApertureShape.Circular : ApertureShape.RegularPolygon,
            psf.BladeCount,
            psf.BladeRotation,
            psf.Obstruction,
            aberration);
        var result = _generator.Generate(descriptor);
        var separable = SeparableKernel.Decompose(result.Kernel.Values.Span, result.Kernel.Size, WaveOpticsSettings.SeparableResidualRatio, WaveOpticsSettings.MaximumRank);

        var size = separable.Size;
        var scale = 1d / separable.Sum;
        for (var term = 0; term < separable.Rank; term++)
        {
            separable.Horizontal.AsSpan(term * size, size).CopyTo(_weightValues.AsSpan(WaveOpticsSettings.GetWeightOffset(term, 0)));
            var verticalOffset = WaveOpticsSettings.GetWeightOffset(term, 1);
            for (var k = 0; k < size; k++)
                _weightValues[verticalOffset + k] = (float)(separable.Vertical[term * size + k] * scale);
        }
        _weights.CopyFrom(_weightValues);
        _kernel = new Kernel(separable.Rank, size / 2);
        _kernelPsf = psf;
    }

    private void EnsureCanvas(int canvasWidth, int canvasHeight)
    {
        if (_canvasWidth == canvasWidth && _canvasHeight == canvasHeight)
            return;

        var canvasLength = canvasWidth * canvasHeight;
        if (!_host.TryEnsureCanvas(new WaveOpticsCanvasResources.Plan(canvasLength, canvasLength), out _))
            throw new InvalidOperationException();

        _cachedLitCount = 0;
        _cachedBoundsMinX = int.MaxValue;
        _cachedBoundsMinY = int.MaxValue;
        _cachedBoundsMaxX = int.MinValue;
        _cachedBoundsMaxY = int.MinValue;
        _convolutionKey = null;
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
        _kernelPsf = null;
        _convolutionKey = null;
        _canvasWidth = 0;
        _canvasHeight = 0;
        _weights.Dispose();
        _scratchReadBack.Dispose();
        _scratch.Dispose();
    }

    internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

    internal readonly record struct Kernel(int Rank, int Radius);

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
