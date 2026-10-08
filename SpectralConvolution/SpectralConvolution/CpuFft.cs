using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using ComputeWeave;

namespace SpectralConvolution;

internal static class CpuFft
{
    const int Channels = 4;
    const int UIntBits = sizeof(uint) * 8;
    const int StagesPerPass = 2;

    internal static void FillReversal(Span<int> order, int log2)
    {
        var shift = UIntBits - log2;
        for (var index = 0; index < order.Length; index++)
            order[index] = (int)(ReverseBits((uint)index) >> shift);
    }

    internal static void Butterflies(Span<float> line, int log2, ReadOnlySpan<Float2> twiddles, float direction, bool allowWide = true)
    {
        var size = 1 << log2;
        if (line.Length < size * Channels || twiddles.Length < size / 2)
            throw new ArgumentException(null, nameof(line));

        ref var data = ref MemoryMarshal.GetReference(line);
        ref var table = ref MemoryMarshal.GetReference(twiddles);
        var wide = allowWide && Vector256.IsHardwareAccelerated;
        var stage = 0;
        if (wide && log2 >= StagesPerPass)
        {
            FirstTwoStages(ref data, ref table, size, log2, direction);
            for (stage = StagesPerPass; stage + 1 < log2; stage += StagesPerPass)
                StagePair(ref data, ref table, size, log2, stage, direction);
        }

        for (; stage < log2; stage++)
        {
            var half = 1 << stage;
            var shift = log2 - 1 - stage;
            var distance = (nuint)(half * Channels);
            if (wide && half >= 2)
                StageWithPairedTwiddles(ref data, ref table, size, half, shift, direction, distance);
            else
                StageWithSingleTwiddles(ref data, ref table, size, half, shift, direction, distance);
        }
    }

