using System.Globalization;
using System.Numerics;
using ComputeWeave;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using WaveOptics.Effects;
using WaveOptics.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Tests;

[Collection("Direct2D")]
public sealed class WaveOpticsEffectProcessorTests
{
    const int Size = 64;
    const int Length = 30;
    const int Start = 16;
    const int End = 48;

    static readonly Bgra Gray = Bgra.Opaque(192, 192, 192);

    static Bgra CenteredSquare(int x, int y) => x is >= Start and < End && y is >= Start and < End ? Gray : Bgra.Transparent;

    static void RequireInterop(IGraphicsDevicesAndContext devices)
    {
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = WaveOpticsInteropProvider.TryCreate(devices, scheduler, out var device);
        if (provider is null || device is null)
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
    }

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}"""))!;

    static Rendering RenderFrame(IGraphicsDevicesAndContext devices, IVideoEffectProcessor processor, int frame)
    {
        processor.Update(EffectDescriptions.At(frame, Length));
        return Rendering.Capture(devices, processor.Output);
    }

    static void AssertSameAsSource(Rendering rendering, SourceImage source)
    {
        Assert.Equal((0, 0, source.Width, source.Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.Equal(source[point.X, point.Y], rendering[point.X, point.Y]));
    }

    static Bgra PixelAt(Rendering rendering, int x, int y) => rendering.Contains(x, y) ? rendering[x, y] : Bgra.Transparent;

    static bool HasLightOutside(Rendering rendering, SourceImage source)
        => rendering.Coordinates().Any(point => (!source.Contains(point.X, point.Y) || source[point.X, point.Y].Alpha == 0) && rendering[point.X, point.Y].Alpha > 0);

    static int DistanceFromSquare(int x, int y)
        => Math.Max(Math.Max(Math.Max(Start - x, x - (End - 1)), Math.Max(Start - y, y - (End - 1))), 0);

    static bool WithinRounding(Bgra expected, Bgra actual)
        => Math.Abs(expected.Blue - actual.Blue) <= 1 && Math.Abs(expected.Green - actual.Green) <= 1 && Math.Abs(expected.Red - actual.Red) <= 1 && Math.Abs(expected.Alpha - actual.Alpha) <= 1;

    [Fact]
    public void TheProcessorHandsTheDrawDescriptionBackUnchanged()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new WaveOpticsEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var description = EffectDescriptions.At(0, Length);

        var draw = processor.Update(description);

        Assert.Same(description.DrawDescription, draw);
    }

    [Fact]
    public void TheBlurSpreadsOutToTheKernelRadius()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new WaveOpticsEffect { KernelRadius = 4 };
        effect.FNumber.Values[0].Value = 32d;
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        Assert.Contains(rendering.Coordinates(), point => DistanceFromSquare(point.X, point.Y) == effect.KernelRadius && rendering[point.X, point.Y].Alpha > 0);
        Assert.All(rendering.Coordinates(), point =>
        {
            var pixel = rendering[point.X, point.Y];
            Assert.InRange(pixel.Blue, 0, pixel.Alpha);
            Assert.InRange(pixel.Green, 0, pixel.Alpha);
            Assert.InRange(pixel.Red, 0, pixel.Alpha);
            if (DistanceFromSquare(point.X, point.Y) > effect.KernelRadius)
                Assert.Equal(Bgra.Transparent, pixel);
        });
    }

    [Fact]
    public void TheBlurIsCenteredOnTheSource()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new WaveOpticsEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        Assert.True(HasLightOutside(rendering, source));
        Assert.All(rendering.Coordinates(), point =>
        {
            var pixel = rendering[point.X, point.Y];
            Assert.True(WithinRounding(pixel, PixelAt(rendering, Size - 1 - point.X, point.Y)), $"({point.X}, {point.Y}) {pixel}");
            Assert.True(WithinRounding(pixel, PixelAt(rendering, point.X, Size - 1 - point.Y)), $"({point.X}, {point.Y}) {pixel}");
        });
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(-37, 21)]
    public void TheBlurTravelsWithTheImage(int dx, int dy)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var moved = new AffineTransform2D(context.DeviceContext)
        {
            InterPolationMode = AffineTransform2DInterpolationMode.NearestNeighbor,
            BorderMode = BorderMode.Hard,
            TransformMatrix = Matrix3x2.CreateTranslation(dx, dy),
        };
        moved.SetInput(0, source.Bitmap, true);
        using var movedOutput = moved.Output;
        using var inPlaceProcessor = new WaveOpticsEffect().CreateVideoEffect(context);
        inPlaceProcessor.SetInput(source.Bitmap);
        var inPlace = RenderFrame(context, inPlaceProcessor, 0);
        using var travelledProcessor = new WaveOpticsEffect().CreateVideoEffect(context);
        travelledProcessor.SetInput(movedOutput);

        var travelled = RenderFrame(context, travelledProcessor, 0);

        Assert.Equal((inPlace.Left + dx, inPlace.Top + dy, inPlace.Width, inPlace.Height), (travelled.Left, travelled.Top, travelled.Width, travelled.Height));
        Assert.All(inPlace.Coordinates(), point => Assert.True(inPlace[point.X, point.Y] == travelled[point.X + dx, point.Y + dy], $"({point.X}, {point.Y})"));
    }

    [Fact]
    public void AZeroAmountPassesTheImageThrough()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new WaveOpticsEffect();
        effect.Amount.Values[0].Value = 0d;
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        AssertSameAsSource(rendering, source);
    }

    [Fact]
    public void AZeroAmountAfterABlurredFramePassesTheImageThrough()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = SourceImage.Solid(context, Size, Size, Gray);
        var effect = new WaveOpticsEffect();
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var blurred = RenderFrame(context, processor, 0);
        effect.Amount.Values[0].Value = 0d;

        var passed = RenderFrame(context, processor, 0);

        Assert.True(HasLightOutside(blurred, source));
        AssertSameAsSource(passed, source);
    }

    [Theory]
    [InlineData(WaveOpticsSettings.MaximumCanvasSize - WaveOpticsSettings.DefaultCanvasMargin * 2, true)]
    [InlineData(WaveOpticsSettings.MaximumCanvasSize - WaveOpticsSettings.DefaultCanvasMargin * 2 + 1, false)]
    public void OnlyAnImageThatLeavesRoomForTheMarginIsBlurred(int width, bool blurred)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        if (blurred)
            RequireInterop(context);
        using var source = SourceImage.Solid(context, width, 8, Gray);
        using var processor = new WaveOpticsEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        if (blurred)
            Assert.True(HasLightOutside(rendering, source));
        else
            AssertSameAsSource(rendering, source);
    }

    [Theory]
    [InlineData(40, 8192 - 40 * 2, true)]
    [InlineData(40, 8192 - 40 * 2 + 1, false)]
    [InlineData(63, 8192 - 64 * 2, true)]
    [InlineData(63, 8192 - 64 * 2 + 1, false)]
    public void AWideKernelRadiusKeepsRoomForItsOwnMargin(int radius, int width, bool blurred)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        if (blurred)
            RequireInterop(context);
        using var source = SourceImage.Solid(context, width, 8, Gray);
        using var processor = new WaveOpticsEffect { KernelRadius = radius }.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        if (blurred)
            Assert.True(HasLightOutside(rendering, source));
        else
            AssertSameAsSource(rendering, source);
    }

    public static readonly TheoryData<string, Action<WaveOpticsEffect>> Animations = new()
    {
        { nameof(WaveOpticsEffect.Amount), effect => effect.Amount.CopyFrom(Linear(20d, 100d)) },
        { nameof(WaveOpticsEffect.Gain), effect => effect.Gain.CopyFrom(Linear(50d, 150d)) },
        { nameof(WaveOpticsEffect.Defocus), effect => effect.Defocus.CopyFrom(Linear(0d, 2d)) },
    };

    [Theory]
    [MemberData(nameof(Animations))]
    public void ReturningToAFrameReproducesItExactly(string setting, Action<WaveOpticsEffect> animate)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new WaveOpticsEffect();
        animate(effect);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var first = RenderFrame(context, processor, 4);
        var other = RenderFrame(context, processor, 24);
        var again = RenderFrame(context, processor, 4);

        Assert.False(first.SamePixelsAs(other), setting);
        Assert.True(first.SamePixelsAs(again), setting);
    }

    [Fact]
    public void ANewProcessorDrawsTheSameBlur()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new WaveOpticsEffect { ApertureShape = WaveOpticsApertureShape.RegularPolygon };
        effect.Defocus.Values[0].Value = 0.5d;
        using var firstProcessor = effect.CreateVideoEffect(context);
        firstProcessor.SetInput(source.Bitmap);
        var first = RenderFrame(context, firstProcessor, 0);
        using var secondProcessor = effect.CreateVideoEffect(context);
        secondProcessor.SetInput(source.Bitmap);

        var second = RenderFrame(context, secondProcessor, 0);

        Assert.True(first.SamePixelsAs(second));
    }

    [Fact]
    public void AnimatedAmountIsReadAtEachFrame()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new WaveOpticsEffect();
        effect.Amount.CopyFrom(Linear(0d, 100d));
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var start = RenderFrame(context, processor, 0);
        var end = RenderFrame(context, processor, Length - 1);

        AssertSameAsSource(start, source);
        Assert.True(HasLightOutside(end, source));
    }

    [Theory]
    [InlineData(15, 3)]
    [InlineData(3, 15)]
    public void AProcessorThatDrewAnotherRadiusDrawsLikeAFreshOne(int firstRadius, int secondRadius)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new WaveOpticsEffect { KernelRadius = firstRadius };
        effect.FNumber.Values[0].Value = 32d;
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        RenderFrame(context, processor, 0);
        effect.KernelRadius = secondRadius;
        using var fresh = effect.CreateVideoEffect(context);
        fresh.SetInput(source.Bitmap);
        var expected = RenderFrame(context, fresh, 0);

        var reused = RenderFrame(context, processor, 0);

        Assert.True(reused.SamePixelsAs(expected));
    }

    public static readonly TheoryData<string, Action<WaveOpticsEffect>> LaterChanges = new()
    {
        { nameof(WaveOpticsEffect.Amount), effect => effect.Amount.Values[0].Value = 50d },
        { nameof(WaveOpticsEffect.Gain), effect => effect.Gain.Values[0].Value = 50d },
        { nameof(WaveOpticsEffect.Wavelength), effect => effect.Wavelength.Values[0].Value = 700d },
        { nameof(WaveOpticsEffect.FNumber), effect => effect.FNumber.Values[0].Value = 16d },
        { nameof(WaveOpticsEffect.PixelPitch), effect => effect.PixelPitch.Values[0].Value = 2d },
        { nameof(WaveOpticsEffect.KernelRadius), effect => effect.KernelRadius = 2 },
        { nameof(WaveOpticsEffect.Quality), effect => effect.Quality = WaveOpticsQuality.Draft },
        { nameof(WaveOpticsEffect.ApertureShape), effect => effect.ApertureShape = WaveOpticsApertureShape.Circular },
        { nameof(WaveOpticsEffect.BladeCount), effect => effect.BladeCount = 3 },
        { nameof(WaveOpticsEffect.BladeRotation), effect => effect.BladeRotation.Values[0].Value = 30d },
        { nameof(WaveOpticsEffect.Obstruction), effect => effect.Obstruction.Values[0].Value = 50d },
        { nameof(WaveOpticsEffect.Defocus), effect => effect.Defocus.Values[0].Value = 1d },
        { nameof(WaveOpticsEffect.AstigmatismVertical), effect => effect.AstigmatismVertical.Values[0].Value = 1d },
        { nameof(WaveOpticsEffect.AstigmatismOblique), effect => effect.AstigmatismOblique.Values[0].Value = 1d },
        { nameof(WaveOpticsEffect.ComaHorizontal), effect => effect.ComaHorizontal.Values[0].Value = 1d },
        { nameof(WaveOpticsEffect.ComaVertical), effect => effect.ComaVertical.Values[0].Value = 1d },
        { nameof(WaveOpticsEffect.Spherical), effect => effect.Spherical.Values[0].Value = 1d },
    };

    [Theory]
    [MemberData(nameof(LaterChanges))]
    public void EverySettingChangedAfterTheFirstFrameReachesTheEffect(string setting, Action<WaveOpticsEffect> change)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new WaveOpticsEffect { ApertureShape = WaveOpticsApertureShape.RegularPolygon };
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var before = RenderFrame(context, processor, 0);
        change(effect);
        var after = RenderFrame(context, processor, 0);

        Assert.False(before.SamePixelsAs(after), setting);
    }

    [Theory]
    [InlineData(32, 128)]
    [InlineData(128, 32)]
    public void AResizedSourceKeepsBeingBlurred(int firstSize, int secondSize)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var first = SourceImage.Solid(context, firstSize, firstSize, Gray);
        using var second = SourceImage.Solid(context, secondSize, secondSize, Gray);
        using var processor = new WaveOpticsEffect().CreateVideoEffect(context);
        processor.SetInput(first.Bitmap);
        var before = RenderFrame(context, processor, 0);

        processor.SetInput(second.Bitmap);
        var after = RenderFrame(context, processor, 0);

        Assert.True(HasLightOutside(before, first));
        Assert.True(HasLightOutside(after, second));
        Assert.Equal(secondSize > firstSize, after.Width > before.Width);
    }

    [Fact]
    public void TheBlurAppearsOnceATransparentSourceTakesShape()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var empty = SourceImage.Solid(context, Size, Size, Bgra.Transparent);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new WaveOpticsEffect().CreateVideoEffect(context);
        processor.SetInput(empty.Bitmap);
        var before = RenderFrame(context, processor, 0);

        processor.SetInput(source.Bitmap);
        var after = RenderFrame(context, processor, 0);

        AssertSameAsSource(before, empty);
        Assert.True(HasLightOutside(after, source));
    }

    [Theory]
    [InlineData(Size, Size, false)]
    [InlineData(WaveOpticsSettings.MaximumCanvasSize - WaveOpticsSettings.DefaultCanvasMargin * 2 + 1, 8, true)]
    public void TheBlurReturnsAfterAnImageThatPassedThrough(int width, int height, bool opaque)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var passing = SourceImage.Solid(context, width, height, opaque ? Gray : Bgra.Transparent);
        using var processor = new WaveOpticsEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var before = RenderFrame(context, processor, 0);
        processor.SetInput(passing.Bitmap);
        var between = RenderFrame(context, processor, 0);

        processor.SetInput(source.Bitmap);
        var after = RenderFrame(context, processor, 0);

        Assert.True(HasLightOutside(before, source));
        AssertSameAsSource(between, passing);
        Assert.True(after.SamePixelsAs(before));
    }

    [Fact]
    public void AnApertureWithoutAnOpenSamplePassesTheImageThroughUntilItOpens()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new WaveOpticsEffect { Quality = WaveOpticsQuality.Draft, ApertureShape = WaveOpticsApertureShape.RegularPolygon, BladeCount = 4 };
        effect.BladeRotation.Values[0].Value = -357.5d;
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var open = RenderFrame(context, processor, 0);
        effect.Obstruction.Values[0].Value = 95d;
        var blocked = RenderFrame(context, processor, 0);
        effect.Obstruction.Values[0].Value = 0d;

        var reopened = RenderFrame(context, processor, 0);

        Assert.True(HasLightOutside(open, source));
        AssertSameAsSource(blocked, source);
        Assert.True(reopened.SamePixelsAs(open));
    }

    [Fact]
    public void AFailureWhileUpdatingIsNotSwallowed()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var processor = new WaveOpticsEffect().CreateVideoEffect(context);

        Assert.ThrowsAny<Exception>(() => processor.Update(null!));
    }
}
