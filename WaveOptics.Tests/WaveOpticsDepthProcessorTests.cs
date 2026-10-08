using System.Globalization;
using System.Numerics;
using ComputeGuard;
using WaveOptics.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Tests;

[Collection("Direct2D")]
public sealed class WaveOpticsDepthProcessorTests
{
    const int Size = 96;
    const int Length = 30;
    const double Wavelength = 550d;

    static readonly Bgra Warm = Bgra.Opaque(220, 160, 90);

    static Bgra Shape(int x, int y)
        => x is >= 32 and < 64 && y is >= 32 and < 64 && (x + 2 * y) % 9 != 0 ? Warm : Bgra.Transparent;

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}"""))!;

    static WaveOpticsEffect Effect(bool useDepth = true, WaveOpticsColorMode colorMode = WaveOpticsColorMode.Monochrome)
        => new() { UseDepth = useDepth, ColorMode = colorMode };

    static Rendering Render(WaveOpticsEffect effect, Vector3 position, Matrix4x4? camera = null, int frame = 0)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, Shape);
        using var processor = new WaveOpticsEffectProcessor(context, effect, new ComputeGuardian(new ComputeGuardianOptions { Report = _ => { } }), null, false);
        processor.SetInput(source.Bitmap);
        processor.Update(EffectDescriptions.At(frame, Length, EffectDescriptions.Draw(position, camera)));
        return Rendering.Capture(context, processor.Output);
    }

    static Vector3 At(float z) => new(0f, 0f, z);

    static int DistanceFromSquare(int x, int y)
        => Math.Max(Math.Max(Math.Max(32 - x, x - 63), Math.Max(32 - y, y - 63)), 0);

    static double LightBeyond(Rendering rendering, int distance)
        => rendering.Coordinates().Where(point => DistanceFromSquare(point.X, point.Y) > distance).Sum(point => (double)rendering[point.X, point.Y].Alpha);

    static void AssertIdentical(Rendering expected, Rendering actual) => Assert.True(expected.SamePixelsAs(actual));

    static void AssertDiffers(Rendering expected, Rendering actual) => Assert.False(expected.SamePixelsAs(actual));

    static double DepthWaves(float z, double focusDistance = 1000d, double focalLength = 50d, double fNumber = 8d, double wavelength = Wavelength)
        => WaveOpticsDepth.DefocusWaves(focalLength, fNumber, focusDistance, 1000d - z, wavelength);

    [Fact]
    public void AnItemAtTheFocusDistanceIsRenderedAsWithoutDepth()
        => AssertIdentical(Render(Effect(false), At(0f)), Render(Effect(), At(0f)));

    [Fact]
    public void WithoutDepthTheDistanceOfTheItemChangesNothing()
        => AssertIdentical(Render(Effect(false), At(0f)), Render(Effect(false), At(-500f)));

    [Theory]
    [InlineData(-500f)]
    [InlineData(-200f)]
    [InlineData(300f)]
    public void TheDepthDefocusIsTheDefocusOfTheSameNumberOfWaves(float z)
    {
        var manual = Effect(false);
        manual.Defocus.Values[0].Value = DepthWaves(z);

        AssertIdentical(Render(manual, At(0f)), Render(Effect(), At(z)));
    }

    [Fact]
    public void TheDepthDefocusAddsToTheDefocusTheUserSet()
    {
        const double set = 0.3d;
        var manual = Effect(false);
        manual.Defocus.Values[0].Value = (double)(float)set + DepthWaves(-500f);
        var combined = Effect();
        combined.Defocus.Values[0].Value = set;

        AssertIdentical(Render(manual, At(0f)), Render(combined, At(-500f)));
    }

    [Fact]
    public void ABlurredItemSpreadsFartherThanAnItemAtTheFocus()
    {
        var sharp = Render(Effect(), At(0f));

        var behind = Render(Effect(), At(-500f));
        var inFront = Render(Effect(), At(500f));

        Assert.True(LightBeyond(behind, 3) > 4d * LightBeyond(sharp, 3));
        Assert.True(LightBeyond(inFront, 3) > 4d * LightBeyond(sharp, 3));
    }

    [Fact]
    public void TheFocusDistanceChoosesThePlaneThatStaysSharp()
    {
        var focused = Effect();
        focused.FocusDistance.Values[0].Value = 1500d;

        AssertIdentical(Render(Effect(false), At(0f)), Render(focused, At(-500f)));
    }

    [Fact]
    public void TheFocalLengthChangesHowFastTheBlurGrows()
    {
        var longLens = Effect();
        longLens.FocalLength.Values[0].Value = 100d;

        var reference = Render(Effect(), At(-300f));
        var stronger = Render(longLens, At(-300f));

        Assert.True(LightBeyond(stronger, 8) > 1.5d * LightBeyond(reference, 8));
    }

    [Fact]
    public void MovingTheCameraBlursTheItem()
    {
        var camera = Matrix4x4.CreateTranslation(0f, 0f, 300f);

        var sharp = Render(Effect(), At(0f));
        var moved = Render(Effect(), At(0f), camera);

        AssertDiffers(sharp, moved);
        AssertIdentical(Render(Effect(), At(300f)), moved);
    }

    [Theory]
    [InlineData(1000f)]
    [InlineData(1500f)]
    public void AnItemWithoutADistanceKeepsTheDefocusTheUserSet(float z)
    {
        var effect = Effect();
        effect.Defocus.Values[0].Value = 0.4d;
        var manual = Effect(false);
        manual.Defocus.Values[0].Value = 0.4d;

        AssertIdentical(Render(manual, At(0f)), Render(effect, At(z)));
    }

    [Fact]
    public void ThePrimariesAndBroadbandCountWavesIn550Nanometres()
    {
        var violet = Effect(true, WaveOpticsColorMode.Primaries);
        violet.Wavelength.Values[0].Value = 400d;

        AssertIdentical(Render(Effect(true, WaveOpticsColorMode.Primaries), At(-500f)), Render(violet, At(-500f)));
    }

    [Fact]
    public void AMonochromeWaveCountFollowsTheChosenWavelength()
    {
        var red = Effect();
        red.Wavelength.Values[0].Value = 650d;
        var manual = Effect(false);
        manual.Wavelength.Values[0].Value = 650d;
        manual.Defocus.Values[0].Value = DepthWaves(-500f, wavelength: 650d);

        AssertIdentical(Render(manual, At(0f)), Render(red, At(-500f)));
        AssertDiffers(Render(Effect(), At(-500f)), Render(red, At(-500f)));
    }

    [Fact]
    public void TheFNumberAndTheFocalLengthAreTakenFromTheFrame()
    {
        var effect = Effect();
        effect.FNumber.Values[0].Value = 4d;
        effect.FocalLength.Values[0].Value = 35d;
        var manual = Effect(false);
        manual.FNumber.Values[0].Value = 4d;
        manual.Defocus.Values[0].Value = DepthWaves(-500f, focalLength: 35d, fNumber: 4d);

        AssertIdentical(Render(manual, At(0f)), Render(effect, At(-500f)));
    }

    [Fact]
    public void AnAnimatedFocusDistancePullsTheFocusToTheItem()
    {
        var pulled = Effect();
        pulled.FocusDistance.CopyFrom(Linear(1000d, 1500d));

        var first = Render(pulled, At(-500f), null, 0);
        var middle = Render(pulled, At(-500f), null, Length / 2);
        var last = Render(pulled, At(-500f), null, Length - 1);

        Assert.True(LightBeyond(first, 3) > 1.2d * LightBeyond(middle, 3));
        Assert.True(LightBeyond(middle, 3) > 1.5d * LightBeyond(last, 3));
    }
}
