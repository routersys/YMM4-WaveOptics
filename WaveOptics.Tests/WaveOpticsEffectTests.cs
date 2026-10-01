using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using WaveOptics.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;

namespace WaveOptics.Tests;

public sealed class WaveOpticsEffectTests
{
    static PropertyInfo Property(string name) => typeof(WaveOpticsEffect).GetProperty(name)!;

    static T Attribute<T>(string property) where T : Attribute => Property(property).GetCustomAttribute<T>()!;

    static Animation[] Animations(WaveOpticsEffect effect)
        => [effect.Amount, effect.Gain, effect.Wavelength, effect.FNumber, effect.PixelPitch, effect.BladeRotation, effect.Obstruction, effect.Defocus, effect.AstigmatismVertical, effect.AstigmatismOblique, effect.ComaHorizontal, effect.ComaVertical, effect.Spherical];

    [Theory]
    [InlineData(nameof(WaveOpticsEffect.Amount), 100d, 0d, 100d)]
    [InlineData(nameof(WaveOpticsEffect.Gain), 100d, 0d, 400d)]
    [InlineData(nameof(WaveOpticsEffect.Wavelength), 550d, 380d, 780d)]
    [InlineData(nameof(WaveOpticsEffect.FNumber), 8d, 0.5d, 64d)]
    [InlineData(nameof(WaveOpticsEffect.PixelPitch), 4d, 0.25d, 100d)]
    [InlineData(nameof(WaveOpticsEffect.BladeRotation), 0d, -360d, 360d)]
    [InlineData(nameof(WaveOpticsEffect.Obstruction), 0d, 0d, 95d)]
    [InlineData(nameof(WaveOpticsEffect.Defocus), 0d, -10d, 10d)]
    [InlineData(nameof(WaveOpticsEffect.AstigmatismVertical), 0d, -10d, 10d)]
    [InlineData(nameof(WaveOpticsEffect.AstigmatismOblique), 0d, -10d, 10d)]
    [InlineData(nameof(WaveOpticsEffect.ComaHorizontal), 0d, -10d, 10d)]
    [InlineData(nameof(WaveOpticsEffect.ComaVertical), 0d, -10d, 10d)]
    [InlineData(nameof(WaveOpticsEffect.Spherical), 0d, -10d, 10d)]
    public void AnimatedParametersStartFromTheirDefaultsWithinTheirRange(string name, double defaultValue, double minimum, double maximum)
    {
        var effect = new WaveOpticsEffect();

        var animation = (Animation)Property(name).GetValue(effect)!;

        Assert.Equal(defaultValue, animation.DefaultValue);
        Assert.Equal(minimum, animation.MinValue);
        Assert.Equal(maximum, animation.MaxValue);
        Assert.Equal(defaultValue, animation.GetValue(0, 1, EffectDescriptions.Fps));
    }

    [Fact]
    public void KernelRadiusQualityApertureAndBladesStartFromTheirDefaults()
    {
        var effect = new WaveOpticsEffect();

        Assert.Equal(15, effect.KernelRadius);
        Assert.Equal(WaveOpticsQuality.Standard, effect.Quality);
        Assert.Equal(WaveOpticsApertureShape.Circular, effect.ApertureShape);
        Assert.Equal(6, effect.BladeCount);
    }

    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    [InlineData(15, 15)]
    [InlineData(16, 16)]
    [InlineData(63, 63)]
    [InlineData(64, 63)]
    [InlineData(int.MaxValue, 63)]
    public void KernelRadiusStaysBetweenOneAndSixtyThreePixels(int value, int expected)
    {
        var effect = new WaveOpticsEffect { KernelRadius = 8 };

        effect.KernelRadius = value;

        Assert.Equal(expected, effect.KernelRadius);
        Assert.False(effect.HasErrors);
    }

