using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace SpectralConvolution;

internal static class CpuPixels
{
    const int Channels = 4;

    static readonly float[] Units = BuildUnits();

    public static byte ToUnorm(float value)
    {
        if (float.IsNaN(value))
            return 0;
        return (byte)(Math.Clamp(value, 0f, 1f) * ByteColor.Scale + ByteColor.Rounding);
    }

    public static bool LoadRow(Span<float> row, ReadOnlySpan<byte> pixels, ReadOnlySpan<int> order, int sourceX, int left, int first, int last, in LightOptions light)
    {
        row.Clear();
        var lit = false;
        var units = Units;
        for (var x = first; x < last; x++)
        {
            var offset = (left + x - sourceX) * Channels;
            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            var alpha = pixels[offset + 3];
            if ((blue | green | red | alpha) == 0)
                continue;

            var target = order[x] * Channels;
            if (light.Linear)
            {
                var (linearRed, linearGreen, linearBlue, linearAlpha) = LightTransform.FromBytes(red, green, blue, alpha, in light);
                row[target] = linearRed;
                row[target + 1] = linearGreen;
                row[target + 2] = linearBlue;
                row[target + 3] = linearAlpha;
            }
            else
            {
                row[target] = units[red];
                row[target + 1] = units[green];
                row[target + 2] = units[blue];
                row[target + 3] = units[alpha];
            }
            lit = true;
        }

        return lit;
    }

    public static void Emit(ReadOnlySpan<float> pixels, float scale, float gain, Span<byte> bytes, Span<float> store)
    {
        var count = pixels.Length / Channels;
        if (bytes.Length < count * Channels || (!store.IsEmpty && store.Length < count * Channels))
            throw new ArgumentException(null, nameof(bytes));

        ref var from = ref MemoryMarshal.GetReference(pixels);
        ref var to = ref MemoryMarshal.GetReference(bytes);
        ref var kept = ref MemoryMarshal.GetReference(store);
        var scaleVector = Vector128.Create(scale);
        var gainVector = Vector128.Create(gain);
        var keep = !store.IsEmpty;
        for (var pixel = 0; pixel < count; pixel++)
        {
            var value = Vector128.LoadUnsafe(ref from, (nuint)(pixel * Channels)) * scaleVector;
            if (keep)
                value.StoreUnsafe(ref kept, (nuint)(pixel * Channels));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref to, pixel * Channels), PackBgra(value * gainVector));
        }
    }

    public static void Emit(ReadOnlySpan<float> pixels, float scale, float gain, in LightOptions light, int x, int y, Span<byte> bytes, Span<float> store)
    {
        var count = pixels.Length / Channels;
        if (bytes.Length < count * Channels || (!store.IsEmpty && store.Length < count * Channels))
            throw new ArgumentException(null, nameof(bytes));

        ref var from = ref MemoryMarshal.GetReference(pixels);
        ref var to = ref MemoryMarshal.GetReference(bytes);
        ref var kept = ref MemoryMarshal.GetReference(store);
        var scaleVector = Vector128.Create(scale);
        var keep = !store.IsEmpty;
        for (var pixel = 0; pixel < count; pixel++)
        {
            var value = Vector128.LoadUnsafe(ref from, (nuint)(pixel * Channels)) * scaleVector;
            if (keep)
                value.StoreUnsafe(ref kept, (nuint)(pixel * Channels));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref to, pixel * Channels), LightTransform.ToPacked(value, gain, in light, x + pixel, y));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint PackBgra(Vector128<float> rgba)
    {
        var ordered = Vector128.Shuffle(rgba, Vector128.Create(2, 1, 0, 3));
        ordered = Vector128.ConditionalSelect(Vector128.Equals(ordered, ordered), ordered, Vector128<float>.Zero);
        var clamped = Vector128.Min(Vector128.Max(ordered, Vector128<float>.Zero), Vector128<float>.One);
        var levels = Vector128.ConvertToInt32(clamped * Vector128.Create(ByteColor.Scale) + Vector128.Create(ByteColor.Rounding)).AsUInt32();
        var words = Vector128.Narrow(levels, levels);
        return Vector128.Narrow(words, words).AsUInt32().ToScalar();
    }

    static float[] BuildUnits()
    {
        var units = new float[ByteColor.Levels];
        for (var level = 0; level < units.Length; level++)
            units[level] = level / ByteColor.Scale;
        return units;
    }
}
