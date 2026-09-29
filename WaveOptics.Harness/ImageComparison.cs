namespace WaveOptics.Harness;

internal readonly record struct ImageDifference(int Differing, int MaxDelta, int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Differing == 0;

    public override string ToString()
        => IsEmpty ? "一致" : $"differing={Differing} maxDelta={MaxDelta} box=({Left},{Top})-({Right},{Bottom})";
}

internal static class ImageComparison
{
    public static ImageDifference Of(byte[] first, byte[] second, int width, int height)
    {
        if (first.Length != second.Length || first.Length != width * height * HarnessImage.BytesPerPixel)
            throw new ArgumentException("画像の寸法が一致しません。");

        var differing = 0;
        var maxDelta = 0;
        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * HarnessImage.BytesPerPixel;
                var delta = 0;
                for (var channel = 0; channel < HarnessImage.BytesPerPixel; channel++)
                    delta = Math.Max(delta, Math.Abs(first[offset + channel] - second[offset + channel]));
                if (delta == 0)
                    continue;

                differing++;
                maxDelta = Math.Max(maxDelta, delta);
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return differing == 0 ? default : new ImageDifference(differing, maxDelta, left, top, right, bottom);
    }
}
