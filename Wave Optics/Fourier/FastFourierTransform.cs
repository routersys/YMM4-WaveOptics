using System.Numerics;

namespace WaveOptics.Fourier;

internal static class FastFourierTransform
{
    public static void Forward(Span<Complex> values) => Transform(values, false);

    public static void Inverse(Span<Complex> values) => Transform(values, true);

    public static void Forward2D(Complex[] values, int width, int height) => Transform2D(values, width, height, false);

    public static void Inverse2D(Complex[] values, int width, int height) => Transform2D(values, width, height, true);

    static void Transform(Span<Complex> values, bool inverse)
    {
        var count = values.Length;
        if (count == 0 || !BitOperations.IsPow2((uint)count))
            throw new ArgumentException(nameof(values));

        var target = 0;
        for (var source = 1; source < count; source++)
        {
            var bit = count >> 1;
            while ((target & bit) != 0)
            {
                target ^= bit;
                bit >>= 1;
            }
            target ^= bit;
            if (source < target)
                (values[source], values[target]) = (values[target], values[source]);
        }

        for (var length = 2; length <= count; length <<= 1)
        {
            var angle = (inverse ? 2d : -2d) * Math.PI / length;
            var step = Complex.FromPolarCoordinates(1d, angle);
            var half = length >> 1;
            for (var offset = 0; offset < count; offset += length)
            {
                var factor = Complex.One;
                for (var index = 0; index < half; index++)
                {
                    var even = values[offset + index];
                    var odd = values[offset + index + half] * factor;
                    values[offset + index] = even + odd;
                    values[offset + index + half] = even - odd;
                    factor *= step;
                }
            }
        }

        if (!inverse)
            return;

        var scale = 1d / count;
        for (var index = 0; index < count; index++)
            values[index] *= scale;
    }

    static void Transform2D(Complex[] values, int width, int height, bool inverse)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != width * height)
            throw new ArgumentException(nameof(values));
        if (!BitOperations.IsPow2((uint)width) || !BitOperations.IsPow2((uint)height))
            throw new ArgumentException(nameof(width));

        for (var y = 0; y < height; y++)
        {
            var row = values.AsSpan(y * width, width);
            if (inverse)
                Inverse(row);
            else
                Forward(row);
        }

        var column = new Complex[height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
                column[y] = values[y * width + x];
            if (inverse)
                Inverse(column);
            else
                Forward(column);
            for (var y = 0; y < height; y++)
                values[y * width + x] = column[y];
        }
    }
}
