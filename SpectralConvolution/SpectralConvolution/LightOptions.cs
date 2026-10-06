namespace SpectralConvolution;

internal readonly record struct LightOptions(bool Linear, bool Dither, float Threshold, float Boost)
{
    public const float MaximumBoost = 1000f;

    public bool IsDefault => !Linear && !Dither;

    public bool Highlights => Linear && Boost > 1f && Threshold < 1f;

    public LightOptions ForConvolution()
        => new(Linear, false, Highlights ? Threshold : 0f, Highlights ? Boost : 0f);

    public void Validate()
    {
        if (!float.IsFinite(Threshold) || Threshold < 0f || Threshold > 1f)
            throw new ArgumentOutOfRangeException(nameof(Threshold));
        if (!float.IsFinite(Boost) || Boost < 0f || Boost > MaximumBoost)
            throw new ArgumentOutOfRangeException(nameof(Boost));
    }
}
