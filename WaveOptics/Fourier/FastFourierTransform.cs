using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WaveOptics.Fourier;

internal static class FastFourierTransform
{
    static readonly ConcurrentDictionary<long, Twiddles> TwiddleCache = new();

    public static void Forward(Span<double> real, Span<double> imaginary) => Transform(real, imaginary, false);

    public static void Inverse(Span<double> real, Span<double> imaginary) => Transform(real, imaginary, true);

    public static void Forward2D(double[] real, double[] imaginary, int width, int height) => Transform2D(real, imaginary, width, height, false);

    public static void Inverse2D(double[] real, double[] imaginary, int width, int height) => Transform2D(real, imaginary, width, height, true);

    static void Transform(Span<double> real, Span<double> imaginary, bool inverse)
    {
        var count = real.Length;
        if (count == 0 || imaginary.Length != count || !BitOperations.IsPow2((uint)count))
            throw new ArgumentException(nameof(real));

        var twiddles = GetTwiddles(count, inverse);
        Reorder(real, imaginary);
        Butterflies(real, imaginary, twiddles);
        if (inverse)
            Scale(real, imaginary, 1d / count);
    }

    static void Transform2D(double[] real, double[] imaginary, int width, int height, bool inverse)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(imaginary);
        var area = width * height;
        if (real.Length < area || imaginary.Length < area)
            throw new ArgumentException(nameof(real));
        if (!BitOperations.IsPow2((uint)width) || !BitOperations.IsPow2((uint)height))
            throw new ArgumentException(nameof(width));

        for (var y = 0; y < height; y++)
        {
            var offset = y * width;
            Transform(real.AsSpan(offset, width), imaginary.AsSpan(offset, width), inverse);
        }

