using System.ComponentModel.DataAnnotations;

namespace WaveOptics.Effects;

public enum WaveOpticsQuality
{
    [Display(Name = nameof(Texts.QualityDraft), ResourceType = typeof(Texts))]
    Draft,

    [Display(Name = nameof(Texts.QualityStandard), ResourceType = typeof(Texts))]
    Standard,

    [Display(Name = nameof(Texts.QualityHigh), ResourceType = typeof(Texts))]
    High,
}
