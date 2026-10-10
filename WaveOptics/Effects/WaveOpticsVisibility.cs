using System.Globalization;
using System.Windows;
using System.Windows.Data;
using YukkuriMovieMaker.ItemEditor;

namespace WaveOptics.Effects;

internal abstract class PropertyVisibilityAttribute(string propertyName) : Attribute, ICustomVisibilityAttribute2
{
    protected abstract bool IsVisible(object? value);

    public Binding GetBinding(object item, object propertyOwner) => new(propertyName)
    {
        Source = item,
        Converter = new VisibilityConverter(IsVisible)
    };
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class PolygonApertureVisibleAttribute() : PropertyVisibilityAttribute(nameof(WaveOpticsEffect.ApertureShape))
{
    protected override bool IsVisible(object? value) => value is WaveOpticsApertureShape.RegularPolygon;
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class LinearLightVisibleAttribute() : PropertyVisibilityAttribute(nameof(WaveOpticsEffect.Linear))
{
    protected override bool IsVisible(object? value) => value is true;
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class DepthVisibleAttribute() : PropertyVisibilityAttribute(nameof(WaveOpticsEffect.UseDepth))
{
    protected override bool IsVisible(object? value) => value is true;
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class MonochromeVisibleAttribute() : PropertyVisibilityAttribute(nameof(WaveOpticsEffect.ColorMode))
{
    protected override bool IsVisible(object? value) => value is WaveOpticsColorMode.Monochrome;
}

internal sealed class VisibilityConverter(Func<object?, bool> isVisible) : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => isVisible(value) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
