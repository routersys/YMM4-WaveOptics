using System.Numerics;
using System.Reflection;
using Vortice.Direct2D1;
using WaveOptics.Abstractions;
using WaveOptics.Effects;
using WaveOptics.Optics;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Tests;

public sealed class WaveOpticsDepthTests
{
    const double Wavelength = 550d;

    static DrawDescription Draw(Vector3 position, Matrix4x4? camera = null, float? perspectiveDistance = null)
        => new DrawDescription(position, Vector2.Zero, Vector2.One, Vector3.Zero, camera ?? Matrix4x4.Identity, InterpolationMode.Linear, 1d, false, [])
        {
            PerspectiveDistance = perspectiveDistance,
        };

    static DrawDescription AtDepth(float z, Matrix4x4? camera = null, float? perspectiveDistance = null)
        => Draw(new Vector3(300f, -200f, z), camera, perspectiveDistance);

    static double DistanceOf(DrawDescription drawDescription)
    {
        Assert.True(WaveOpticsDepth.TryGetDistance(drawDescription, out var distance));
        return distance;
    }

    static double CircleOfConfusionRadius(double focalLength, double fNumber, double pixelPitchMicrometers, double focusDistance, double distance)
        => focalLength * focalLength / (2d * fNumber * pixelPitchMicrometers / 1000d) * Math.Abs(1d / focusDistance - 1d / distance);

    [Theory]
    [InlineData(0f, 1000d)]
    [InlineData(250f, 750d)]
    [InlineData(-500f, 1500d)]
    [InlineData(999f, 1d)]
    public void TheDistanceIsTheBasePerspectiveDistanceMinusZ(float z, double expected)
    {
        Assert.Equal(1000f, WaveOpticsDepth.BasePerspectiveDistance);

        Assert.Equal(expected, DistanceOf(AtDepth(z)), 6);
    }

    [Theory]
    [InlineData(0f, 0f, 1000d)]
    [InlineData(5000f, -3000f, 1000d)]
    [InlineData(-20000f, 20000f, 1000d)]
    public void TheDistanceDoesNotDependOnTheSidewaysPosition(float x, float y, double expected)
        => Assert.Equal(expected, DistanceOf(Draw(new Vector3(x, y, 0f))), 6);

    [Fact]
    public void MovingTheCameraForwardShortensTheDistance()
    {
        var camera = Matrix4x4.CreateTranslation(0f, 0f, 300f);

        Assert.Equal(700d, DistanceOf(AtDepth(0f, camera)), 4);
        Assert.Equal(600d, DistanceOf(AtDepth(100f, camera)), 4);
    }

    [Theory]
    [InlineData(60d, 500d)]
    [InlineData(-60d, 500d)]
    [InlineData(80d, 173.64817766693033d)]
    [InlineData(0d, 1000d)]
    public void ARotatedCameraMeasuresTheDepthAlongTheViewingAxis(double degrees, double expected)
    {
        var camera = Matrix4x4.CreateRotationY((float)(Math.PI * degrees / 180d), new Vector3(0f, 0f, 1000f));

        var found = WaveOpticsDepth.TryGetDistance(Draw(Vector3.Zero, camera), out var distance);

        Assert.Equal(expected > 1d, found);
        if (found)
            Assert.Equal(expected, distance, 3);
    }

