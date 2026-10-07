using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Windows;
using SpectralConvolution;
using WaveOptics.Effects;
using WaveOptics.Rendering;
using YukkuriMovieMaker.ItemEditor;

namespace WaveOptics.Tests;

public sealed class WaveOpticsVisibilityTests
{
    const int Width = 96;
    const int Height = 96;
    const int Black = unchecked((int)0xFF000000);
    const int White = unchecked((int)0xFFFFFFFF);
    const int Gray = unchecked((int)0xFFCCCCCC);

    sealed record Setting(
        float Gain, WaveOpticsQuality Quality, int KernelRadius, float Wavelength, float FNumber, float PixelPitch,
        WaveOpticsApertureShape Shape, int Blades, float Rotation, float Obstruction,
        float Defocus, float AstigmatismVertical, float AstigmatismOblique, float ComaHorizontal, float ComaVertical, float Spherical,
        bool Linear, bool Dither, float Threshold, float Boost, WaveOpticsColorMode ColorMode);

    static readonly Dictionary<string, Func<Setting, Setting>> Variants = new()
    {
        [nameof(WaveOpticsEffect.Gain)] = s => s with { Gain = 2f },
        [nameof(WaveOpticsEffect.Linear)] = s => s with { Linear = !s.Linear },
        [nameof(WaveOpticsEffect.HighlightThreshold)] = s => s with { Threshold = 0.5f },
        [nameof(WaveOpticsEffect.HighlightBoost)] = s => s with { Boost = 10f },
        [nameof(WaveOpticsEffect.Dither)] = s => s with { Dither = true },
        [nameof(WaveOpticsEffect.ColorMode)] = s => s with { ColorMode = s.ColorMode == WaveOpticsColorMode.Monochrome ? WaveOpticsColorMode.Primaries : WaveOpticsColorMode.Monochrome },
        [nameof(WaveOpticsEffect.Wavelength)] = s => s with { Wavelength = 650f },
        [nameof(WaveOpticsEffect.FNumber)] = s => s with { FNumber = 20f },
        [nameof(WaveOpticsEffect.PixelPitch)] = s => s with { PixelPitch = 2.5f },
        [nameof(WaveOpticsEffect.KernelRadius)] = s => s with { KernelRadius = 20 },
        [nameof(WaveOpticsEffect.Quality)] = s => s with { Quality = WaveOpticsQuality.High },
        [nameof(WaveOpticsEffect.ApertureShape)] = s => s with { Shape = s.Shape == WaveOpticsApertureShape.Circular ? WaveOpticsApertureShape.RegularPolygon : WaveOpticsApertureShape.Circular },
        [nameof(WaveOpticsEffect.BladeCount)] = s => s with { Blades = 8 },
        [nameof(WaveOpticsEffect.BladeRotation)] = s => s with { Rotation = 30f },
        [nameof(WaveOpticsEffect.Obstruction)] = s => s with { Obstruction = 0.3f },
        [nameof(WaveOpticsEffect.Defocus)] = s => s with { Defocus = 0.5f },
        [nameof(WaveOpticsEffect.AstigmatismVertical)] = s => s with { AstigmatismVertical = 0.5f },
        [nameof(WaveOpticsEffect.AstigmatismOblique)] = s => s with { AstigmatismOblique = 0.5f },
        [nameof(WaveOpticsEffect.ComaHorizontal)] = s => s with { ComaHorizontal = 0.5f },
        [nameof(WaveOpticsEffect.ComaVertical)] = s => s with { ComaVertical = 0.5f },
        [nameof(WaveOpticsEffect.Spherical)] = s => s with { Spherical = 0.5f },
    };

    static IEnumerable<PropertyInfo> ParameterProperties
        => typeof(WaveOpticsEffect).GetProperties().Where(property => property.GetCustomAttribute<DisplayAttribute>() is not null);

