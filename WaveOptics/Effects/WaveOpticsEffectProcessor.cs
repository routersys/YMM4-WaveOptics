using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using ComputeGuard;
using ComputeWeave;
using SpectralConvolution;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using WaveOptics.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace WaveOptics.Effects;

internal sealed class WaveOpticsEffectProcessor : VideoEffectProcessorBase
{
    private readonly IGraphicsDevicesAndContext _devices;
    private readonly WaveOpticsEffect _item;
    private readonly ComputeGuardian _guardian;
    private readonly Func<ComputeDevice, IReadOnlyList<ComputeCheck>> _selfTest;
    private readonly bool _allowGpu;
    private readonly ConvolutionMeasurement[] _measurements = new ConvolutionMeasurement[WaveOpticsPipeline.MeasurementCount];
    private readonly ComputeCheck[] _checks = new ComputeCheck[WaveOpticsPipeline.MeasurementCount];
    private ComputeExternalQueueScheduler? _scheduler;
    private WaveOpticsInteropProvider? _interopProvider;
    private ComputeInteropDomain? _interopDomain;
    private WaveOpticsResourceSet? _resourceSet;
    private ExternalTextureLease<ExternalDirect3D11TextureView>? _outputLease;
    private WaveOpticsPipeline? _pipeline;
    private GraphicsDevice? _graphicsDevice;
    private ComputeDevice? _computeDevice;
    private bool _gpuAttempted;
    private WaveOpticsCpuPipeline? _cpuPipeline;
    private WaveOpticsCpuSurface? _cpuSurface;
    private WaveOpticsCustomEffect? _effect;
    private Crop? _outputCrop;
    private ID2D1Image? _outputCropOutput;
    private AffineTransform2D? _outputTransform;
    private ID2D1Image? _outputTransformOutput;
    private bool _isFirst = true;
    private bool _hasOutput;
    private bool _hasOutputOffset;
    private bool _hasCropRect;
    private bool _hasRenderState;
    private OutputKind _outputKind;
    private Vector2 _outputOffset;
    private Vector4 _cropRect;
    private float _amount;
    private float _appliedAmount = float.NaN;
    private readonly ID2D1Bitmap1 _sourceBitmap = new(IntPtr.Zero);
    private RenderState _renderState;

    public WaveOpticsEffectProcessor(IGraphicsDevicesAndContext devices, WaveOpticsEffect item)
        : this(devices, item, WaveOpticsCompute.Guardian, null, true)
    {
    }

    internal WaveOpticsEffectProcessor(
        IGraphicsDevicesAndContext devices,
        WaveOpticsEffect item,
        ComputeGuardian guardian,
        Func<ComputeDevice, IReadOnlyList<ComputeCheck>>? selfTest,
        bool allowGpu)
        : base(devices)
    {
        _devices = devices;
        _item = item;
        _guardian = guardian;
        _selfTest = selfTest ?? RunSelfTest;
        _allowGpu = allowGpu;
    }

    internal WaveOpticsPipeline? Pipeline
    {
        get
        {
            EnsureGpu();
            return _pipeline;
        }
    }

