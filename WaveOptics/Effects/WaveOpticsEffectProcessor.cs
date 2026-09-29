using System.ComponentModel;
using System.Numerics;
using ComputeWeave;
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
    private ComputeExternalQueueScheduler? _scheduler;
    private WaveOpticsInteropProvider? _interopProvider;
    private ComputeInteropDomain? _interopDomain;
    private WaveOpticsResourceSet? _resourceSet;
    private ExternalTextureLease<ExternalDirect3D11TextureView>? _outputLease;
    private WaveOpticsPipeline? _pipeline;
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
    private Vector2 _outputOffset;
    private Vector4 _cropRect;
    private float _amount;
    private RenderState _renderState;

    public WaveOpticsEffectProcessor(IGraphicsDevicesAndContext devices, WaveOpticsEffect item)
        : base(devices)
    {
        _devices = devices;
        _item = item;
    }

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        if (IsPassThroughEffect || _effect is null || _outputCrop is null || _outputTransform is null || _outputTransformOutput is null || _resourceSet is null || _interopProvider is null || _pipeline is null || input is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;
        var amount = Sanitize(_item.Amount.GetValue(frame, length, fps) / 100d, 0, 1, 0);
        var parameters = new WaveOpticsPipeline.Parameters(
            Sanitize(_item.Gain.GetValue(frame, length, fps) / 100d, 0, 4, 1),
            new WaveOpticsPipeline.PsfParameters(
                _item.Quality,
                Math.Clamp(_item.KernelRadius, WaveOpticsSettings.MinimumKernelRadius, WaveOpticsSettings.MaximumKernelRadius),
                Sanitize(_item.Wavelength.GetValue(frame, length, fps), 380, 780, 550),
                Sanitize(_item.FNumber.GetValue(frame, length, fps), 0.5, 64, 8),
                Sanitize(_item.PixelPitch.GetValue(frame, length, fps), 0.25, 100, 4),
                _item.ApertureShape,
                Math.Clamp(_item.BladeCount, 3, 32),
                Sanitize(_item.BladeRotation.GetValue(frame, length, fps), -360, 360, 0),
                Sanitize(_item.Obstruction.GetValue(frame, length, fps) / 100d, 0, 0.95, 0),
                Sanitize(_item.Defocus.GetValue(frame, length, fps), -10, 10, 0),
                Sanitize(_item.AstigmatismVertical.GetValue(frame, length, fps), -10, 10, 0),
                Sanitize(_item.AstigmatismOblique.GetValue(frame, length, fps), -10, 10, 0),
                Sanitize(_item.ComaHorizontal.GetValue(frame, length, fps), -10, 10, 0),
                Sanitize(_item.ComaVertical.GetValue(frame, length, fps), -10, 10, 0),
                Sanitize(_item.Spherical.GetValue(frame, length, fps), -10, 10, 0)));

        if (_isFirst || _amount != amount)
            _effect.Amount = amount;

        if (amount <= 0f)
        {
            _effect.Amount = 0f;
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
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var margin = WaveOpticsSettings.CanvasMargin;
        var longSide = Math.Max(widthValue, heightValue);
        if ((WaveOpticsSettings.MaximumCanvasSize - longSide) / 2d < margin)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }
        var canvasWidth = (int)widthValue + margin * 2;
        var canvasHeight = (int)heightValue + margin * 2;
        var itemWidth = (int)widthValue;
        var itemHeight = (int)heightValue;

        if (!EnsureSource(itemWidth, itemHeight))
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        RenderInput(new Vortice.RawRectF(bounds.Left, bounds.Top, bounds.Left + itemWidth, bounds.Top + itemHeight));

        var convolutionChanged = _pipeline.Simulate(
            _resourceSet.GetSourceComputeBinding(),
            canvasWidth,
            canvasHeight,
            margin,
            margin,
            itemWidth,
            itemHeight,
            in parameters);

        if (!_pipeline.TryGetVisibleBounds(canvasWidth, canvasHeight, in parameters, out var rect))
        {
            _effect.Amount = 0f;
            _amount = amount;
            _isFirst = true;
            _hasRenderState = false;
            return effectDescription.DrawDescription;
        }

        if (!OutputCovers(rect.Width, rect.Height))
            _outputCrop.SetInput(0, null, true);
        if (!EnsureOutput(rect.Width, rect.Height, out var outputChanged))
        {
            _effect.Amount = 0f;
            _amount = amount;
            _isFirst = true;
            _hasRenderState = false;
            return effectDescription.DrawDescription;
        }
        var renderState = new RenderState(parameters.Gain, rect);
        if (convolutionChanged || outputChanged || !_hasOutput || !_hasRenderState || _renderState != renderState)
        {
            _pipeline.RenderVisible(_resourceSet.GetOutputComputeBinding(), rect, in parameters);
            _renderState = renderState;
            _hasRenderState = true;
        }

        _outputLease ??= _resourceSet.AcquireOutputExternalViewLease();

        if (outputChanged || !_hasOutput)
        {
            using var outputBitmap = new ID2D1Bitmap1(_outputLease.DangerousGetView().AddRefBitmap());
            _outputCrop.SetInput(0, outputBitmap, true);
            _effect.SetInput(1, _outputTransformOutput, true);
        }
        var cropRect = new Vector4(0f, 0f, rect.Width, rect.Height);
        if (!_hasCropRect || _cropRect != cropRect)
        {
            _outputCrop.Rectangle = cropRect;
            _cropRect = cropRect;
            _hasCropRect = true;
        }
        var outputOffset = new Vector2(bounds.Left - margin + rect.X, bounds.Top - margin + rect.Y);
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

    private bool EnsureSource(int width, int height)
    {
        return _resourceSet!.TryEnsureSource(width, height, out _);
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
        using var sourceBitmap = new ID2D1Bitmap1(borrow.DangerousGetView().AddRefBitmap());
        renderContext.Target = sourceBitmap;
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

    private static float Sanitize(double value, double minimum, double maximum, double fallback)
    {
        if (!double.IsFinite(value))
            return (float)fallback;
        return (float)Math.Clamp(value, minimum, maximum);
    }

    private void ReleaseInterop()
    {
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
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
    {
        var scheduler = ComputeExternalQueueScheduler.Create();
        var interopProvider = WaveOpticsInteropProvider.TryCreate(devices, scheduler, out var interopDevice);
        if (interopProvider is null || interopDevice is null)
        {
            scheduler.Dispose();
            return null;
        }

        _scheduler = scheduler;

        try
        {
            _interopProvider = interopProvider;
            _interopDomain = interopDevice.RegisterExternalDomain(interopProvider);
            _resourceSet = WaveOpticsResourceSet.Create(interopDevice, _interopDomain);
            _pipeline = WaveOpticsPipeline.TryCreate(interopDevice);
        }
        catch (Win32Exception)
        {
            ReleaseInterop();
            return null;
        }
        catch
        {
            ReleaseInterop();
            throw;
        }

        if (_pipeline is null)
        {
            ReleaseInterop();
            return null;
        }

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
                ReleaseInterop();
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
            ReleaseInterop();
            throw;
        }
    }

    protected override void setInput(ID2D1Image? inputImage)
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
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                ClearEffectChain();
                ReleaseInterop();
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    private readonly record struct RenderState(
        float Gain,
        WaveOpticsPipeline.PixelRect Rect);
}
