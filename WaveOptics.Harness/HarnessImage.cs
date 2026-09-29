using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WaveOptics.Harness;

internal sealed record HarnessImage(int Width, int Height, byte[] Pixels, string Identity, string? Key)
{
    public const int BytesPerPixel = 4;
    const int KeyLength = 16;

    public static HarnessImage Synthetic(int width, int height)
    {
        var pixels = SyntheticImage.Create(width, height);
        return new HarnessImage(width, height, pixels, $"synthetic {width}x{height} {Convert.ToHexString(SHA256.HashData(pixels))[..KeyLength]}", null);
    }

    public static HarnessImage Load(string path)
    {
        if (!File.Exists(path))
            throw new HarnessException($"入力の画像がありません。{path}");

        var bytes = File.ReadAllBytes(path);
        var (width, height, pixels) = Decode(bytes, path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return new HarnessImage(width, height, pixels, $"{hash} {width}x{height}", hash[..KeyLength]);
    }

    public static (int Width, int Height, byte[] Pixels) LoadPremultiplied(string path) => Decode(File.ReadAllBytes(path), path);

    public static void Save(string path, byte[] pixels, int width, int height)
    {
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * BytesPerPixel);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    static (int Width, int Height, byte[] Pixels) Decode(byte[] bytes, string path)
    {
        using var stream = new MemoryStream(bytes);
        BitmapDecoder decoder;
        try
        {
            decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException)
        {
            throw new HarnessException($"画像として読めません。{path}");
        }

        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0d);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * BytesPerPixel];
        converted.CopyPixels(pixels, converted.PixelWidth * BytesPerPixel, 0);
        return (converted.PixelWidth, converted.PixelHeight, pixels);
    }
}
