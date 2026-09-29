using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace WaveOptics.Tests;

internal sealed class Rendering
{
    readonly byte[] pixels;

    public int Left { get; }

    public int Top { get; }

    public int Width { get; }

    public int Height { get; }

    public int Right => Left + Width;

    public int Bottom => Top + Height;

    Rendering(int left, int top, int width, int height, byte[] pixels)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        this.pixels = pixels;
    }

    public static Rendering Capture(IGraphicsDevicesAndContext devices, ID2D1Image image)
    {
        var context = devices.DeviceContext;
        var bounds = context.GetImageLocalBounds(image);
        var left = (int)Math.Floor(bounds.Left);
        var top = (int)Math.Floor(bounds.Top);
        var width = (int)Math.Ceiling(bounds.Right) - left;
        var height = (int)Math.Ceiling(bounds.Bottom) - top;
        var stride = width * SourceImage.BytesPerPixel;
        var size = new SizeI(width, height);

        using var target = context.CreateBitmap(size, nint.Zero, stride,
            new BitmapProperties1(SourceImage.PixelFormat, SourceImage.Dpi, SourceImage.Dpi, BitmapOptions.Target));
        using var readback = context.CreateBitmap(size, nint.Zero, stride,
            new BitmapProperties1(SourceImage.PixelFormat, SourceImage.Dpi, SourceImage.Dpi, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));

        context.Target = target;
        context.BeginDraw();
        context.Clear(new Color4(0f, 0f, 0f, 0f));
        context.DrawImage(image, new Vector2(-left, -top), null, InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
        context.EndDraw();
        context.Target = null;

        readback.CopyFromBitmap(target);
        var pixels = new byte[stride * height];
        var mapped = readback.Map(MapOptions.Read);
        try
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(mapped.Bits + y * (int)mapped.Pitch, pixels, y * stride, stride);
        }
        finally
        {
            readback.Unmap();
        }

        return new Rendering(left, top, width, height, pixels);
    }

    public Bgra this[int x, int y]
    {
        get
        {
            var offset = ((y - Top) * Width + (x - Left)) * SourceImage.BytesPerPixel;
            return new Bgra(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
        }
    }

    public bool Contains(int x, int y) => x >= Left && y >= Top && x < Right && y < Bottom;

    public bool SamePixelsAs(Rendering other)
        => Left == other.Left && Top == other.Top && Width == other.Width && Height == other.Height && pixels.AsSpan().SequenceEqual(other.pixels);

    public IEnumerable<(int X, int Y)> Coordinates()
    {
        for (var y = Top; y < Bottom; y++)
        {
            for (var x = Left; x < Right; x++)
                yield return (x, y);
        }
    }
}
