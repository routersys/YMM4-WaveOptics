namespace WaveOptics.Rendering;

internal readonly record struct WaveOpticsSourceHash(int LitCount, int MinimumX, int MinimumY, int MaximumX, int MaximumY, int Sum, int Mix)
{
    public static WaveOpticsSourceHash Empty { get; } = new(0, int.MaxValue, int.MaxValue, int.MinValue, int.MinValue, 0, 0);

    public static WaveOpticsSourceHash FromScratch(ReadOnlySpan<int> scratch)
        => new(
            scratch[WaveOpticsSettings.ScratchLitCount],
            scratch[WaveOpticsSettings.ScratchBoundsMinX],
            scratch[WaveOpticsSettings.ScratchBoundsMinY],
            scratch[WaveOpticsSettings.ScratchBoundsMaxX],
            scratch[WaveOpticsSettings.ScratchBoundsMaxY],
            scratch[WaveOpticsSettings.ScratchHashSum],
            scratch[WaveOpticsSettings.ScratchHashMix]);

    public static WaveOpticsSourceHash Compute(ReadOnlySpan<byte> bgra, int sourceX, int sourceY, int sourceWidth, int sourceHeight)
    {
        if (bgra.Length < (long)sourceWidth * sourceHeight * 4)
            throw new ArgumentException(null, nameof(bgra));

        var count = 0;
        var sum = 0;
        var mix = 0;
        var minimumX = int.MaxValue;
        var minimumY = int.MaxValue;
        var maximumX = int.MinValue;
        var maximumY = int.MinValue;
        for (var y = 0; y < sourceHeight; y++)
        {
            var row = bgra.Slice(y * sourceWidth * 4, sourceWidth * 4);
            for (var x = 0; x < sourceWidth; x++)
            {
                var quantized = (uint)row[x * 4 + 2] << 24 | (uint)row[x * 4 + 1] << 16 | (uint)row[x * 4] << 8 | row[x * 4 + 3];
                if (quantized == 0u)
                    continue;

                var mixed = unchecked((uint)(y * sourceWidth + x) * 0x9E3779B9u ^ quantized * 0x85EBCA6Bu);
                mixed ^= mixed >> 16;
                mixed = unchecked(mixed * 0x85EBCA6Bu);
                mixed ^= mixed >> 13;
                sum = unchecked(sum + (int)mixed);
                mix ^= unchecked((int)(mixed * 0xC2B2AE35u));
                count++;
                minimumX = Math.Min(minimumX, sourceX + x);
                maximumX = Math.Max(maximumX, sourceX + x);
                minimumY = Math.Min(minimumY, sourceY + y);
                maximumY = Math.Max(maximumY, sourceY + y);
            }
        }

        return new WaveOpticsSourceHash(count, minimumX, minimumY, maximumX, maximumY, sum, mix);
    }

    public bool TryGetVisibleBounds(int canvasWidth, int canvasHeight, int radius, out WaveOpticsPipeline.PixelRect rect)
    {
        rect = default;
        if (LitCount <= 0 || MinimumX > MaximumX)
            return false;

        var left = Math.Clamp((MinimumX - radius) & ~3, 0, canvasWidth);
        var top = Math.Clamp((MinimumY - radius) & ~3, 0, canvasHeight);
        var right = Math.Clamp(MaximumX + 1 + radius, 0, canvasWidth);
        var bottom = Math.Clamp(MaximumY + 1 + radius, 0, canvasHeight);
        var width = Math.Min((right - left + 3) & ~3, canvasWidth - left);
        var height = Math.Min((bottom - top + 3) & ~3, canvasHeight - top);
        if (width <= 0 || height <= 0)
            return false;

        rect = new WaveOpticsPipeline.PixelRect(left, top, width, height);
        return true;
    }
}
