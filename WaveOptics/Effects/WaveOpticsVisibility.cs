using System.Globalization;
using System.Windows;
using System.Windows.Data;
using YukkuriMovieMaker.ItemEditor;

namespace WaveOptics.Effects;

[AttributeUsage(AttributeTargets.Property)]
internal sealed class PolygonApertureVisibleAttribute : Attribute, ICustomVisibilityAttribute2
{
    public Binding GetBinding(object item, object propertyOwner) => new(nameof(WaveOpticsEffect.ApertureShape))
    {
        Source = item,
        Converter = new VisibilityConverter(value => value is WaveOpticsApertureShape.RegularPolygon)
    };
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class LinearLightVisibleAttribute : Attribute, ICustomVisibilityAttribute2
{
    public Binding GetBinding(object item, object propertyOwner) => new(nameof(WaveOpticsEffect.Linear))
    {
        Source = item,
        Converter = new VisibilityConverter(value => value is true)
    };
}

internal sealed class VisibilityConverter(Func<object?, bool> isVisible) : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => isVisible(value) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
