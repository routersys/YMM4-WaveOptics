using Vortice.Direct2D1;
using WaveOptics.Abstractions;
using WaveOptics.Optics;
using WaveOptics.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace WaveOptics.Effects;

internal sealed class WaveOpticsEffectProcessor(IGraphicsDevicesAndContext devices, WaveOpticsEffect item) : VideoEffectProcessorBase(devices)
{
    readonly IGraphicsDevicesAndContext devices = devices;
    readonly WaveOpticsEffect item = item;
    readonly FraunhoferPsfGenerator generator = new();

    WaveOpticsConvolutionEffect? effect;
    ID2D1Bitmap? kernelBitmap;
    Parameters? currentParameters;
    float amount;
    float gain;
    bool isFirst = true;

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        if (IsPassThroughEffect || effect is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;
        var amount = Sanitize(item.Amount.GetValue(frame, length, fps) / 100d, 0, 1, 0);
        var gain = Sanitize(item.Gain.GetValue(frame, length, fps) / 100d, 0, 4, 1);
        var parameters = new Parameters(
            GetGridSize(item.Quality),
            item.KernelRadius * 2 + 1,
            Sanitize(item.Wavelength.GetValue(frame, length, fps), 380, 780, 550),
            Sanitize(item.FNumber.GetValue(frame, length, fps), 0.5, 64, 8),
            Sanitize(item.PixelPitch.GetValue(frame, length, fps), 0.25, 100, 4),
            item.ApertureShape,
            Math.Clamp(item.BladeCount, 3, 32),
            Sanitize(item.BladeRotation.GetValue(frame, length, fps), -360, 360, 0),
            Sanitize(item.Obstruction.GetValue(frame, length, fps) / 100d, 0, 0.95, 0),
            Sanitize(item.Defocus.GetValue(frame, length, fps), -10, 10, 0),
            Sanitize(item.AstigmatismVertical.GetValue(frame, length, fps), -10, 10, 0),
            Sanitize(item.AstigmatismOblique.GetValue(frame, length, fps), -10, 10, 0),
            Sanitize(item.ComaHorizontal.GetValue(frame, length, fps), -10, 10, 0),
            Sanitize(item.ComaVertical.GetValue(frame, length, fps), -10, 10, 0),
            Sanitize(item.Spherical.GetValue(frame, length, fps), -10, 10, 0));

        if (currentParameters != parameters)
            UpdateKernel(parameters);
        if (isFirst || this.amount != amount)
            effect.Amount = amount;
        if (isFirst || this.gain != gain)
            effect.Gain = gain;

        isFirst = false;
        this.amount = amount;
        this.gain = gain;
        currentParameters = parameters;
        return effectDescription.DrawDescription;
    }

    void UpdateKernel(Parameters parameters)
    {
        if (effect is null)
            return;

        var aberration = new WavefrontAberration(
            defocusWaves: parameters.Defocus,
            astigmatismVerticalWaves: parameters.AstigmatismVertical,
            astigmatismObliqueWaves: parameters.AstigmatismOblique,
            comaHorizontalWaves: parameters.ComaHorizontal,
            comaVerticalWaves: parameters.ComaVertical,
            sphericalWaves: parameters.Spherical);
        var descriptor = new PsfDescriptor(
            parameters.GridSize,
            parameters.GridSize / 4,
            parameters.KernelSize,
            parameters.Wavelength,
            parameters.FNumber,
            parameters.PixelPitch,
            parameters.ApertureShape == WaveOpticsApertureShape.Circular ? ApertureShape.Circular : ApertureShape.RegularPolygon,
            parameters.BladeCount,
            parameters.BladeRotation,
            parameters.Obstruction,
            aberration);
        var result = generator.Generate(descriptor);
        var nextBitmap = PsfBitmapFactory.Create(devices.DeviceContext, result.Kernel);

        effect.SetKernel(null);
        disposer.RemoveAndDispose(ref kernelBitmap);
        kernelBitmap = nextBitmap;
        disposer.Collect(kernelBitmap);
        effect.KernelSize = result.Kernel.Size;
        effect.SetKernel(kernelBitmap);
    }

    static int GetGridSize(WaveOpticsQuality quality)
    {
        return quality switch
        {
            WaveOpticsQuality.Draft => 128,
            WaveOpticsQuality.High => 512,
            _ => 256,
        };
    }

    static float Sanitize(double value, double minimum, double maximum, double fallback)
    {
        if (!double.IsFinite(value))
            return (float)fallback;
        return (float)Math.Clamp(value, minimum, maximum);
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
    {
        effect = new WaveOpticsConvolutionEffect(devices);
        if (!effect.IsEnabled)
        {
            effect.Dispose();
            effect = null;
            return null;
        }
        disposer.Collect(effect);

        var output = effect.Output;
        disposer.Collect(output);
        return output;
    }

    protected override void setInput(ID2D1Image? input)
    {
        effect?.SetSource(input);
    }

    protected override void ClearEffectChain()
    {
        effect?.SetSource(null);
        effect?.SetKernel(null);
        currentParameters = null;
        isFirst = true;
    }

    readonly record struct Parameters(
        int GridSize,
        int KernelSize,
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
}
