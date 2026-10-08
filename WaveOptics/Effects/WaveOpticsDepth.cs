using System.Numerics;
using WaveOptics.Optics;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Effects;

internal static class WaveOpticsDepth
{
    public const float BasePerspectiveDistance = 1000f;
    public const double MinimumDistanceRatio = 1.001;
    const double Sqrt12 = 3.4641016151377544;
    const double MillimetresPerNanometre = 1e-6;

    public static bool TryGetDistance(DrawDescription drawDescription, out double distance)
    {
        distance = 0d;
        var requested = drawDescription.PerspectiveDistance;
        var perspectiveDistance = requested > 0f ? requested.Value : BasePerspectiveDistance;
        if (!float.IsFinite(perspectiveDistance))
            return false;

        var position = Vector4.Transform(new Vector4(drawDescription.Draw, 1f), drawDescription.Camera);
        var result = perspectiveDistance * (double)position.W - position.Z;
        if (!double.IsFinite(result) || result <= 0d)
            return false;

        distance = result;
        return true;
    }

    public static double ReferenceWavelength(WaveOpticsColorMode colorMode, double wavelength)
        => colorMode == WaveOpticsColorMode.Monochrome ? wavelength : SpectralPlan.ReferenceWavelength;

    public static double DefocusWaves(double focalLength, double fNumber, double focusDistance, double distance, double wavelength)
    {
        var minimum = focalLength * MinimumDistanceRatio;
        var focus = Math.Max(focusDistance, minimum);
        var subject = Math.Max(distance, minimum);
        var apertureDiameter = focalLength / fNumber;
        var pathDifference = apertureDiameter * apertureDiameter / 8d * (1d / focus - 1d / subject);
        return pathDifference / (Sqrt12 * wavelength * MillimetresPerNanometre);
    }

    public static double AddDefocus(double defocus, DrawDescription drawDescription, double focalLength, double fNumber, double focusDistance, double wavelength)
        => TryGetDistance(drawDescription, out var distance)
            ? defocus + DefocusWaves(focalLength, fNumber, focusDistance, distance, wavelength)
            : defocus;
}
