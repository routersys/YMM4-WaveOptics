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
    const double SeparableResidualRatio = 1e-4;
    const int MaximumRank = WaveOpticsSeparableResolveEffect.MaximumRank;

    readonly IGraphicsDevicesAndContext devices = devices;
    readonly WaveOpticsEffect item = item;
    readonly FraunhoferPsfGenerator generator = new();
    readonly WaveOpticsSeparablePassEffect[] horizontalPasses = new WaveOpticsSeparablePassEffect[MaximumRank];
    readonly WaveOpticsSeparablePassEffect[] verticalPasses = new WaveOpticsSeparablePassEffect[MaximumRank];
    readonly ID2D1Bitmap?[] horizontalWeights = new ID2D1Bitmap?[MaximumRank];
    readonly ID2D1Bitmap?[] verticalWeights = new ID2D1Bitmap?[MaximumRank];

    WaveOpticsSeparableResolveEffect? resolve;
    Parameters? currentParameters;
    float amount;
    float gain;
    bool isFirst = true;

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        if (IsPassThroughEffect || resolve is null)
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
            resolve.Amount = amount;
        if (isFirst || this.gain != gain)
            resolve.Gain = gain;

        isFirst = false;
        this.amount = amount;
        this.gain = gain;
        currentParameters = parameters;
        return effectDescription.DrawDescription;
    }

    void UpdateKernel(Parameters parameters)
    {
        if (resolve is null)
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
        var separable = SeparableKernel.Decompose(result.Kernel.Values.Span, result.Kernel.Size, SeparableResidualRatio, MaximumRank);

        var size = separable.Size;
        var radius = size / 2;
        ReadOnlySpan<float> passthrough = [1f];

        for (var term = 0; term < MaximumRank; term++)
        {
            if (term < separable.Rank)
            {
                ConfigurePass(horizontalPasses[term], ref horizontalWeights[term], separable.Horizontal.AsSpan(term * size, size), radius, 0);
                ConfigurePass(verticalPasses[term], ref verticalWeights[term], separable.Vertical.AsSpan(term * size, size), radius, 1);
            }
            else
            {
                ConfigurePass(horizontalPasses[term], ref horizontalWeights[term], passthrough, 0, 0);
                ConfigurePass(verticalPasses[term], ref verticalWeights[term], passthrough, 0, 1);
            }
        }

        resolve.Rank = separable.Rank;
    }

    void ConfigurePass(WaveOpticsSeparablePassEffect pass, ref ID2D1Bitmap? slot, ReadOnlySpan<float> weights, int radius, int axis)
    {
        var next = PsfBitmapFactory.CreateWeights(devices.DeviceContext, weights);
        pass.SetWeights(null);
        disposer.RemoveAndDispose(ref slot);
        slot = next;
        disposer.Collect(slot);
        pass.Axis = axis;
        pass.Radius = radius;
        pass.SetWeights(slot);
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
        resolve = new WaveOpticsSeparableResolveEffect(devices);
        if (!resolve.IsEnabled)
        {
            resolve.Dispose();
            resolve = null;
            return null;
        }
        disposer.Collect(resolve);

        for (var term = 0; term < MaximumRank; term++)
        {
            var horizontal = new WaveOpticsSeparablePassEffect(devices);
            var vertical = new WaveOpticsSeparablePassEffect(devices);
            if (!horizontal.IsEnabled || !vertical.IsEnabled)
            {
                horizontal.Dispose();
                vertical.Dispose();
                resolve = null;
                return null;
            }
            disposer.Collect(horizontal);
            disposer.Collect(vertical);
            horizontal.Axis = 0;
            vertical.Axis = 1;
            vertical.SetSource(horizontal.Output);
            resolve.SetTerm(term, vertical.Output);
            horizontalPasses[term] = horizontal;
            verticalPasses[term] = vertical;
        }

        var output = resolve.Output;
        disposer.Collect(output);
        return output;
    }

    protected override void setInput(ID2D1Image? input)
    {
        foreach (var pass in horizontalPasses)
            pass?.SetSource(input);
        resolve?.SetSource(input);
    }

    protected override void ClearEffectChain()
    {
        for (var term = 0; term < MaximumRank; term++)
        {
            horizontalPasses[term]?.SetSource(null);
            horizontalPasses[term]?.SetWeights(null);
            verticalPasses[term]?.SetSource(null);
            verticalPasses[term]?.SetWeights(null);
            resolve?.SetTerm(term, null);
        }
        resolve?.SetSource(null);
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
