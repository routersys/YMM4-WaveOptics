using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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

        return Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian
            ? ComputeVector(bgra, sourceX, sourceY, sourceWidth, sourceHeight)
            : ComputeScalar(bgra, sourceX, sourceY, sourceWidth, sourceHeight);
    }

    internal static WaveOpticsSourceHash ComputeScalar(ReadOnlySpan<byte> bgra, int sourceX, int sourceY, int sourceWidth, int sourceHeight)
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

                var mixed = Mixed(y * sourceWidth + x, quantized);
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

    // The same sums as the scalar form: eight pixels at a time, with the unlit ones masked out. Both sums
    // do not depend on the order of the pixels, so the result is the same, bit for bit.
    private static WaveOpticsSourceHash ComputeVector(ReadOnlySpan<byte> bgra, int sourceX, int sourceY, int sourceWidth, int sourceHeight)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(bgra[..(sourceWidth * sourceHeight * 4)]);
        ref var origin = ref MemoryMarshal.GetReference(pixels);
        var lanes = Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);
        var golden = Vector256.Create(0x9E3779B9u);
        var first = Vector256.Create(0x85EBCA6Bu);
        var second = Vector256.Create(0xC2B2AE35u);
        var sumVector = Vector256<uint>.Zero;
        var mixVector = Vector256<uint>.Zero;
        var countVector = Vector256<int>.Zero;
        var count = 0;
        var sum = 0u;
        var mix = 0u;
        var minimumX = int.MaxValue;
        var minimumY = int.MaxValue;
        var maximumX = int.MinValue;
        var maximumY = int.MinValue;
        for (var y = 0; y < sourceHeight; y++)
        {
            var rowStart = y * sourceWidth;
            var rowMinimum = int.MaxValue;
            var rowMaximum = int.MinValue;
            var x = 0;
            for (; x + 8 <= sourceWidth; x += 8)
            {
                var pixel = Vector256.LoadUnsafe(ref origin, (nuint)(rowStart + x));
                var lit = ~Vector256.Equals(pixel, Vector256<uint>.Zero);
                var bits = lit.ExtractMostSignificantBits();
                if (bits == 0u)
                    continue;

                var quantized = (pixel << 8) | (pixel >> 24);
                var mixed = (Vector256.Create((uint)(rowStart + x)) + lanes) * golden ^ quantized * first;
                mixed ^= mixed >> 16;
                mixed *= first;
                mixed ^= mixed >> 13;
                mixed &= lit;
                sumVector += mixed;
                mixVector ^= mixed * second;
                countVector -= lit.AsInt32();
                rowMinimum = Math.Min(rowMinimum, x + BitOperations.TrailingZeroCount(bits));
                rowMaximum = Math.Max(rowMaximum, x + 31 - BitOperations.LeadingZeroCount(bits));
            }

            for (; x < sourceWidth; x++)
            {
                var quantized = BitOperations.RotateLeft(Unsafe.Add(ref origin, rowStart + x), 8);
                if (quantized == 0u)
                    continue;

                var mixed = Mixed(rowStart + x, quantized);
                sum = unchecked(sum + mixed);
                mix ^= unchecked(mixed * 0xC2B2AE35u);
                count++;
                rowMinimum = Math.Min(rowMinimum, x);
                rowMaximum = Math.Max(rowMaximum, x);
            }

            if (rowMaximum < rowMinimum)
                continue;

            minimumX = Math.Min(minimumX, sourceX + rowMinimum);
            maximumX = Math.Max(maximumX, sourceX + rowMaximum);
            minimumY = Math.Min(minimumY, sourceY + y);
            maximumY = Math.Max(maximumY, sourceY + y);
        }

        for (var lane = 0; lane < Vector256<uint>.Count; lane++)
            mix ^= mixVector.GetElement(lane);

        return new WaveOpticsSourceHash(
            count + Vector256.Sum(countVector),
            minimumX,
            minimumY,
            maximumX,
            maximumY,
            unchecked((int)(sum + Vector256.Sum(sumVector))),
            unchecked((int)mix));
    }

    private static uint Mixed(int index, uint quantized)
    {
        var mixed = unchecked((uint)index * 0x9E3779B9u ^ quantized * 0x85EBCA6Bu);
        mixed ^= mixed >> 16;
        mixed = unchecked(mixed * 0x85EBCA6Bu);
        mixed ^= mixed >> 13;
        return mixed;
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