    [Theory]
    [InlineData(2000f, 0f, 2000d)]
    [InlineData(2000f, 500f, 1500d)]
    [InlineData(400f, 100f, 300d)]
    public void APerspectiveDistanceReplacesTheBaseDistance(float perspectiveDistance, float z, double expected)
        => Assert.Equal(expected, DistanceOf(AtDepth(z, null, perspectiveDistance)), 4);

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NegativeInfinity)]
    public void AnInvalidPerspectiveDistanceIsTheBaseDistanceAsInTheHost(float perspectiveDistance)
        => Assert.Equal(750d, DistanceOf(AtDepth(250f, null, perspectiveDistance)), 4);

    [Fact]
    public void AnInfinitePerspectiveDistanceHasNoDistance()
    {
        Assert.False(WaveOpticsDepth.TryGetDistance(AtDepth(0f, null, float.PositiveInfinity), out var distance));
        Assert.Equal(0d, distance);
    }

    [Theory]
    [InlineData(1000f)]
    [InlineData(1000.5f)]
    [InlineData(1500f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void AnItemAtOrBehindTheCameraHasNoDistance(float z)
    {
        Assert.False(WaveOpticsDepth.TryGetDistance(AtDepth(z), out var distance));
        Assert.Equal(0d, distance);
    }

    [Fact]
    public void ACameraThatIsNotFiniteHasNoDistance()
    {
        var camera = Matrix4x4.Identity;
        camera.M43 = float.NaN;

        Assert.False(WaveOpticsDepth.TryGetDistance(AtDepth(0f, camera), out _));
    }

    static Matrix4x4 Camera(float x, float y, float z, double yaw, double pitch)
        => Matrix4x4.CreateTranslation(x, y, z)
            * Matrix4x4.CreateRotationY((float)(Math.PI * yaw / 180d), new Vector3(0f, 0f, 1000f))
            * Matrix4x4.CreateRotationX((float)(Math.PI * pitch / 180d), new Vector3(0f, 0f, 1000f));

    public static TheoryData<float, float, float, double, double, float?, BillboardMode> HostCases { get; } = new()
    {
        { 0f, 0f, 0f, 0d, 0d, null, BillboardMode.None },
        { 40f, -20f, 300f, 0d, 0d, null, BillboardMode.None },
        { 0f, 0f, -250f, 25d, -10d, null, BillboardMode.None },
        { 80f, 30f, 120f, -35d, 20d, 400f, BillboardMode.None },
        { 80f, 30f, 120f, -35d, 20d, 2500f, BillboardMode.Spherical },
        { -60f, 10f, -90f, 50d, 15d, null, BillboardMode.Spherical },
        { 10f, 10f, 60f, 70d, -25d, 1800f, BillboardMode.Cylindrical },
        { 0f, 0f, 0f, 0d, 0d, 1200f, BillboardMode.Cylindrical },
    };

    [Theory]
    [MemberData(nameof(HostCases))]
    public void TheDistanceMatchesTheCameraTheHostFinalizes(float x, float y, float z, double yaw, double pitch, float? perspectiveDistance, BillboardMode billboard)
    {
        var finalizer = Type.GetType("YukkuriMovieMaker.Player.Video.CameraFinalizer, YukkuriMovieMaker");
        var apply = finalizer?.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
        if (apply is null)
            Assert.Skip("The host camera finalizer is unavailable.");

        foreach (var position in new[] { new Vector3(120f, -80f, 200f), new Vector3(0f, 0f, -700f), new Vector3(-30f, 55f, 0f) })
        {
            var drawDescription = Draw(position, Camera(x, y, z, yaw, pitch), perspectiveDistance) with { Billboard = billboard };
            var finalized = (DrawDescription)apply.Invoke(null, [drawDescription])!;
            var projection = new Matrix4x4(1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, -0.001f, 0f, 0f, 0f, 1f);
            var matrix = Matrix4x4.CreateTranslation(finalized.Draw) * finalized.Camera * projection;
            var w = Vector4.Transform(new Vector4(0f, 0f, 0f, 1f), matrix).W;
            var expected = (perspectiveDistance is > 0f ? perspectiveDistance.Value : WaveOpticsDepth.BasePerspectiveDistance) * (double)w;

            var found = WaveOpticsDepth.TryGetDistance(drawDescription, out var distance);

            Assert.Equal(expected > 0d, found);
            if (found)
                Assert.Equal(expected, distance, expected * 1e-4);
        }
    }

    [Theory]
    [InlineData(380d)]
    [InlineData(550d)]
    [InlineData(650d)]
    public void AMonochromeWaveCountIsMeasuredInTheChosenWavelength(double wavelength)
        => Assert.Equal(wavelength, WaveOpticsDepth.ReferenceWavelength(WaveOpticsColorMode.Monochrome, wavelength));

    [Theory]
    [InlineData(WaveOpticsColorMode.Primaries, 380d)]
    [InlineData(WaveOpticsColorMode.Primaries, 650d)]
    [InlineData(WaveOpticsColorMode.Broadband, 380d)]
    [InlineData(WaveOpticsColorMode.Broadband, 780d)]
    public void APrimariesOrBroadbandWaveCountIsMeasuredIn550Nanometres(WaveOpticsColorMode colorMode, double wavelength)
        => Assert.Equal(550d, WaveOpticsDepth.ReferenceWavelength(colorMode, wavelength));

    [Fact]
    public void TheDefocusIsZeroAtTheFocusDistance()
        => Assert.Equal(0d, WaveOpticsDepth.DefocusWaves(50d, 8d, 1500d, 1500d, Wavelength));

    [Fact]
    public void ADistantSubjectOfAFiftyMillimetreLensAtF8NeedsAboutThreeQuartersOfAWave()
        => Assert.Equal(0.7688435758029464, WaveOpticsDepth.DefocusWaves(50d, 8d, 2000d, 5000d, Wavelength), 9);

    [Theory]
    [InlineData(2000d, 5000d)]
    [InlineData(1000d, 400d)]
    [InlineData(300d, 8000d)]
    public void SwappingTheFocusAndTheSubjectReversesTheSign(double focus, double subject)
    {
        var forward = WaveOpticsDepth.DefocusWaves(35d, 4d, focus, subject, Wavelength);
        var backward = WaveOpticsDepth.DefocusWaves(35d, 4d, subject, focus, Wavelength);

        Assert.NotEqual(0d, forward);
        Assert.Equal(-forward, backward, 12);
        Assert.Equal(subject > focus, forward > 0d);
    }

    [Fact]
    public void TheDefocusGrowsWithTheSquareOfTheFocalLengthAndShrinksWithTheSquareOfTheFNumber()
    {
        var reference = WaveOpticsDepth.DefocusWaves(50d, 8d, 2000d, 5000d, Wavelength);

        Assert.Equal(reference * 4d, WaveOpticsDepth.DefocusWaves(100d, 8d, 2000d, 5000d, Wavelength), 9);
        Assert.Equal(reference / 4d, WaveOpticsDepth.DefocusWaves(50d, 16d, 2000d, 5000d, Wavelength), 9);
    }

    [Fact]
    public void TheDefocusInWavesIsInverselyProportionalToTheWavelength()
    {
        var green = WaveOpticsDepth.DefocusWaves(50d, 8d, 2000d, 5000d, 550d);

        Assert.Equal(green * 550d / 650d, WaveOpticsDepth.DefocusWaves(50d, 8d, 2000d, 5000d, 650d), 9);
    }

    [Theory]
    [InlineData(50d, 8d, 4d, 2000d, 5000d)]
    [InlineData(35d, 2.8d, 2d, 1200d, 700d)]
    [InlineData(85d, 16d, 6d, 3000d, 1200d)]
    public void TheDefocusReproducesTheGeometricCircleOfConfusion(double focalLength, double fNumber, double pixelPitch, double focus, double subject)
    {
        var waves = WaveOpticsDepth.DefocusWaves(focalLength, fNumber, focus, subject, Wavelength);

        var edge = 4d * fNumber * (2d * Math.Sqrt(3d) * Math.Abs(waves) * Wavelength / 1000d) / pixelPitch;

        Assert.Equal(CircleOfConfusionRadius(focalLength, fNumber, pixelPitch, focus, subject), edge, 9);
    }

    [Fact]
    public void ThePointSpreadFunctionHasTheRadiusOfTheCircleOfConfusion()
    {
        const double focalLength = 50d;
        const double fNumber = 8d;
        const double pixelPitch = 4d;
        const double focus = 1000d;
        const double subject = 3000d;
        const int radius = 63;
        var waves = WaveOpticsDepth.DefocusWaves(focalLength, fNumber, focus, subject, Wavelength);
        var descriptor = new PsfDescriptor(512, 128, radius * 2 + 1, Wavelength, fNumber, pixelPitch, ApertureShape.Circular, 6, 0, 0, new WavefrontAberration(defocusWaves: waves));

        var kernel = new FraunhoferPsfGenerator().Generate(descriptor).Kernel;

        var distances = new List<(double Distance, double Value)>();
        for (var y = 0; y < kernel.Size; y++)
        {
            for (var x = 0; x < kernel.Size; x++)
                distances.Add((Math.Sqrt((x - radius) * (x - radius) + (y - radius) * (y - radius)), kernel[x, y]));
        }
        var energy = 0d;
        var ninetyPercentRadius = 0d;
        foreach (var (distance, value) in distances.OrderBy(entry => entry.Distance))
        {
            energy += value;
            ninetyPercentRadius = distance;
            if (energy >= 0.9)
                break;
        }

        var expected = CircleOfConfusionRadius(focalLength, fNumber, pixelPitch, focus, subject);
        Assert.InRange(ninetyPercentRadius, Math.Sqrt(0.9) * expected - 1d, Math.Sqrt(0.9) * expected + 1d);
    }

    [Theory]
    [InlineData(20d, 55d)]
    [InlineData(-3d, 55d)]
    [InlineData(10000d, 50d)]
    public void DistancesInsideTheFocalLengthAreHeldJustOutsideIt(double distance, double focalLength)
    {
        var held = WaveOpticsDepth.DefocusWaves(focalLength, 8d, 2000d, focalLength * WaveOpticsDepth.MinimumDistanceRatio, Wavelength);

        var waves = WaveOpticsDepth.DefocusWaves(focalLength, 8d, 2000d, distance, Wavelength);

        Assert.True(double.IsFinite(waves));
        if (distance < focalLength)
            Assert.Equal(held, waves, 12);
        else
            Assert.NotEqual(held, waves);
    }

    [Fact]
    public void AFocusDistanceInsideTheFocalLengthIsHeldJustOutsideIt()
    {
        var held = WaveOpticsDepth.DefocusWaves(50d, 8d, 50d * WaveOpticsDepth.MinimumDistanceRatio, 1000d, Wavelength);

        Assert.Equal(held, WaveOpticsDepth.DefocusWaves(50d, 8d, 10d, 1000d, Wavelength), 12);
        Assert.Equal(held, WaveOpticsDepth.DefocusWaves(50d, 8d, 50d, 1000d, Wavelength), 12);
    }

    [Fact]
    public void AddingTheDepthDefocusKeepsTheDefocusTheUserSet()
    {
        var drawDescription = AtDepth(-1000f);
        var depth = WaveOpticsDepth.DefocusWaves(50d, 8d, 1000d, 2000d, Wavelength);

        Assert.NotEqual(0d, depth);
        Assert.Equal(0.5d + depth, WaveOpticsDepth.AddDefocus(0.5d, drawDescription, 50d, 8d, 1000d, Wavelength), 12);
        Assert.Equal(-1.25d + depth, WaveOpticsDepth.AddDefocus(-1.25d, drawDescription, 50d, 8d, 1000d, Wavelength), 12);
    }

    [Theory]
    [InlineData(1000f, float.NaN)]
    [InlineData(0f, float.PositiveInfinity)]
    public void AnItemWithoutADistanceKeepsTheDefocusTheUserSet(float z, float perspectiveDistance)
        => Assert.Equal(0.5d, WaveOpticsDepth.AddDefocus(0.5d, AtDepth(z, null, perspectiveDistance), 50d, 8d, 500d, Wavelength));

    [Fact]
    public void AnItemAtTheFocusDistanceAddsNothing()
        => Assert.Equal(0.75d, WaveOpticsDepth.AddDefocus(0.75d, AtDepth(250f), 50d, 8d, 750d, Wavelength));
}