    static int[] Scene()
    {
        var pixels = new int[Width * Height];
        Array.Fill(pixels, Black);
        pixels[Height / 2 * Width + Width / 2] = White;
        for (var y = 20; y < 26; y++)
        {
            for (var x = 20; x < 26; x++)
                pixels[y * Width + x] = Gray;
        }

        return pixels;
    }

    static int[] Render(Setting setting)
    {
        var parameters = new WaveOpticsPipeline.Parameters(
            setting.Gain,
            new WaveOpticsPipeline.PsfParameters(
                setting.Quality, setting.KernelRadius, setting.Wavelength, setting.FNumber, setting.PixelPitch, setting.Shape, setting.Blades,
                setting.Rotation, setting.Obstruction, setting.Defocus, setting.AstigmatismVertical, setting.AstigmatismOblique,
                setting.ComaHorizontal, setting.ComaVertical, setting.Spherical, setting.ColorMode),
            new LightOptions(setting.Linear, setting.Dither, setting.Threshold, setting.Boost));
        var source = Scene();
        var destination = new int[source.Length];
        using var pipeline = new WaveOpticsCpuPipeline(2);
        pipeline.Process(source, destination, Width, Height, in parameters);
        return destination;
    }

    static bool IsVisible(WaveOpticsEffect effect, string name)
    {
        var attribute = typeof(WaveOpticsEffect).GetProperty(name)!.GetCustomAttributes().OfType<ICustomVisibilityAttribute2>().SingleOrDefault();
        if (attribute is null)
            return true;

        var binding = attribute.GetBinding(effect, effect);
        var value = typeof(WaveOpticsEffect).GetProperty(binding.Path.Path)!.GetValue(binding.Source);
        return (Visibility)binding.Converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture) == Visibility.Visible;
    }

    [Fact]
    public void EveryParameterExceptAmountHasAVariantToTest()
    {
        var names = ParameterProperties.Select(property => property.Name).Where(name => name != nameof(WaveOpticsEffect.Amount)).Order().ToArray();

        Assert.Equal(Variants.Keys.Order().ToArray(), names);
    }

    [Theory]
    [InlineData(WaveOpticsApertureShape.Circular, false, WaveOpticsColorMode.Monochrome)]
    [InlineData(WaveOpticsApertureShape.Circular, true, WaveOpticsColorMode.Monochrome)]
    [InlineData(WaveOpticsApertureShape.RegularPolygon, false, WaveOpticsColorMode.Monochrome)]
    [InlineData(WaveOpticsApertureShape.RegularPolygon, true, WaveOpticsColorMode.Monochrome)]
    [InlineData(WaveOpticsApertureShape.Circular, false, WaveOpticsColorMode.Primaries)]
    [InlineData(WaveOpticsApertureShape.RegularPolygon, true, WaveOpticsColorMode.Primaries)]
    [InlineData(WaveOpticsApertureShape.Circular, true, WaveOpticsColorMode.Broadband)]
    [InlineData(WaveOpticsApertureShape.RegularPolygon, false, WaveOpticsColorMode.Broadband)]
    public void AParameterIsShownExactlyWhenChangingItChangesTheOutput(WaveOpticsApertureShape shape, bool linear, WaveOpticsColorMode colorMode)
    {
        var effect = new WaveOpticsEffect { ApertureShape = shape, Linear = linear, ColorMode = colorMode };
        var baseline = new Setting(
            1f, WaveOpticsQuality.Standard, 31, 550f, 16f, 2f, shape, 6, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, linear, false, 0.9f, 30f, colorMode);
        var reference = Render(baseline);

        foreach (var (name, variant) in Variants)
        {
            var changes = !reference.AsSpan().SequenceEqual(Render(variant(baseline)));

            Assert.True(changes == IsVisible(effect, name), $"{name}: changes the output = {changes}, shown = {IsVisible(effect, name)}, shape = {shape}, linear = {linear}, color mode = {colorMode}");
        }
    }
}