        var columnReal = ArrayPool<double>.Shared.Rent(height);
        var columnImaginary = ArrayPool<double>.Shared.Rent(height);
        try
        {
            var rowReal = real.AsSpan(0, area);
            var rowImaginary = imaginary.AsSpan(0, area);
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    var index = y * width + x;
                    columnReal[y] = rowReal[index];
                    columnImaginary[y] = rowImaginary[index];
                }
                Transform(columnReal.AsSpan(0, height), columnImaginary.AsSpan(0, height), inverse);
                for (var y = 0; y < height; y++)
                {
                    var index = y * width + x;
                    rowReal[index] = columnReal[y];
                    rowImaginary[index] = columnImaginary[y];
                }
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(columnReal);
            ArrayPool<double>.Shared.Return(columnImaginary);
        }
    }

    static void Reorder(Span<double> real, Span<double> imaginary)
    {
        var count = real.Length;
        for (int source = 1, target = 0; source < count; source++)
        {
            var bit = count >> 1;
            for (; (target & bit) != 0; bit >>= 1)
                target ^= bit;
            target ^= bit;
            if (source < target)
            {
                (real[source], real[target]) = (real[target], real[source]);
                (imaginary[source], imaginary[target]) = (imaginary[target], imaginary[source]);
            }
        }
    }

    static void Butterflies(Span<double> real, Span<double> imaginary, Twiddles twiddles)
    {
        var count = real.Length;
        ref var realBase = ref MemoryMarshal.GetReference(real);
        ref var imaginaryBase = ref MemoryMarshal.GetReference(imaginary);
        var width = Vector<double>.Count;

        for (int length = 2, stage = 0; length <= count; length <<= 1, stage++)
        {
            var half = length >> 1;
            var cos = twiddles.Cos[stage];
            var sin = twiddles.Sin[stage];
            ref var cosBase = ref MemoryMarshal.GetArrayDataReference(cos);
            ref var sinBase = ref MemoryMarshal.GetArrayDataReference(sin);
            var vectorized = Vector.IsHardwareAccelerated && half >= width;

            for (var offset = 0; offset < count; offset += length)
            {
                var index = 0;
                if (vectorized)
                {
                    for (; index + width <= half; index += width)
                    {
                        var even = offset + index;
                        var odd = even + half;
                        var evenReal = Vector.LoadUnsafe(ref Unsafe.Add(ref realBase, even));
                        var evenImaginary = Vector.LoadUnsafe(ref Unsafe.Add(ref imaginaryBase, even));
                        var oddReal = Vector.LoadUnsafe(ref Unsafe.Add(ref realBase, odd));
                        var oddImaginary = Vector.LoadUnsafe(ref Unsafe.Add(ref imaginaryBase, odd));
                        var weightReal = Vector.LoadUnsafe(ref Unsafe.Add(ref cosBase, index));
                        var weightImaginary = Vector.LoadUnsafe(ref Unsafe.Add(ref sinBase, index));
                        var productReal = oddReal * weightReal - oddImaginary * weightImaginary;
                        var productImaginary = oddReal * weightImaginary + oddImaginary * weightReal;
                        (evenReal + productReal).StoreUnsafe(ref Unsafe.Add(ref realBase, even));
                        (evenImaginary + productImaginary).StoreUnsafe(ref Unsafe.Add(ref imaginaryBase, even));
                        (evenReal - productReal).StoreUnsafe(ref Unsafe.Add(ref realBase, odd));
                        (evenImaginary - productImaginary).StoreUnsafe(ref Unsafe.Add(ref imaginaryBase, odd));
                    }
                }
                for (; index < half; index++)
                {
                    var even = offset + index;
                    var odd = even + half;
                    var weightReal = Unsafe.Add(ref cosBase, index);
                    var weightImaginary = Unsafe.Add(ref sinBase, index);
                    var oddReal = Unsafe.Add(ref realBase, odd);
                    var oddImaginary = Unsafe.Add(ref imaginaryBase, odd);
                    var productReal = oddReal * weightReal - oddImaginary * weightImaginary;
                    var productImaginary = oddReal * weightImaginary + oddImaginary * weightReal;
                    var evenReal = Unsafe.Add(ref realBase, even);
                    var evenImaginary = Unsafe.Add(ref imaginaryBase, even);
                    Unsafe.Add(ref realBase, even) = evenReal + productReal;
                    Unsafe.Add(ref imaginaryBase, even) = evenImaginary + productImaginary;
                    Unsafe.Add(ref realBase, odd) = evenReal - productReal;
                    Unsafe.Add(ref imaginaryBase, odd) = evenImaginary - productImaginary;
                }
            }
        }
    }

    static void Scale(Span<double> real, Span<double> imaginary, double factor)
    {
        var count = real.Length;
        ref var realBase = ref MemoryMarshal.GetReference(real);
        ref var imaginaryBase = ref MemoryMarshal.GetReference(imaginary);
        var index = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var scale = new Vector<double>(factor);
            var width = Vector<double>.Count;
            for (; index + width <= count; index += width)
            {
                (Vector.LoadUnsafe(ref Unsafe.Add(ref realBase, index)) * scale).StoreUnsafe(ref Unsafe.Add(ref realBase, index));
                (Vector.LoadUnsafe(ref Unsafe.Add(ref imaginaryBase, index)) * scale).StoreUnsafe(ref Unsafe.Add(ref imaginaryBase, index));
            }
        }
        for (; index < count; index++)
        {
            Unsafe.Add(ref realBase, index) *= factor;
            Unsafe.Add(ref imaginaryBase, index) *= factor;
        }
    }

    static Twiddles GetTwiddles(int count, bool inverse) =>
        TwiddleCache.GetOrAdd(((long)count << 1) | (inverse ? 1L : 0L), _ => new Twiddles(count, inverse));

    sealed class Twiddles
    {
        public double[][] Cos { get; }
        public double[][] Sin { get; }

        public Twiddles(int count, bool inverse)
        {
            var stages = BitOperations.TrailingZeroCount(count);
            Cos = new double[stages][];
            Sin = new double[stages][];
            var sign = inverse ? 1d : -1d;
            for (int length = 2, stage = 0; stage < stages; length <<= 1, stage++)
            {
                var half = length >> 1;
                var cos = new double[half];
                var sin = new double[half];
                for (var index = 0; index < half; index++)
                {
                    var (sinValue, cosValue) = Math.SinCos(sign * 2d * Math.PI * index / length);
                    cos[index] = cosValue;
                    sin[index] = sinValue;
                }
                Cos[stage] = cos;
                Sin[stage] = sin;
            }
        }
    }
}
