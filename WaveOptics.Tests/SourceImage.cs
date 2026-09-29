using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace WaveOptics.Tests;

internal sealed class SourceImage : IDisposable
{
    public const float Dpi = 96f;
    public const int BytesPerPixel = 4;

    public static readonly PixelFormat PixelFormat = new(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);

    readonly Func<int, int, Bgra> pixelOf;

    public int Width { get; }

    public int Height { get; }

    public ID2D1Bitmap1 Bitmap { get; }

    public SourceImage(IGraphicsDevicesAndContext devices, int width, int height, Func<int, int, Bgra> pixelOf)
    {
        this.pixelOf = pixelOf;
        Width = width;
        Height = height;

        var stride = width * BytesPerPixel;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (blue, green, red, alpha) = pixelOf(x, y).Premultiplied();
                var offset = y * stride + x * BytesPerPixel;
                pixels[offset] = blue;
                pixels[offset + 1] = green;
                pixels[offset + 2] = red;
                pixels[offset + 3] = alpha;
            }
        }

        Bitmap = devices.DeviceContext.CreateBitmap(
            new SizeI(width, height), nint.Zero, stride,
            new BitmapProperties1(PixelFormat, Dpi, Dpi, BitmapOptions.None));
        Bitmap.CopyFromMemory(pixels, stride);
    }

    public static SourceImage Solid(IGraphicsDevicesAndContext devices, int width, int height, Bgra color)
        => new(devices, width, height, (_, _) => color);

    public Bgra this[int x, int y] => pixelOf(x, y).Premultiplied();

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public void Dispose() => Bitmap.Dispose();
}
