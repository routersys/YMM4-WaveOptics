using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace WaveOptics.Effects;

[VideoEffect(nameof(Texts.WaveOptics), [VideoEffectCategories.Filtering], [nameof(Texts.TagDiffraction), nameof(Texts.TagLens), nameof(Texts.TagPsf), nameof(Texts.TagOptics)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class WaveOpticsEffect : VideoEffectBase
{
    public override string Label => Texts.WaveOptics;

    public WaveOpticsEffect()
    {
        WaveOpticsTelemetry.EnsureStartedOnce();
        WaveOpticsUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.OutputGroup), Name = nameof(Texts.Amount), Description = nameof(Texts.AmountDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new(100, 0, 100);

    [Display(GroupName = nameof(Texts.OutputGroup), Name = nameof(Texts.Gain), Description = nameof(Texts.GainDescription), Order = 1, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 400)]
    public Animation Gain { get; } = new(100, 0, 400);

    [Display(GroupName = nameof(Texts.OutputGroup), Name = nameof(Texts.Linear), Description = nameof(Texts.LinearDescription), Order = 2, ResourceType = typeof(Texts))]
    [ToggleSlider]
    public bool Linear { get => _linear; set => Set(ref _linear, value); }
    private bool _linear;

    [Display(GroupName = nameof(Texts.OutputGroup), Name = nameof(Texts.HighlightThreshold), Description = nameof(Texts.HighlightThresholdDescription), Order = 3, ResourceType = typeof(Texts))]
    [LinearLightVisible]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation HighlightThreshold { get; } = new(95, 0, 100);

    [Display(GroupName = nameof(Texts.OutputGroup), Name = nameof(Texts.HighlightBoost), Description = nameof(Texts.HighlightBoostDescription), Order = 4, ResourceType = typeof(Texts))]
    [LinearLightVisible]
    [AnimationSlider("F1", "x", 1, 100)]
    public Animation HighlightBoost { get; } = new(1, 1, 1000);

    [Display(GroupName = nameof(Texts.OutputGroup), Name = nameof(Texts.Dither), Description = nameof(Texts.DitherDescription), Order = 5, ResourceType = typeof(Texts))]
    [ToggleSlider]
    public bool Dither { get => _dither; set => Set(ref _dither, value); }
    private bool _dither;

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.ColorMode), Description = nameof(Texts.ColorModeDescription), Order = 9, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public WaveOpticsColorMode ColorMode { get => _colorMode; set => Set(ref _colorMode, value); }
    private WaveOpticsColorMode _colorMode;

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.Wavelength), Description = nameof(Texts.WavelengthDescription), Order = 10, ResourceType = typeof(Texts))]
    [MonochromeVisible]
    [AnimationSlider("F1", "nm", 380, 780)]
    public Animation Wavelength { get; } = new(550, 380, 780);

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.FNumber), Description = nameof(Texts.FNumberDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "", 0.5, 32)]
    public Animation FNumber { get; } = new(8, 0.5, 64);

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.PixelPitch), Description = nameof(Texts.PixelPitchDescription), Order = 12, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "μm", 0.5, 20)]
    public Animation PixelPitch { get; } = new(4, 0.25, 100);

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.KernelRadius), Description = nameof(Texts.KernelRadiusDescription), Order = 13, ResourceType = typeof(Texts))]
    [TextBoxSlider("F0", "px", WaveOpticsSettings.MinimumKernelRadius, WaveOpticsSettings.MaximumKernelRadius)]
    [Range(WaveOpticsSettings.MinimumKernelRadius, WaveOpticsSettings.MaximumKernelRadius)]
    [DefaultValue(WaveOpticsSettings.DefaultKernelRadius)]
    public int KernelRadius { get => _kernelRadius; set => Set(ref _kernelRadius, Math.Clamp(value, WaveOpticsSettings.MinimumKernelRadius, WaveOpticsSettings.MaximumKernelRadius)); }
    private int _kernelRadius = WaveOpticsSettings.DefaultKernelRadius;

    [Display(GroupName = nameof(Texts.OpticsGroup), Name = nameof(Texts.Quality), Description = nameof(Texts.QualityDescription), Order = 14, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public WaveOpticsQuality Quality { get => _quality; set => Set(ref _quality, value); }
    private WaveOpticsQuality _quality = WaveOpticsQuality.Standard;

    [Display(GroupName = nameof(Texts.DepthGroup), Name = nameof(Texts.UseDepth), Description = nameof(Texts.UseDepthDescription), Order = 15, ResourceType = typeof(Texts))]
    [ToggleSlider]
    public bool UseDepth { get => _useDepth; set => Set(ref _useDepth, value); }
    private bool _useDepth;

    [Display(GroupName = nameof(Texts.DepthGroup), Name = nameof(Texts.FocusDistance), Description = nameof(Texts.FocusDistanceDescription), Order = 16, ResourceType = typeof(Texts))]
    [DepthVisible]
    [AnimationSlider("F0", "mm", 100, 10000)]
    public Animation FocusDistance { get; } = new(1000, 10, 1000000);

    [Display(GroupName = nameof(Texts.DepthGroup), Name = nameof(Texts.FocalLength), Description = nameof(Texts.FocalLengthDescription), Order = 17, ResourceType = typeof(Texts))]
    [DepthVisible]
    [AnimationSlider("F1", "mm", 10, 300)]
    public Animation FocalLength { get; } = new(50, 1, 1000);

    [Display(GroupName = nameof(Texts.ApertureGroup), Name = nameof(Texts.ApertureShape), Description = nameof(Texts.ApertureShapeDescription), Order = 20, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public WaveOpticsApertureShape ApertureShape { get => _apertureShape; set => Set(ref _apertureShape, value); }
    private WaveOpticsApertureShape _apertureShape;

    [Display(GroupName = nameof(Texts.ApertureGroup), Name = nameof(Texts.BladeCount), Description = nameof(Texts.BladeCountDescription), Order = 21, ResourceType = typeof(Texts))]
    [PolygonApertureVisible]
    [TextBoxSlider("F0", "", 3, 16)]
    [Range(3, 32)]
    [DefaultValue(6)]
    public int BladeCount { get => _bladeCount; set => Set(ref _bladeCount, Math.Clamp(value, 3, 32)); }
    private int _bladeCount = 6;

    [Display(GroupName = nameof(Texts.ApertureGroup), Name = nameof(Texts.BladeRotation), Description = nameof(Texts.BladeRotationDescription), Order = 22, ResourceType = typeof(Texts))]
    [PolygonApertureVisible]
    [AnimationSlider("F1", "°", -180, 180)]
    public Animation BladeRotation { get; } = new(0, -360, 360);

    [Display(GroupName = nameof(Texts.ApertureGroup), Name = nameof(Texts.Obstruction), Description = nameof(Texts.ObstructionDescription), Order = 23, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 95)]
    public Animation Obstruction { get; } = new(0, 0, 95);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.Defocus), Description = nameof(Texts.DefocusDescription), Order = 30, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation Defocus { get; } = new(0, -10, 10);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.AstigmatismVertical), Description = nameof(Texts.AstigmatismVerticalDescription), Order = 31, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation AstigmatismVertical { get; } = new(0, -10, 10);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.AstigmatismOblique), Description = nameof(Texts.AstigmatismObliqueDescription), Order = 32, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation AstigmatismOblique { get; } = new(0, -10, 10);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.ComaHorizontal), Description = nameof(Texts.ComaHorizontalDescription), Order = 33, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation ComaHorizontal { get; } = new(0, -10, 10);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.ComaVertical), Description = nameof(Texts.ComaVerticalDescription), Order = 34, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation ComaVertical { get; } = new(0, -10, 10);

    [Display(GroupName = nameof(Texts.AberrationGroup), Name = nameof(Texts.Spherical), Description = nameof(Texts.SphericalDescription), Order = 35, ResourceType = typeof(Texts))]
    [AnimationSlider("F3", "waves", -3, 3)]
    public Animation Spherical { get; } = new(0, -10, 10);

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
    {
        try
        {
            return new WaveOpticsEffectProcessor(devices, this);
        }
        catch (Exception exception)
        {
            WaveOpticsTelemetry.Report(exception);
            throw;
        }
    }

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Amount, Gain, HighlightThreshold, HighlightBoost, Wavelength, FNumber, PixelPitch, BladeRotation, Obstruction, Defocus, AstigmatismVertical, AstigmatismOblique, ComaHorizontal, ComaVertical, Spherical, FocusDistance, FocalLength];
}
