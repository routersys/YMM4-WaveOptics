using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace WaveOptics.Effects;

[VideoEffect(nameof(Texts.EffectName), [VideoEffectCategories.Filtering], [nameof(Texts.TagDiffraction), nameof(Texts.TagLens), nameof(Texts.TagPsf), nameof(Texts.TagOptics)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class WaveOpticsEffect : VideoEffectBase
{
    public override string Label => Texts.EffectName;

    [Display(GroupName = nameof(Texts.OutputGroup), Name = nameof(Texts.AmountName), Description = nameof(Texts.AmountDesc), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new(100, 0, 100);

    [Display(GroupName = nameof(Texts.OutputGroup), Name = nameof(Texts.GainName), Description = nameof(Texts.GainDesc), Order = 1, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 400)]
    public Animation Gain { get; } = new(100, 0, 400);

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.WavelengthName), Description = nameof(Texts.WavelengthDesc), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "nm", 380, 780)]
    public Animation Wavelength { get; } = new(550, 380, 780);

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.FNumberName), Description = nameof(Texts.FNumberDesc), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "", 0.5, 32)]
    public Animation FNumber { get; } = new(8, 0.5, 64);

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.PixelPitchName), Description = nameof(Texts.PixelPitchDesc), Order = 12, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "μm", 0.5, 20)]
    public Animation PixelPitch { get; } = new(4, 0.25, 100);

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.KernelRadiusName), Description = nameof(Texts.KernelRadiusDesc), Order = 13, ResourceType = typeof(Texts))]
    [TextBoxSlider("F0", "px", 1, 15)]
    [Range(1, 15)]
    [DefaultValue(15)]
    public int KernelRadius { get => kernelRadius; set => Set(ref kernelRadius, Math.Clamp(value, 1, 15)); }
    int kernelRadius = 15;

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.QualityName), Description = nameof(Texts.QualityDesc), Order = 14, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public WaveOpticsQuality Quality { get => quality; set => Set(ref quality, value); }
    WaveOpticsQuality quality = WaveOpticsQuality.Standard;

    [Display(GroupName = nameof(Texts.ApertureGroup), Name = nameof(Texts.ApertureShapeName), Description = nameof(Texts.ApertureShapeDesc), Order = 20, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public WaveOpticsApertureShape ApertureShape { get => apertureShape; set => Set(ref apertureShape, value); }
    WaveOpticsApertureShape apertureShape;

    [Display(GroupName = nameof(Texts.ApertureGroup), Name = nameof(Texts.BladeCountName), Description = nameof(Texts.BladeCountDesc), Order = 21, ResourceType = typeof(Texts))]
    [TextBoxSlider("F0", "", 3, 16)]
    [Range(3, 32)]
    [DefaultValue(6)]
    public int BladeCount { get => bladeCount; set => Set(ref bladeCount, Math.Clamp(value, 3, 32)); }
    int bladeCount = 6;

    [Display(GroupName = nameof(Texts.ApertureGroup), Name = nameof(Texts.BladeRotationName), Description = nameof(Texts.BladeRotationDesc), Order = 22, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", -180, 180)]
    public Animation BladeRotation { get; } = new(0, -360, 360);

    [Display(GroupName = nameof(Texts.ApertureGroup), Name = nameof(Texts.ObstructionName), Description = nameof(Texts.ObstructionDesc), Order = 23, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 95)]
    public Animation Obstruction { get; } = new(0, 0, 95);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.DefocusName), Description = nameof(Texts.DefocusDesc), Order = 30, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation Defocus { get; } = new(0, -10, 10);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.AstigmatismVerticalName), Description = nameof(Texts.AstigmatismVerticalDesc), Order = 31, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation AstigmatismVertical { get; } = new(0, -10, 10);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.AstigmatismObliqueName), Description = nameof(Texts.AstigmatismObliqueDesc), Order = 32, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation AstigmatismOblique { get; } = new(0, -10, 10);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.ComaHorizontalName), Description = nameof(Texts.ComaHorizontalDesc), Order = 33, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation ComaHorizontal { get; } = new(0, -10, 10);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.ComaVerticalName), Description = nameof(Texts.ComaVerticalDesc), Order = 34, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation ComaVertical { get; } = new(0, -10, 10);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.SphericalName), Description = nameof(Texts.SphericalDesc), Order = 35, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation Spherical { get; } = new(0, -10, 10);

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => new WaveOpticsEffectProcessor(devices, this);

    protected override IEnumerable<IAnimatable> GetAnimatables()
    {
        yield return Amount;
        yield return Gain;
        yield return Wavelength;
        yield return FNumber;
        yield return PixelPitch;
        yield return BladeRotation;
        yield return Obstruction;
        yield return Defocus;
        yield return AstigmatismVertical;
        yield return AstigmatismOblique;
        yield return ComaHorizontal;
        yield return ComaVertical;
        yield return Spherical;
    }
}