    [Theory]
    [InlineData(int.MinValue, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(12, 12)]
    [InlineData(32, 32)]
    [InlineData(33, 32)]
    [InlineData(int.MaxValue, 32)]
    public void BladeCountStaysBetweenThreeAndThirtyTwo(int value, int expected)
    {
        var effect = new WaveOpticsEffect { BladeCount = 5 };

        effect.BladeCount = value;

        Assert.Equal(expected, effect.BladeCount);
        Assert.False(effect.HasErrors);
    }

    [Fact]
    public void ChangingTheSettingsThatDoNotAnimateNotifiesTheEditor()
    {
        var effect = new WaveOpticsEffect();
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.KernelRadius = 7;
        effect.Quality = WaveOpticsQuality.High;
        effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon;
        effect.BladeCount = 9;

        Assert.Equal([nameof(WaveOpticsEffect.KernelRadius), nameof(WaveOpticsEffect.Quality), nameof(WaveOpticsEffect.ApertureShape), nameof(WaveOpticsEffect.BladeCount)], changed);
    }

    [Fact]
    public void AssigningAnUnchangedOrClampedValueDoesNotNotify()
    {
        var effect = new WaveOpticsEffect { KernelRadius = WaveOpticsSettings.MaximumKernelRadius };
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.KernelRadius = WaveOpticsSettings.MaximumKernelRadius;
        effect.KernelRadius = WaveOpticsSettings.MaximumKernelRadius + 1;
        effect.Quality = WaveOpticsQuality.Standard;
        effect.ApertureShape = WaveOpticsApertureShape.Circular;
        effect.BladeCount = 6;

        Assert.Empty(changed);
    }

    [Fact]
    public void TheLabelIsTheLocalizedEffectName()
    {
        var effect = new WaveOpticsEffect();

        Assert.Equal(Texts.WaveOptics, effect.Label);
    }

    [Fact]
    public void TheThirteenNumericParametersReceiveTheAnimationParameters()
    {
        var effect = new WaveOpticsEffect();

        effect.SetAnimationParameters(120, EffectDescriptions.Fps);

        Assert.All(Animations(effect), animation => Assert.Equal(120, animation.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NoExoFilterIsWrittenForAviUtl(int keyFrameIndex)
    {
        var effect = new WaveOpticsEffect();

        var description = new ExoOutputDescription(new VideoInfo(), string.Empty, new AviUtlDirectories(string.Empty, string.Empty));

        Assert.Empty(effect.CreateExoVideoFilters(keyFrameIndex, description));
    }

    [Fact]
    public void TheEffectIsRegisteredForFilteringWithoutAviUtlSupport()
    {
        var attribute = typeof(WaveOpticsEffect).GetCustomAttribute<VideoEffectAttribute>()!;

        Assert.Equal(nameof(Texts.WaveOptics), attribute.Name);
        Assert.Equal([VideoEffectCategories.Filtering], attribute.Categories);
        Assert.Equal([nameof(Texts.TagDiffraction), nameof(Texts.TagLens), nameof(Texts.TagPsf), nameof(Texts.TagOptics)], attribute.Keywords);
        Assert.False(attribute.IsAviUtlSupported);
        Assert.True(attribute.IsEffectItemSupported);
        Assert.Equal(typeof(Texts), attribute.ResourceType);
        Assert.Equal(Texts.WaveOptics, attribute.GetName());
    }

    [Theory]
    [InlineData(nameof(WaveOpticsEffect.Amount), nameof(Texts.OutputGroup), nameof(Texts.Amount), nameof(Texts.AmountDescription), 0)]
    [InlineData(nameof(WaveOpticsEffect.Gain), nameof(Texts.OutputGroup), nameof(Texts.Gain), nameof(Texts.GainDescription), 1)]
    [InlineData(nameof(WaveOpticsEffect.Wavelength), nameof(Texts.OpticsGroup), nameof(Texts.Wavelength), nameof(Texts.WavelengthDescription), 10)]
    [InlineData(nameof(WaveOpticsEffect.FNumber), nameof(Texts.OpticsGroup), nameof(Texts.FNumber), nameof(Texts.FNumberDescription), 11)]
    [InlineData(nameof(WaveOpticsEffect.PixelPitch), nameof(Texts.OpticsGroup), nameof(Texts.PixelPitch), nameof(Texts.PixelPitchDescription), 12)]
    [InlineData(nameof(WaveOpticsEffect.KernelRadius), nameof(Texts.OpticsGroup), nameof(Texts.KernelRadius), nameof(Texts.KernelRadiusDescription), 13)]
    [InlineData(nameof(WaveOpticsEffect.Quality), nameof(Texts.OpticsGroup), nameof(Texts.Quality), nameof(Texts.QualityDescription), 14)]
    [InlineData(nameof(WaveOpticsEffect.ApertureShape), nameof(Texts.ApertureGroup), nameof(Texts.ApertureShape), nameof(Texts.ApertureShapeDescription), 20)]
    [InlineData(nameof(WaveOpticsEffect.BladeCount), nameof(Texts.ApertureGroup), nameof(Texts.BladeCount), nameof(Texts.BladeCountDescription), 21)]
    [InlineData(nameof(WaveOpticsEffect.BladeRotation), nameof(Texts.ApertureGroup), nameof(Texts.BladeRotation), nameof(Texts.BladeRotationDescription), 22)]
    [InlineData(nameof(WaveOpticsEffect.Obstruction), nameof(Texts.ApertureGroup), nameof(Texts.Obstruction), nameof(Texts.ObstructionDescription), 23)]
    [InlineData(nameof(WaveOpticsEffect.Defocus), nameof(Texts.AberrationGroup), nameof(Texts.Defocus), nameof(Texts.DefocusDescription), 30)]
    [InlineData(nameof(WaveOpticsEffect.AstigmatismVertical), nameof(Texts.AberrationGroup), nameof(Texts.AstigmatismVertical), nameof(Texts.AstigmatismVerticalDescription), 31)]
    [InlineData(nameof(WaveOpticsEffect.AstigmatismOblique), nameof(Texts.AberrationGroup), nameof(Texts.AstigmatismOblique), nameof(Texts.AstigmatismObliqueDescription), 32)]
    [InlineData(nameof(WaveOpticsEffect.ComaHorizontal), nameof(Texts.AberrationGroup), nameof(Texts.ComaHorizontal), nameof(Texts.ComaHorizontalDescription), 33)]
    [InlineData(nameof(WaveOpticsEffect.ComaVertical), nameof(Texts.AberrationGroup), nameof(Texts.ComaVertical), nameof(Texts.ComaVerticalDescription), 34)]
    [InlineData(nameof(WaveOpticsEffect.Spherical), nameof(Texts.AberrationGroup), nameof(Texts.Spherical), nameof(Texts.SphericalDescription), 35)]
    public void EveryParameterIsDisplayedInItsGroupInOrder(string property, string group, string name, string description, int order)
    {
        var display = Attribute<DisplayAttribute>(property);

        Assert.Equal(group, display.GroupName);
        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(order, display.Order);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Theory]
    [InlineData(nameof(WaveOpticsEffect.Amount), "F1", "%", 0d, 100d)]
    [InlineData(nameof(WaveOpticsEffect.Gain), "F1", "%", 0d, 400d)]
    [InlineData(nameof(WaveOpticsEffect.Wavelength), "F1", "nm", 380d, 780d)]
    [InlineData(nameof(WaveOpticsEffect.FNumber), "F2", "", 0.5d, 32d)]
    [InlineData(nameof(WaveOpticsEffect.PixelPitch), "F2", "μm", 0.5d, 20d)]
    [InlineData(nameof(WaveOpticsEffect.BladeRotation), "F1", "°", -180d, 180d)]
    [InlineData(nameof(WaveOpticsEffect.Obstruction), "F1", "%", 0d, 95d)]
    [InlineData(nameof(WaveOpticsEffect.Defocus), "F3", "waves", -3d, 3d)]
    [InlineData(nameof(WaveOpticsEffect.AstigmatismVertical), "F3", "waves", -3d, 3d)]
    [InlineData(nameof(WaveOpticsEffect.AstigmatismOblique), "F3", "waves", -3d, 3d)]
    [InlineData(nameof(WaveOpticsEffect.ComaHorizontal), "F3", "waves", -3d, 3d)]
    [InlineData(nameof(WaveOpticsEffect.ComaVertical), "F3", "waves", -3d, 3d)]
    [InlineData(nameof(WaveOpticsEffect.Spherical), "F3", "waves", -3d, 3d)]
    public void AnimatedParametersAreEditedWithAnimationSliders(string property, string format, string unit, double minimum, double maximum)
    {
        var slider = Attribute<AnimationSliderAttribute>(property);

        Assert.Equal(format, slider.StringFormat);
        Assert.Equal(unit, slider.UnitText);
        Assert.Equal(minimum, slider.DefaultMin);
        Assert.Equal(maximum, slider.DefaultMax);
    }

    [Theory]
    [InlineData(nameof(WaveOpticsEffect.KernelRadius), "px", 1d, 63d, 1, 63, 15)]
    [InlineData(nameof(WaveOpticsEffect.BladeCount), "", 3d, 16d, 3, 32, 6)]
    public void WholeNumberParametersAreEditedWithTextBoxSliders(string property, string unit, double sliderMinimum, double sliderMaximum, int minimum, int maximum, int defaultValue)
    {
        var slider = Attribute<TextBoxSliderAttribute>(property);
        var range = Attribute<RangeAttribute>(property);

        Assert.Equal("F0", slider.StringFormat);
        Assert.Equal(unit, slider.UnitText);
        Assert.Equal(sliderMinimum, slider.DefaultMin);
        Assert.Equal(sliderMaximum, slider.DefaultMax);
        Assert.Equal(minimum, range.Minimum);
        Assert.Equal(maximum, range.Maximum);
        Assert.Equal(defaultValue, Attribute<DefaultValueAttribute>(property).Value);
    }

    [Fact]
    public void TheQualityAndTheApertureShapeAreChosenFromCombos()
    {
        Assert.NotNull(Attribute<EnumComboBoxAttribute>(nameof(WaveOpticsEffect.Quality)));
        Assert.NotNull(Attribute<EnumComboBoxAttribute>(nameof(WaveOpticsEffect.ApertureShape)));
        Assert.Equal([WaveOpticsQuality.Draft, WaveOpticsQuality.Standard, WaveOpticsQuality.High], Enum.GetValues<WaveOpticsQuality>());
        Assert.Equal([WaveOpticsApertureShape.Circular, WaveOpticsApertureShape.RegularPolygon], Enum.GetValues<WaveOpticsApertureShape>());
    }

    [Theory]
    [InlineData(typeof(WaveOpticsQuality), nameof(WaveOpticsQuality.Draft), nameof(Texts.QualityDraft))]
    [InlineData(typeof(WaveOpticsQuality), nameof(WaveOpticsQuality.Standard), nameof(Texts.QualityStandard))]
    [InlineData(typeof(WaveOpticsQuality), nameof(WaveOpticsQuality.High), nameof(Texts.QualityHigh))]
    [InlineData(typeof(WaveOpticsApertureShape), nameof(WaveOpticsApertureShape.Circular), nameof(Texts.CircularAperture))]
    [InlineData(typeof(WaveOpticsApertureShape), nameof(WaveOpticsApertureShape.RegularPolygon), nameof(Texts.RegularPolygonAperture))]
    public void EveryChoiceIsDisplayedWithItsLocalizedName(Type type, string value, string name)
    {
        var display = type.GetField(value)!.GetCustomAttribute<DisplayAttribute>()!;

        Assert.Equal(name, display.Name);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Fact]
    public void EverySettingSurvivesAProjectRoundTrip()
    {
        var effect = new WaveOpticsEffect { KernelRadius = 7, Quality = WaveOpticsQuality.High, ApertureShape = WaveOpticsApertureShape.RegularPolygon, BladeCount = 9 };
        var values = new[] { 55d, 150d, 620d, 5.6d, 2.5d, 30d, 40d, 1.5d, -0.5d, 0.25d, -1d, 0.75d, 2d };
        foreach (var (animation, value) in Animations(effect).Zip(values))
            animation.Values[0].Value = value;

        var clone = Json.GetClone(effect)!;

        Assert.NotSame(effect, clone);
        Assert.Equal(7, clone.KernelRadius);
        Assert.Equal(WaveOpticsQuality.High, clone.Quality);
        Assert.Equal(WaveOpticsApertureShape.RegularPolygon, clone.ApertureShape);
        Assert.Equal(9, clone.BladeCount);
        Assert.Equal(values, Animations(clone).Select(animation => animation.GetValue(0, 1, EffectDescriptions.Fps)));
    }

    [Fact]
    public void ProjectsSavedWithTheSettingNamesStillLoad()
    {
        const string saved = """{"KernelRadius":7,"Quality":"High","ApertureShape":"RegularPolygon","BladeCount":9,"Defocus":{"Values":[{"Value":1.5}]}}""";

        var effect = Json.LoadFromText<WaveOpticsEffect>(saved)!;

        Assert.Equal(7, effect.KernelRadius);
        Assert.Equal(WaveOpticsQuality.High, effect.Quality);
        Assert.Equal(WaveOpticsApertureShape.RegularPolygon, effect.ApertureShape);
        Assert.Equal(9, effect.BladeCount);
        Assert.Equal(1.5d, effect.Defocus.GetValue(0, 1, EffectDescriptions.Fps));
    }

    [Fact]
    public void TheEffectKeepsTheTypeNameThatProjectsStore()
    {
        var type = typeof(WaveOpticsEffect);

        Assert.Equal("WaveOptics.Effects.WaveOpticsEffect", type.FullName);
        Assert.Equal("WaveOptics", type.Assembly.GetName().Name);
    }
}
