using WaveOptics.Fourier;

namespace WaveOptics.Tests.Fourier;

public sealed class FastFourierTransformTests
{
    [Fact]
    public void ForwardOfImpulseIsConstant()
    {
        var real = new double[16];
        var imaginary = new double[16];
        real[0] = 1;

        FastFourierTransform.Forward(real, imaginary);

        for (var index = 0; index < real.Length; index++)
        {
            Assert.True(Math.Abs(real[index] - 1) < 1e-12);
            Assert.True(Math.Abs(imaginary[index]) < 1e-12);
        }
    }

    [Fact]
    public void OneDimensionalRoundTripRestoresInput()
    {
        var random = new Random(12345);
        var real = new double[256];
        var imaginary = new double[256];
        for (var index = 0; index < real.Length; index++)
        {
            real[index] = random.NextDouble() * 2 - 1;
            imaginary[index] = random.NextDouble() * 2 - 1;
        }
        var expectedReal = (double[])real.Clone();
        var expectedImaginary = (double[])imaginary.Clone();

        FastFourierTransform.Forward(real, imaginary);
        FastFourierTransform.Inverse(real, imaginary);

        AssertMaximumError(expectedReal, expectedImaginary, real, imaginary, 1e-11);
    }

    [Fact]
    public void TwoDimensionalRoundTripRestoresInput()
    {
        var random = new Random(67890);
        var length = 64 * 32;
        var real = new double[length];
        var imaginary = new double[length];
        for (var index = 0; index < length; index++)
        {
            real[index] = random.NextDouble();
            imaginary[index] = random.NextDouble();
        }
        var expectedReal = (double[])real.Clone();
        var expectedImaginary = (double[])imaginary.Clone();

        FastFourierTransform.Forward2D(real, imaginary, 64, 32);
        FastFourierTransform.Inverse2D(real, imaginary, 64, 32);

        AssertMaximumError(expectedReal, expectedImaginary, real, imaginary, 1e-10);
    }

    static void AssertMaximumError(double[] expectedReal, double[] expectedImaginary, double[] actualReal, double[] actualImaginary, double tolerance)
    {
        var maximum = 0d;
        for (var index = 0; index < expectedReal.Length; index++)
        {
            var realError = Math.Abs(expectedReal[index] - actualReal[index]);
            var imaginaryError = Math.Abs(expectedImaginary[index] - actualImaginary[index]);
            maximum = Math.Max(maximum, Math.Max(realError, imaginaryError));
        }
        Assert.True(maximum <= tolerance, $"{maximum:R}");
    }
}
