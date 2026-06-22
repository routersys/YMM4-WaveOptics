using System.Numerics;
using WaveOptics.Fourier;

namespace WaveOptics.Tests.Fourier;

public sealed class FastFourierTransformTests
{
    [Fact]
    public void ForwardOfImpulseIsConstant()
    {
        var values = new Complex[16];
        values[0] = Complex.One;

        FastFourierTransform.Forward(values);

        foreach (var value in values)
            Assert.True(Complex.Abs(value - Complex.One) < 1e-12);
    }

    [Fact]
    public void OneDimensionalRoundTripRestoresInput()
    {
        var random = new Random(12345);
        var values = Enumerable.Range(0, 256)
            .Select(_ => new Complex(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1))
            .ToArray();
        var expected = (Complex[])values.Clone();

        FastFourierTransform.Forward(values);
        FastFourierTransform.Inverse(values);

        AssertMaximumError(expected, values, 1e-11);
    }

    [Fact]
    public void TwoDimensionalRoundTripRestoresInput()
    {
        var random = new Random(67890);
        var values = Enumerable.Range(0, 64 * 32)
            .Select(_ => new Complex(random.NextDouble(), random.NextDouble()))
            .ToArray();
        var expected = (Complex[])values.Clone();

        FastFourierTransform.Forward2D(values, 64, 32);
        FastFourierTransform.Inverse2D(values, 64, 32);

        AssertMaximumError(expected, values, 1e-10);
    }

    static void AssertMaximumError(Complex[] expected, Complex[] actual, double tolerance)
    {
        var maximum = 0d;
        for (var index = 0; index < expected.Length; index++)
            maximum = Math.Max(maximum, Complex.Abs(expected[index] - actual[index]));
        Assert.True(maximum <= tolerance, $"{maximum:R}");
    }
}