    internal ComputeDevice? Device
    {
        get
        {
            EnsureGpu();
            return _pipeline is null ? null : _computeDevice;
        }
    }

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        try
        {
            return UpdateCore(effectDescription);
        }
        catch (Exception exception)
        {
            WaveOpticsTelemetry.Report(exception);
            throw;
        }
    }

    private DrawDescription UpdateCore(EffectDescription effectDescription)
    {
        if (IsPassThroughEffect || _effect is null || _outputCrop is null || _outputTransform is null || _outputTransformOutput is null || input is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;
        var amount = Sanitize(_item.Amount.GetValue(frame, length, fps) / 100d, 0, 1, 0);
        var wavelength = Sanitize(_item.Wavelength.GetValue(frame, length, fps), 380, 780, 550);
        var fNumber = Sanitize(_item.FNumber.GetValue(frame, length, fps), 0.5, 64, 8);
        var defocus = Sanitize(_item.Defocus.GetValue(frame, length, fps), -10, 10, 0);
        if (_item.UseDepth)
        {
            defocus = Sanitize(
                WaveOpticsDepth.AddDefocus(
                    defocus,
                    effectDescription.DrawDescription,
                    Sanitize(_item.FocalLength.GetValue(frame, length, fps), 1, 1000, 50),
                    fNumber,
                    Sanitize(_item.FocusDistance.GetValue(frame, length, fps), 10, 1000000, 1000),
                    WaveOpticsDepth.ReferenceWavelength(_item.ColorMode, wavelength)),
                -10,
                10,
                0);
        }

        var parameters = new WaveOpticsPipeline.Parameters(
            Sanitize(_item.Gain.GetValue(frame, length, fps) / 100d, 0, 4, 1),
            new WaveOpticsPipeline.PsfParameters(
                _item.Quality,
                Math.Clamp(_item.KernelRadius, WaveOpticsSettings.MinimumKernelRadius, WaveOpticsSettings.MaximumKernelRadius),
                wavelength,
                fNumber,
                Sanitize(_item.PixelPitch.GetValue(frame, length, fps), 0.25, 100, 4),
                _item.ApertureShape,
                Math.Clamp(_item.BladeCount, 3, 32),
                Sanitize(_item.BladeRotation.GetValue(frame, length, fps), -360, 360, 0),
                Sanitize(_item.Obstruction.GetValue(frame, length, fps) / 100d, 0, 0.95, 0),
                defocus,
                Sanitize(_item.AstigmatismVertical.GetValue(frame, length, fps), -10, 10, 0),
                Sanitize(_item.AstigmatismOblique.GetValue(frame, length, fps), -10, 10, 0),
                Sanitize(_item.ComaHorizontal.GetValue(frame, length, fps), -10, 10, 0),
                Sanitize(_item.ComaVertical.GetValue(frame, length, fps), -10, 10, 0),
                Sanitize(_item.Spherical.GetValue(frame, length, fps), -10, 10, 0),
                _item.ColorMode),
            new LightOptions(
                _item.Linear,
                _item.Dither,
                Sanitize(_item.HighlightThreshold.GetValue(frame, length, fps) / 100d, 0, 1, 0.95),
                Sanitize(_item.HighlightBoost.GetValue(frame, length, fps), 1, LightOptions.MaximumBoost, 1)));

        if (_isFirst || _amount != amount)
            ApplyAmount(amount);

        if (amount <= 0f)
        {
            ApplyAmount(0f);
            _amount = amount;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var bounds = _devices.DeviceContext.GetImageLocalBounds(input);
        var widthValue = Math.Ceiling((double)bounds.Right - bounds.Left);
        var heightValue = Math.Ceiling((double)bounds.Bottom - bounds.Top);
        if (!double.IsFinite(widthValue) || !double.IsFinite(heightValue) ||
            !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            widthValue <= 0d || heightValue <= 0d)
        {
            ApplyAmount(0f);
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var margin = WaveOpticsSettings.GetCanvasMargin(parameters.Psf.KernelRadius);
        var longSide = Math.Max(widthValue, heightValue);
        if ((WaveOpticsSettings.MaximumCanvasSize - longSide) / 2d < margin)
        {
            ApplyAmount(0f);
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var geometry = new FrameGeometry(
            bounds.Left,
            bounds.Top,
            (int)widthValue,
            (int)heightValue,
            margin,
            (int)widthValue + margin * 2,
            (int)heightValue + margin * 2);

        EnsureGpu();
        var workload = WaveOpticsCompute.Workload(geometry.CanvasWidth, geometry.CanvasHeight, parameters.Psf.KernelRadius, parameters.Psf.ColorMode != WaveOpticsColorMode.Monochrome);
        var device = _pipeline is null ? null : _computeDevice;
        var decision = _guardian.Select(device, workload, _selfTest);
        if (decision.Reason is ComputeRouteReason.Pinned or ComputeRouteReason.SoftwareAdapter)
            ReleaseInterop();
        var outcome = decision.Route == ComputeRoute.Gpu && device is { } gpuDevice
            ? RenderGpu(in geometry, in parameters, gpuDevice, in workload)
            : FrameOutcome.Fallback;
        if (outcome == FrameOutcome.Fallback)
            outcome = RenderCpu(in geometry, in parameters);

        if (outcome == FrameOutcome.PassThrough)
        {
            ApplyAmount(0f);
            _amount = amount;
            _isFirst = true;
            _hasRenderState = false;
            return effectDescription.DrawDescription;
        }

        var rect = _renderState.Rect;
        var cropRect = new Vector4(0f, 0f, rect.Width, rect.Height);
        if (!_hasCropRect || _cropRect != cropRect)
        {
            _outputCrop.Rectangle = cropRect;
            _cropRect = cropRect;
            _hasCropRect = true;
        }
        var outputOffset = new Vector2(geometry.Left - margin + rect.X, geometry.Top - margin + rect.Y);
        if (!_hasOutputOffset || _outputOffset != outputOffset)
        {
            _outputTransform.TransformMatrix = Matrix3x2.CreateTranslation(outputOffset);
            _outputOffset = outputOffset;
            _hasOutputOffset = true;
        }
        _hasOutput = true;
        _amount = amount;
        _isFirst = false;
        return effectDescription.DrawDescription;
    }

    private FrameOutcome RenderGpu(in FrameGeometry geometry, in WaveOpticsPipeline.Parameters parameters, ComputeDevice device, in ComputeWorkload workload)
    {
        var pipeline = _pipeline!;
        var resourceSet = _resourceSet!;
        try
        {
            if (!resourceSet.TryEnsureSource(geometry.ItemWidth, geometry.ItemHeight, out _))
            {
                _guardian.Fail(device, workload, ComputeFailure.ResourceExhausted, null);
                return FrameOutcome.Fallback;
            }

            RenderInput(new Vortice.RawRectF(geometry.Left, geometry.Top, geometry.Left + geometry.ItemWidth, geometry.Top + geometry.ItemHeight));
            var convolutionChanged = pipeline.Simulate(
                resourceSet.GetSourceComputeBinding(),
                geometry.CanvasWidth,
                geometry.CanvasHeight,
                geometry.Margin,
                geometry.Margin,
                geometry.ItemWidth,
                geometry.ItemHeight,
                in parameters);

            if (!pipeline.HasKernel || !pipeline.TryGetVisibleBounds(geometry.CanvasWidth, geometry.CanvasHeight, in parameters, out var rect))
                return FrameOutcome.PassThrough;

            if (!OutputCovers(rect.Width, rect.Height))
                _outputCrop!.SetInput(0, null, true);
            if (!EnsureOutput(rect.Width, rect.Height, out var outputChanged))
            {
                _guardian.Fail(device, workload, ComputeFailure.ResourceExhausted, null);
                return FrameOutcome.Fallback;
            }

            var kindChanged = _outputKind != OutputKind.Gpu;
            var renderState = new RenderState(parameters.Gain, parameters.Light.Dither, rect);
            if (convolutionChanged || outputChanged || kindChanged || !_hasOutput || !_hasRenderState || _renderState != renderState)
            {
                var count = pipeline.RenderVisible(resourceSet.GetSourceComputeBinding(), resourceSet.GetOutputComputeBinding(), rect, in parameters, _measurements);
                if (count > 0)
                {
                    for (var index = 0; index < count; index++)
                        _checks[index] = WaveOpticsCompute.ToCheck(_measurements[index]);
                    if (!_guardian.Judge(device, workload, _checks.AsSpan(0, count)))
                        return FrameOutcome.Fallback;
                }

                _renderState = renderState;
                _hasRenderState = true;
            }

            _outputLease ??= resourceSet.AcquireOutputExternalViewLease();
            if (outputChanged || kindChanged || !_hasOutput)
            {
                using var outputBitmap = new ID2D1Bitmap1(_outputLease.DangerousGetView().AddRefBitmap());
                _outputCrop!.SetInput(0, outputBitmap, true);
                _effect!.SetInput(1, _outputTransformOutput, true);
                _outputKind = OutputKind.Gpu;
            }

            return FrameOutcome.Rendered;
        }
        catch (Exception exception)
        {
            _guardian.Fail(device, workload, WaveOpticsCompute.Classify(exception, pipeline.IsDeviceLost), exception);
            return FrameOutcome.Fallback;
        }
    }

    private FrameOutcome RenderCpu(in FrameGeometry geometry, in WaveOpticsPipeline.Parameters parameters)
    {
        var surface = _cpuSurface ??= new WaveOpticsCpuSurface(_devices.D2D.Device);
        var pipeline = _cpuPipeline ??= new WaveOpticsCpuPipeline();
        var pixels = surface.Read(input!, new Vector2(-geometry.Left, -geometry.Top), geometry.ItemWidth, geometry.ItemHeight);
        var convolutionChanged = pipeline.Simulate(
            pixels,
            geometry.CanvasWidth,
            geometry.CanvasHeight,
            geometry.Margin,
            geometry.Margin,
            geometry.ItemWidth,
            geometry.ItemHeight,
            in parameters);

        if (!pipeline.HasKernel || !pipeline.TryGetVisibleBounds(geometry.CanvasWidth, geometry.CanvasHeight, in parameters, out var rect))
            return FrameOutcome.PassThrough;

        var kindChanged = _outputKind != OutputKind.Cpu;
        var renderState = new RenderState(parameters.Gain, parameters.Light.Dither, rect);
        var outputChanged = false;
        if (convolutionChanged || kindChanged || !_hasOutput || !_hasRenderState || _renderState != renderState)
        {
            var output = pipeline.RenderVisible(rect, in parameters);
            surface.Write(output, rect.Width, rect.Height, out outputChanged);
            _renderState = renderState;
            _hasRenderState = true;
        }

        if (outputChanged || kindChanged || !_hasOutput)
        {
            _outputCrop!.SetInput(0, surface.Output, true);
            _effect!.SetInput(1, _outputTransformOutput, true);
            _outputKind = OutputKind.Cpu;
        }

        return FrameOutcome.Rendered;
    }

    private IReadOnlyList<ComputeCheck> RunSelfTest(ComputeDevice device)
        => WaveOpticsCompute.SelfTest(_graphicsDevice ?? throw new InvalidOperationException());

    private void EnsureGpu()
    {
        if (_gpuAttempted || !_allowGpu || IsPassThroughEffect)
            return;
        _gpuAttempted = true;

        var scheduler = ComputeExternalQueueScheduler.Create();
        var interopProvider = WaveOpticsInteropProvider.TryCreate(_devices, scheduler, out var interopDevice);
        if (interopProvider is null || interopDevice is null)
        {
            scheduler.Dispose();
            return;
        }

        _scheduler = scheduler;
        try
        {
            _interopProvider = interopProvider;
            _interopDomain = interopDevice.RegisterExternalDomain(interopProvider);
            _resourceSet = WaveOpticsResourceSet.Create(interopDevice, _interopDomain);
            _pipeline = WaveOpticsPipeline.TryCreate(interopDevice);
            _computeDevice = WaveOpticsCompute.DescribeDevice(_devices);
            _graphicsDevice = interopDevice;
        }
        catch (Win32Exception)
        {
            ReleaseInterop();
        }
        catch
        {
            ReleaseInterop();
            throw;
        }

        if (_pipeline is null)
            ReleaseInterop();
    }

    private bool OutputCovers(int width, int height)
        => _outputLease is { IsDisposed: false } lease &&
           lease.Width >= width &&
           lease.Height >= height;

    private bool EnsureOutput(int width, int height, out bool changed)
    {
        changed = false;
        if (OutputCovers(width, height))
            return true;

        _outputLease?.Dispose();
        _outputLease = null;
        return _resourceSet!.TryEnsureOutput(width, height, out changed);
    }

    private void RenderInput(Vortice.RawRectF bounds)
    {
        var renderContext = _interopProvider!.RenderContext;
        using var borrow = _resourceSet!.BeginSourceExternalOperation();
        var previousTarget = renderContext.Target;
        var bitmapPointer = borrow.DangerousGetView().AddRefBitmap();
        _sourceBitmap.NativePointer = bitmapPointer;
        try
        {
            renderContext.Target = _sourceBitmap;
            renderContext.BeginDraw();
            renderContext.Clear(null);
            renderContext.DrawImage(
                input,
                new Vector2(-bounds.Left, -bounds.Top),
                null,
                InterpolationMode.NearestNeighbor,
                CompositeMode.SourceCopy);
            renderContext.EndDraw();
            renderContext.Target = previousTarget;
        }
        finally
        {
            _sourceBitmap.NativePointer = IntPtr.Zero;
            Marshal.Release(bitmapPointer);
        }
    }

    private static float Sanitize(double value, double minimum, double maximum, double fallback)
    {
        if (!double.IsFinite(value))
            return (float)fallback;
        return (float)Math.Clamp(value, minimum, maximum);
    }

    private void ApplyAmount(float amount)
    {
        if (_appliedAmount == amount)
            return;

        _effect!.Amount = amount;
        _appliedAmount = amount;
    }

    private void ReleaseInterop()
    {
        if (_outputKind == OutputKind.Gpu)
        {
            _outputCrop?.SetInput(0, null, true);
            _outputKind = OutputKind.None;
            _hasOutput = false;
        }
        _outputLease?.Dispose();
        _outputLease = null;
        _pipeline?.Dispose();
        _pipeline = null;
        _resourceSet?.Dispose();
        _resourceSet?.WaitForDisposal();
        _resourceSet = null;
        _interopDomain?.Dispose();
        _interopDomain?.WaitForDisposal();
        _interopDomain = null;
        _interopProvider?.Dispose();
        _interopProvider = null;
        _scheduler?.Dispose();
        _scheduler = null;
        _graphicsDevice = null;
        _computeDevice = null;
    }

    private void ReleaseCpu()
    {
        _cpuPipeline?.Dispose();
        _cpuPipeline = null;
        _cpuSurface?.Dispose();
        _cpuSurface = null;
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
    {
        WaveOpticsCustomEffect? effect = null;
        Crop? outputCrop = null;
        ID2D1Image? outputCropOutput = null;
        AffineTransform2D? outputTransform = null;
        ID2D1Image? outputTransformOutput = null;
        ID2D1Image? output = null;
        try
        {
            effect = new WaveOpticsCustomEffect(devices);
            if (!effect.IsEnabled)
            {
                effect.Dispose();
                return null;
            }
            outputCrop = new Crop(devices.DeviceContext);
            outputCropOutput = outputCrop.Output;
            outputTransform = new AffineTransform2D(devices.DeviceContext)
            {
                BorderMode = BorderMode.Hard,
            };
            outputTransform.SetInput(0, outputCropOutput, true);
            outputTransformOutput = outputTransform.Output;
            output = effect.Output;
            _effect = effect;
            _outputCrop = outputCrop;
            _outputCropOutput = outputCropOutput;
            _outputTransform = outputTransform;
            _outputTransformOutput = outputTransformOutput;
            disposer.Collect(effect);
            disposer.Collect(outputCrop);
            disposer.Collect(outputCropOutput);
            disposer.Collect(outputTransform);
            disposer.Collect(outputTransformOutput);
            disposer.Collect(output);
            return output;
        }
        catch
        {
            output?.Dispose();
            outputTransformOutput?.Dispose();
            outputTransform?.Dispose();
            outputCropOutput?.Dispose();
            outputCrop?.Dispose();
            effect?.Dispose();
            throw;
        }
    }

    protected override void setInput(ID2D1Image? inputImage)
    {
        try
        {
            SetInputCore(inputImage);
        }
        catch (Exception exception)
        {
            WaveOpticsTelemetry.Report(exception);
            throw;
        }
    }

    private void SetInputCore(ID2D1Image? inputImage)
    {
        _effect?.SetInput(0, inputImage, true);
        if (!_hasOutput)
            _effect?.SetInput(1, inputImage, true);
    }

    protected override void ClearEffectChain()
    {
        _effect?.SetInput(0, null, true);
        _effect?.SetInput(1, null, true);
        _outputCrop?.SetInput(0, null, true);
        _isFirst = true;
        _hasOutput = false;
        _hasOutputOffset = false;
        _hasCropRect = false;
        _hasRenderState = false;
        _outputKind = OutputKind.None;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                ClearEffectChain();
                ReleaseInterop();
                ReleaseCpu();
            }
        }
        catch (Exception exception)
        {
            WaveOpticsTelemetry.Report(exception);
            throw;
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    private enum OutputKind
    {
        None,
        Gpu,
        Cpu,
    }

    private enum FrameOutcome
    {
        Rendered,
        PassThrough,
        Fallback,
    }

    private readonly record struct FrameGeometry(
        float Left,
        float Top,
        int ItemWidth,
        int ItemHeight,
        int Margin,
        int CanvasWidth,
        int CanvasHeight);

    private readonly record struct RenderState(
        float Gain,
        bool Dither,
        WaveOpticsPipeline.PixelRect Rect);
}
