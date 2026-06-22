namespace WaveOptics.Abstractions;

public readonly struct WavefrontAberration : IEquatable<WavefrontAberration>
{
    public double DefocusWaves { get; }
    public double AstigmatismVerticalWaves { get; }
    public double AstigmatismObliqueWaves { get; }
    public double ComaHorizontalWaves { get; }
    public double ComaVerticalWaves { get; }
    public double TrefoilHorizontalWaves { get; }
    public double TrefoilVerticalWaves { get; }
    public double SphericalWaves { get; }

    public WavefrontAberration(
        double defocusWaves = 0,
        double astigmatismVerticalWaves = 0,
        double astigmatismObliqueWaves = 0,
        double comaHorizontalWaves = 0,
        double comaVerticalWaves = 0,
        double trefoilHorizontalWaves = 0,
        double trefoilVerticalWaves = 0,
        double sphericalWaves = 0)
    {
        DefocusWaves = Validate(defocusWaves, nameof(defocusWaves));
        AstigmatismVerticalWaves = Validate(astigmatismVerticalWaves, nameof(astigmatismVerticalWaves));
        AstigmatismObliqueWaves = Validate(astigmatismObliqueWaves, nameof(astigmatismObliqueWaves));
        ComaHorizontalWaves = Validate(comaHorizontalWaves, nameof(comaHorizontalWaves));
        ComaVerticalWaves = Validate(comaVerticalWaves, nameof(comaVerticalWaves));
        TrefoilHorizontalWaves = Validate(trefoilHorizontalWaves, nameof(trefoilHorizontalWaves));
        TrefoilVerticalWaves = Validate(trefoilVerticalWaves, nameof(trefoilVerticalWaves));
        SphericalWaves = Validate(sphericalWaves, nameof(sphericalWaves));
    }

    static double Validate(double value, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(value), true, name);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, -10, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 10, name);
        return value;
    }

    public bool Equals(WavefrontAberration other)
    {
        return DefocusWaves.Equals(other.DefocusWaves)
            && AstigmatismVerticalWaves.Equals(other.AstigmatismVerticalWaves)
            && AstigmatismObliqueWaves.Equals(other.AstigmatismObliqueWaves)
            && ComaHorizontalWaves.Equals(other.ComaHorizontalWaves)
            && ComaVerticalWaves.Equals(other.ComaVerticalWaves)
            && TrefoilHorizontalWaves.Equals(other.TrefoilHorizontalWaves)
            && TrefoilVerticalWaves.Equals(other.TrefoilVerticalWaves)
            && SphericalWaves.Equals(other.SphericalWaves);
    }

    public override bool Equals(object? obj) => obj is WavefrontAberration other && Equals(other);

    public override int GetHashCode()
    {
        var first = HashCode.Combine(DefocusWaves, AstigmatismVerticalWaves, AstigmatismObliqueWaves, ComaHorizontalWaves);
        var second = HashCode.Combine(ComaVerticalWaves, TrefoilHorizontalWaves, TrefoilVerticalWaves, SphericalWaves);
        return HashCode.Combine(first, second);
    }

    public static bool operator ==(WavefrontAberration left, WavefrontAberration right) => left.Equals(right);
    public static bool operator !=(WavefrontAberration left, WavefrontAberration right) => !left.Equals(right);
}