    internal static void ButterfliesScalar(Span<float> line, int log2, ReadOnlySpan<Float2> twiddles, float direction)
    {
        var pairs = 1 << (log2 - 1);
        for (var stage = 0; stage < log2; stage++)
        {
            var half = 1 << stage;
            var shift = log2 - 1 - stage;
            for (var pair = 0; pair < pairs; pair++)
            {
                var position = pair & (half - 1);
                var even = (((pair >> stage) << (stage + 1)) + position) * Channels;
                var odd = even + half * Channels;
                var twiddle = twiddles[position << shift];
                var real = twiddle.X;
                var imaginary = twiddle.Y * direction;
                var odd0 = line[odd];
                var odd1 = line[odd + 1];
                var odd2 = line[odd + 2];
                var odd3 = line[odd + 3];
                var product0 = odd0 * real - odd1 * imaginary;
                var product1 = odd0 * imaginary + odd1 * real;
                var product2 = odd2 * real - odd3 * imaginary;
                var product3 = odd2 * imaginary + odd3 * real;
                var even0 = line[even];
                var even1 = line[even + 1];
                var even2 = line[even + 2];
                var even3 = line[even + 3];
                line[even] = even0 + product0;
                line[even + 1] = even1 + product1;
                line[even + 2] = even2 + product2;
                line[even + 3] = even3 + product3;
                line[odd] = even0 - product0;
                line[odd + 1] = even1 - product1;
                line[odd + 2] = even2 - product2;
                line[odd + 3] = even3 - product3;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<float> Rotate(Vector128<float> value, Vector128<float> real, Vector128<float> imaginary)
        => value * real + Vector128.Shuffle(value, Vector128.Create(1, 0, 3, 2)) * imaginary;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector256<float> Rotate(Vector256<float> value, Vector256<float> real, Vector256<float> imaginary)
        => value * real + Vector256.Shuffle(value, Vector256.Create(1, 0, 3, 2, 5, 4, 7, 6)) * imaginary;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<float> Imaginary(float value) => Vector128.Create(-value, value, -value, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void PairedTwiddles(ref Float2 table, int nearIndex, int farIndex, float direction, out Vector256<float> real, out Vector256<float> imaginary)
    {
        var near = Unsafe.Add(ref table, nearIndex);
        var far = Unsafe.Add(ref table, farIndex);
        var nearImaginary = near.Y * direction;
        var farImaginary = far.Y * direction;
        real = Vector256.Create(near.X, near.X, near.X, near.X, far.X, far.X, far.X, far.X);
        imaginary = Vector256.Create(-nearImaginary, nearImaginary, -nearImaginary, nearImaginary, -farImaginary, farImaginary, -farImaginary, farImaginary);
    }

    static void FirstTwoStages(ref float data, ref Float2 table, int size, int log2, float direction)
    {
        var first = Unsafe.Add(ref table, 0);
        var second = Unsafe.Add(ref table, 1 << (log2 - 2));
        var firstReal = Vector128.Create(first.X);
        var firstImaginary = Imaginary(first.Y * direction);
        var secondReal = Vector128.Create(second.X);
        var secondImaginary = Imaginary(second.Y * direction);
        for (var block = 0; block < size; block += 4)
        {
            var start = (nuint)(block * Channels);
            var value0 = Vector128.LoadUnsafe(ref data, start);
            var value1 = Vector128.LoadUnsafe(ref data, start + Channels);
            var value2 = Vector128.LoadUnsafe(ref data, start + Channels * 2);
            var value3 = Vector128.LoadUnsafe(ref data, start + Channels * 3);
            var product1 = Rotate(value1, firstReal, firstImaginary);
            var product3 = Rotate(value3, firstReal, firstImaginary);
            var sum0 = value0 + product1;
            var sum1 = value0 - product1;
            var sum2 = value2 + product3;
            var sum3 = value2 - product3;
            var evenProduct = Rotate(sum2, firstReal, firstImaginary);
            var oddProduct = Rotate(sum3, secondReal, secondImaginary);
            (sum0 + evenProduct).StoreUnsafe(ref data, start);
            (sum1 + oddProduct).StoreUnsafe(ref data, start + Channels);
            (sum0 - evenProduct).StoreUnsafe(ref data, start + Channels * 2);
            (sum1 - oddProduct).StoreUnsafe(ref data, start + Channels * 3);
        }
    }

    static void StagePair(ref float data, ref Float2 table, int size, int log2, int stage, float direction)
    {
        var half = 1 << stage;
        var firstShift = log2 - 1 - stage;
        var secondShift = log2 - 2 - stage;
        var distance = (nuint)(half * Channels);
        for (var position = 0; position < half; position += 2)
        {
            PairedTwiddles(ref table, position << firstShift, (position + 1) << firstShift, direction, out var firstReal, out var firstImaginary);
            PairedTwiddles(ref table, position << secondShift, (position + 1) << secondShift, direction, out var evenReal, out var evenImaginary);
            PairedTwiddles(ref table, (position + half) << secondShift, (position + half + 1) << secondShift, direction, out var oddReal, out var oddImaginary);
            for (var block = 0; block < size; block += half * 4)
            {
                var start = (nuint)((block + position) * Channels);
                var value0 = Vector256.LoadUnsafe(ref data, start);
                var value1 = Vector256.LoadUnsafe(ref data, start + distance);
                var value2 = Vector256.LoadUnsafe(ref data, start + distance * 2);
                var value3 = Vector256.LoadUnsafe(ref data, start + distance * 3);
                var product1 = Rotate(value1, firstReal, firstImaginary);
                var product3 = Rotate(value3, firstReal, firstImaginary);
                var sum0 = value0 + product1;
                var sum1 = value0 - product1;
                var sum2 = value2 + product3;
                var sum3 = value2 - product3;
                var evenProduct = Rotate(sum2, evenReal, evenImaginary);
                var oddProduct = Rotate(sum3, oddReal, oddImaginary);
                (sum0 + evenProduct).StoreUnsafe(ref data, start);
                (sum1 + oddProduct).StoreUnsafe(ref data, start + distance);
                (sum0 - evenProduct).StoreUnsafe(ref data, start + distance * 2);
                (sum1 - oddProduct).StoreUnsafe(ref data, start + distance * 3);
            }
        }
    }

    static void StageWithPairedTwiddles(ref float data, ref Float2 table, int size, int half, int shift, float direction, nuint distance)
    {
        for (var position = 0; position < half; position += 2)
        {
            PairedTwiddles(ref table, position << shift, (position + 1) << shift, direction, out var real, out var imaginary);
            for (var block = 0; block < size; block += half * 2)
            {
                var even = (nuint)((block + position) * Channels);
                var evenValue = Vector256.LoadUnsafe(ref data, even);
                var oddValue = Vector256.LoadUnsafe(ref data, even + distance);
                var product = Rotate(oddValue, real, imaginary);
                (evenValue + product).StoreUnsafe(ref data, even);
                (evenValue - product).StoreUnsafe(ref data, even + distance);
            }
        }
    }

    static void StageWithSingleTwiddles(ref float data, ref Float2 table, int size, int half, int shift, float direction, nuint distance)
    {
        for (var position = 0; position < half; position++)
        {
            var twiddle = Unsafe.Add(ref table, position << shift);
            var real = Vector128.Create(twiddle.X);
            var imaginary = Imaginary(twiddle.Y * direction);
            for (var block = 0; block < size; block += half * 2)
            {
                var even = (nuint)((block + position) * Channels);
                var evenValue = Vector128.LoadUnsafe(ref data, even);
                var oddValue = Vector128.LoadUnsafe(ref data, even + distance);
                var product = Rotate(oddValue, real, imaginary);
                (evenValue + product).StoreUnsafe(ref data, even);
                (evenValue - product).StoreUnsafe(ref data, even + distance);
            }
        }
    }

    static uint ReverseBits(uint value)
    {
        value = ((value >> 1) & 0x55555555u) | ((value & 0x55555555u) << 1);
        value = ((value >> 2) & 0x33333333u) | ((value & 0x33333333u) << 2);
        value = ((value >> 4) & 0x0F0F0F0Fu) | ((value & 0x0F0F0F0Fu) << 4);
        value = ((value >> 8) & 0x00FF00FFu) | ((value & 0x00FF00FFu) << 8);
        return (value >> 16) | (value << 16);
    }
}
