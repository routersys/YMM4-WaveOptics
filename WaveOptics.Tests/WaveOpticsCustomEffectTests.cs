using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.Mathematics;
using WaveOptics.Rendering;
using YukkuriMovieMaker.Commons;

namespace WaveOptics.Tests;

[Collection("Direct2D")]
public sealed class WaveOpticsCustomEffectTests
{
    const int AmountIndex = 0;
    const int Width = 40;
    const int Height = 24;

    static readonly Bgra Blue = Bgra.Opaque(255, 0, 0);
    static readonly Bgra HalfConvolved = new(0, 0, 0, 128);

    static Rendering Render(IGraphicsDevicesAndContext devices, ID2D1Image source, ID2D1Image convolved, float amount)
    {
        using var effect = new WaveOpticsCustomEffect(devices);
        effect.SetInput(0, source, true);
        effect.SetInput(1, convolved, true);
        effect.Amount = amount;
        using var output = effect.Output;
        return Rendering.Capture(devices, output);
    }

    static AffineTransform2D Translate(IGraphicsDevicesAndContext devices, ID2D1Image image, float dx, float dy)
    {
        var transform = new AffineTransform2D(devices.DeviceContext)
        {
            InterPolationMode = AffineTransform2DInterpolationMode.NearestNeighbor,
            BorderMode = BorderMode.Hard,
            TransformMatrix = Matrix3x2.CreateTranslation(dx, dy),
        };
        transform.SetInput(0, image, true);
        return transform;
    }

    static ID2D1Bitmap1 RawBitmap(IGraphicsDevicesAndContext devices, int width, int height, Bgra premultiplied)
    {
        var stride = width * SourceImage.BytesPerPixel;
        var pixels = new byte[stride * height];
        for (var offset = 0; offset < pixels.Length; offset += SourceImage.BytesPerPixel)
        {
            pixels[offset] = premultiplied.Blue;
            pixels[offset + 1] = premultiplied.Green;
            pixels[offset + 2] = premultiplied.Red;
            pixels[offset + 3] = premultiplied.Alpha;
        }

        var bitmap = devices.DeviceContext.CreateBitmap(
            new SizeI(width, height), nint.Zero, stride,
            new BitmapProperties1(SourceImage.PixelFormat, SourceImage.Dpi, SourceImage.Dpi, BitmapOptions.None));
        bitmap.CopyFromMemory(pixels, stride);
        return bitmap;
    }

    static bool WithinRounding(Bgra expected, Bgra actual)
        => Math.Abs(expected.Blue - actual.Blue) <= 1 && Math.Abs(expected.Green - actual.Green) <= 1 && Math.Abs(expected.Red - actual.Red) <= 1 && Math.Abs(expected.Alpha - actual.Alpha) <= 1;

    static Bgra Mix(Bgra convolved, Bgra source, float amount)
    {
        byte Channel(byte convolvedValue, byte sourceValue) => (byte)Math.Round(convolvedValue * amount + sourceValue * (1d - amount), MidpointRounding.AwayFromZero);
        return new Bgra(Channel(convolved.Blue, source.Blue), Channel(convolved.Green, source.Green), Channel(convolved.Red, source.Red), Channel(convolved.Alpha, source.Alpha));
    }

    [Fact]
    public void TheEffectIsEnabledOnceCreated()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new WaveOpticsCustomEffect(context);

        Assert.True(effect.IsEnabled);
    }

    [Fact]
    public void TheAmountStartsFromZero()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new WaveOpticsCustomEffect(context);

        Assert.Equal(0f, effect.GetFloatValue(AmountIndex));
    }

    [Theory]
    [InlineData(-0.5f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.4f, 0.4f)]
    [InlineData(1f, 1f)]
    [InlineData(1.5f, 1f)]
    [InlineData(float.MaxValue, 1f)]
    public void TheAmountIsClampedToTheUnitRange(float value, float expected)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new WaveOpticsCustomEffect(context);

        effect.Amount = value;

        Assert.Equal(expected, effect.GetFloatValue(AmountIndex));
    }

    [Fact]
    public void TheOutputBoundsCoverTheSourceAndTheConvolution()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Blue);
        using var convolved = SourceImage.Solid(context, 8, 8, HalfConvolved);
        using var moved = Translate(context, convolved.Bitmap, 50f, -6f);
        using var movedOutput = moved.Output;
        using var effect = new WaveOpticsCustomEffect(context);
        effect.SetInput(0, source.Bitmap, true);
        effect.SetInput(1, movedOutput, true);
        effect.Amount = 1f;
        using var output = effect.Output;

        var bounds = context.DeviceContext.GetImageLocalBounds(output);

        Assert.Equal(0f, bounds.Left);
        Assert.Equal(-6f, bounds.Top);
        Assert.Equal(58f, bounds.Right);
        Assert.Equal((float)Height, bounds.Bottom);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void WithoutAmountTheSourcePassesThroughUntouched(float amount)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Blue);
        using var convolved = SourceImage.Solid(context, Width, Height, HalfConvolved);

        var rendering = Render(context, source.Bitmap, convolved.Bitmap, amount);

        Assert.All(rendering.Coordinates(), point => Assert.Equal(source[point.X, point.Y], rendering[point.X, point.Y]));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(0.5f)]
    [InlineData(0.25f)]
    public void TheSourceGivesWayToTheConvolutionByTheAmount(float amount)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Blue);
        using var convolved = SourceImage.Solid(context, Width, Height, HalfConvolved);
        var expected = Mix(HalfConvolved.Premultiplied(), Blue.Premultiplied(), amount);

        var rendering = Render(context, source.Bitmap, convolved.Bitmap, amount);

        Assert.All(rendering.Coordinates(), point => Assert.True(WithinRounding(expected, rendering[point.X, point.Y]), $"({point.X}, {point.Y}) {rendering[point.X, point.Y]}"));
    }

    [Fact]
    public void AtTheFullAmountOnlyTheConvolutionIsDrawn()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Blue);
        using var convolved = SourceImage.Solid(context, 8, 8, HalfConvolved);
        using var moved = Translate(context, convolved.Bitmap, 50f, 4f);
        using var movedOutput = moved.Output;
        var expected = Mix(HalfConvolved.Premultiplied(), Bgra.Transparent, 1f);

        var rendering = Render(context, source.Bitmap, movedOutput, 1f);

        Assert.True(WithinRounding(expected, rendering[53, 7]), $"{rendering[53, 7]}");
        Assert.Equal(Bgra.Transparent, rendering[10, 10]);
        Assert.Equal(Bgra.Transparent, rendering[45, 10]);
    }

    [Fact]
    public void AConvolvedColorAboveItsAlphaIsCappedAtTheAlpha()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Bgra.Transparent);
        using var convolved = RawBitmap(context, Width, Height, new Bgra(200, 160, 120, 100));

        var rendering = Render(context, source.Bitmap, convolved, 1f);

        Assert.All(rendering.Coordinates(), point => Assert.True(WithinRounding(new Bgra(100, 100, 100, 100), rendering[point.X, point.Y]), $"({point.X}, {point.Y}) {rendering[point.X, point.Y]}"));
    }
}
