using WaveOptics.Abstractions;

namespace WaveOptics.Optics;

internal static class ZernikeWavefront
{
    static readonly double Sqrt3 = Math.Sqrt(3);
    static readonly double Sqrt5 = Math.Sqrt(5);
    static readonly double Sqrt6 = Math.Sqrt(6);
    static readonly double Sqrt8 = Math.Sqrt(8);

    public static double Evaluate(double x, double y, WavefrontAberration aberration)
    {
        var radiusSquared = x * x + y * y;
        var radiusFourth = radiusSquared * radiusSquared;
        var defocus = Sqrt3 * (2 * radiusSquared - 1);
        var astigmatismVertical = Sqrt6 * (x * x - y * y);
        var astigmatismOblique = 2 * Sqrt6 * x * y;
        var comaHorizontal = Sqrt8 * (3 * radiusSquared - 2) * x;
        var comaVertical = Sqrt8 * (3 * radiusSquared - 2) * y;
        var trefoilHorizontal = Sqrt8 * x * (x * x - 3 * y * y);
        var trefoilVertical = Sqrt8 * y * (3 * x * x - y * y);
        var spherical = Sqrt5 * (6 * radiusFourth - 6 * radiusSquared + 1);

        return aberration.DefocusWaves * defocus
            + aberration.AstigmatismVerticalWaves * astigmatismVertical
            + aberration.AstigmatismObliqueWaves * astigmatismOblique
            + aberration.ComaHorizontalWaves * comaHorizontal
            + aberration.ComaVerticalWaves * comaVertical
            + aberration.TrefoilHorizontalWaves * trefoilHorizontal
            + aberration.TrefoilVerticalWaves * trefoilVertical
            + aberration.SphericalWaves * spherical;
    }
}
