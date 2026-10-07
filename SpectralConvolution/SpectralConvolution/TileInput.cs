namespace SpectralConvolution;

internal static class TileInput
{
    const int Channels = 4;

    public static (double RedGreen, double BlueAlpha) Norms(
        ReadOnlySpan<byte> bgra,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        in TilePlan plan,
        int tile)
    {
        if (bgra.Length < (long)sourceWidth * sourceHeight * Channels)
            throw new ArgumentException(null, nameof(bgra));

        var left = plan.OriginX(tile) - plan.Radius;
        var top = plan.OriginY(tile) - plan.Radius;
        var redGreen = 0L;
        var blueAlpha = 0L;
        var lastY = Math.Min(top + plan.Size, sourceY + sourceHeight);
        var lastX = Math.Min(left + plan.Size, sourceX + sourceWidth);
        for (var y = Math.Max(top, sourceY); y < lastY; y++)
        {
            for (var x = Math.Max(left, sourceX); x < lastX; x++)
            {
                var offset = ((y - sourceY) * sourceWidth + x - sourceX) * Channels;
                long blue = bgra[offset];
                long green = bgra[offset + 1];
                long red = bgra[offset + 2];
                long alpha = bgra[offset + 3];
                redGreen += red * red + green * green;
                blueAlpha += blue * blue + alpha * alpha;
            }
        }

        return (Math.Sqrt(redGreen) / ByteColor.ScaleDouble, Math.Sqrt(blueAlpha) / ByteColor.ScaleDouble);
    }
}
