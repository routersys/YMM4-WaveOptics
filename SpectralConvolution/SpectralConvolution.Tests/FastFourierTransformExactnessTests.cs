namespace SpectralConvolution.Tests;

public sealed class FastFourierTransformExactnessTests
{
    static void Reference(double[] real, double[] imaginary, bool inverse)
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

        var sign = inverse ? 1d : -1d;
        for (var length = 2; length <= count; length <<= 1)
        {
            var half = length >> 1;
            for (var offset = 0; offset < count; offset += length)
            {
                for (var index = 0; index < half; index++)
                {
                    var (sin, cos) = Math.SinCos(sign * 2d * Math.PI * index / length);
                    var even = offset + index;
                    var odd = even + half;
                    var productReal = real[odd] * cos - imaginary[odd] * sin;
                    var productImaginary = real[odd] * sin + imaginary[odd] * cos;
                    var evenReal = real[even];
                    var evenImaginary = imaginary[even];
                    real[even] = evenReal + productReal;
                    imaginary[even] = evenImaginary + productImaginary;
                    real[odd] = evenReal - productReal;
                    imaginary[odd] = evenImaginary - productImaginary;
                }
            }
        }

        if (!inverse)
            return;

        var scale = 1d / count;
        for (var index = 0; index < count; index++)
        {
            real[index] *= scale;
            imaginary[index] *= scale;
        }
    }

    static (double[] Real, double[] Imaginary) Input(int count, int trial, Random random)
    {
        var real = new double[count];
        var imaginary = new double[count];
        for (var index = 0; index < count; index++)
        {
            real[index] = trial % 3 == 0 && index % 5 != 0 ? 0d : random.NextDouble() * 2 - 1;
            imaginary[index] = trial % 4 == 0 && index % 7 != 0 ? 0d : (random.NextDouble() * 2 - 1) * (trial % 5 == 0 ? 1e-9 : 1d);
        }

        return (real, imaginary);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void TheTransformPerformsTheSameArithmeticAsTheRadixTwoReference(int count)
    {
        var random = new Random(count);
        for (var trial = 0; trial < 40; trial++)
        {
            foreach (var inverse in new[] { false, true })
            {
                var (expectedReal, expectedImaginary) = Input(count, trial, random);
                var actualReal = (double[])expectedReal.Clone();
                var actualImaginary = (double[])expectedImaginary.Clone();

                Reference(expectedReal, expectedImaginary, inverse);
                if (inverse)
                    FastFourierTransform.Inverse(actualReal, actualImaginary);
                else
                    FastFourierTransform.Forward(actualReal, actualImaginary);

                Assert.True(expectedReal.AsSpan().SequenceEqual(actualReal), $"real {count} {trial} {inverse}");
                Assert.True(expectedImaginary.AsSpan().SequenceEqual(actualImaginary), $"imaginary {count} {trial} {inverse}");
            }
        }
    }

    [Fact]
    public void ATransformOfALongerArrayThanTheTransformedPartLeavesTheRestUntouched()
    {
        var real = new double[600];
        var imaginary = new double[600];
        real.AsSpan().Fill(3d);
        imaginary.AsSpan().Fill(5d);

        FastFourierTransform.Forward(real.AsSpan(40, 256), imaginary.AsSpan(40, 256));

        Assert.All(real.AsSpan(0, 40).ToArray(), value => Assert.Equal(3d, value));
        Assert.All(real.AsSpan(296).ToArray(), value => Assert.Equal(3d, value));
        Assert.All(imaginary.AsSpan(0, 40).ToArray(), value => Assert.Equal(5d, value));
        Assert.All(imaginary.AsSpan(296).ToArray(), value => Assert.Equal(5d, value));
    }
}
