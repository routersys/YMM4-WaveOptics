using System.ComponentModel.DataAnnotations;

namespace WaveOptics.Effects;

public enum WaveOpticsApertureShape
{
    [Display(Name = nameof(Texts.CircularAperture), ResourceType = typeof(Texts))]
    Circular,

    [Display(Name = nameof(Texts.RegularPolygonAperture), ResourceType = typeof(Texts))]
    RegularPolygon,
}
