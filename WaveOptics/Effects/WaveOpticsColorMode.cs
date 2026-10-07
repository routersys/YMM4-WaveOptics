using System.ComponentModel.DataAnnotations;

namespace WaveOptics.Effects;

public enum WaveOpticsColorMode
{
    [Display(Name = nameof(Texts.ColorModeMonochrome), ResourceType = typeof(Texts))]
    Monochrome,

    [Display(Name = nameof(Texts.ColorModePrimaries), ResourceType = typeof(Texts))]
    Primaries,

    [Display(Name = nameof(Texts.ColorModeBroadband), ResourceType = typeof(Texts))]
    Broadband,
}
