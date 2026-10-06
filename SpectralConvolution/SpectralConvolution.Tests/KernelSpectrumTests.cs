namespace SpectralConvolution.Tests;

public sealed class KernelSpectrumTests
{
    static double[] AsymmetricKernel(int radius)
    {
        var size = radius * 2 + 1;
        var values = new double[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
                values[y * size + x] = 1d + x * 0.75 + y * y * 0.125 + (x == size - 1 && y == 0 ? 9d : 0d);
        }

        return values;
    }

    static (double Real, double Imaginary) NaiveSpectrum(ReadOnlySpan<double> kernel, int radius, int size, int frequencyX, int frequencyY)
    {
        var kernelSize = radius * 2 + 1;
        var real = 0d;
        var imaginary = 0d;
        for (var offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                var weight = kernel[(offsetY + radius) * kernelSize + offsetX + radius];
                var angle = 2d * Math.PI * (frequencyX * offsetX + frequencyY * offsetY) / size;
                real += weight * Math.Cos(angle);
                imaginary += weight * Math.Sin(angle);
            }
        }

        return (real, imaginary);
    }

    [Fact]
    public void TheKernelIsScaledToSumToOne()
    {
        var values = AsymmetricKernel(3);
        var spectrum = new KernelSpectrum();

        spectrum.Update(values, 3, 64);

        var sum = values.Sum();
        Assert.Equal(1d, spectrum.Kernel.ToArray().Sum(), 12);
        for (var index = 0; index < values.Length; index++)
            Assert.Equal(values[index] / sum, spectrum.Kernel[index]);
    }

    [Fact]
    public void TheTwiddlesAreTheRoundedRootsOfUnity()
    {
        var spectrum = new KernelSpectrum();

        spectrum.Update(AsymmetricKernel(1), 1, 128);

        Assert.Equal(64, spectrum.Twiddles.Length);
        for (var index = 0; index < 64; index++)
        {
            var (sin, cos) = Math.SinCos(-2d * Math.PI * index / 128);
            Assert.Equal(KernelSpectrum.Flush((float)cos), spectrum.Twiddles[index].X);
            Assert.Equal(KernelSpectrum.Flush((float)sin), spectrum.Twiddles[index].Y);
        }
        Assert.Equal(1f, spectrum.Twiddles[0].X);
        Assert.Equal(0f, spectrum.Twiddles[0].Y);
        Assert.Equal(-1f, spectrum.Twiddles[32].Y);
    }

    [Theory]
    [InlineData(2, 64)]
    [InlineData(5, 128)]
    public void TheSpectrumIsTheTransformOfTheReversedKernelStoredByColumn(int radius, int size)
    {
        var spectrum = new KernelSpectrum();

        spectrum.Update(AsymmetricKernel(radius), radius, size);

        var bound = ConvolutionBound.SpectrumError(size) + 1e-12;
        for (var frequencyY = 0; frequencyY < size; frequencyY += 7)
        {
            for (var frequencyX = 0; frequencyX < size; frequencyX += 5)
            {
                var (real, imaginary) = NaiveSpectrum(spectrum.Kernel, radius, size, frequencyX, frequencyY);
                var value = spectrum.Spectrum[frequencyX * size + frequencyY];
                Assert.True(Math.Abs(value.X - real) <= bound, $"({frequencyX}, {frequencyY}) real {value.X} {real}");
                Assert.True(Math.Abs(value.Y - imaginary) <= bound, $"({frequencyX}, {frequencyY}) imaginary {value.Y} {imaginary}");
            }
        }
    }

    [Theory]
    [InlineData(5, 64)]
    [InlineData(20, 128)]
    [InlineData(63, 512)]
    public void EveryElementOfTheSpectrumAgreesWithAFullTwoDimensionalTransform(int radius, int size)
    {
        var values = AsymmetricKernel(radius);
        var spectrum = new KernelSpectrum();

        spectrum.Update(values, radius, size);

        var kernelSize = radius * 2 + 1;
        var sum = values.Sum();
        var mask = size - 1;
        var real = new double[size * size];
        var imaginary = new double[size * size];
        for (var offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
                real[(-offsetY & mask) * size + (-offsetX & mask)] = values[(offsetY + radius) * kernelSize + offsetX + radius] / sum;
        }

        FastFourierTransform.Forward2D(real, imaginary, size, size);

        var excess = double.NegativeInfinity;
        for (var frequencyY = 0; frequencyY < size; frequencyY++)
        {
            for (var frequencyX = 0; frequencyX < size; frequencyX++)
            {
                var value = spectrum.Spectrum[frequencyX * size + frequencyY];
                var index = frequencyY * size + frequencyX;
                excess = Math.Max(excess, Math.Abs(value.X - real[index]) - (1e-12 + Math.Abs(real[index]) * 1e-6));
                excess = Math.Max(excess, Math.Abs(value.Y - imaginary[index]) - (1e-12 + Math.Abs(imaginary[index]) * 1e-6));
            }
        }

        Assert.True(excess <= 0d, $"{excess}");
    }

    [Fact]
    public void TheSpectrumAtZeroFrequencyIsTheKernelSum()
    {
        var spectrum = new KernelSpectrum();

        spectrum.Update(AsymmetricKernel(4), 4, 64);

        Assert.Equal(1f, spectrum.Spectrum[0].X);
        Assert.Equal(0f, spectrum.Spectrum[0].Y);
    }

    [Fact]
    public void AReusedSpectrumMatchesAFreshOne()
    {
        var reused = new KernelSpectrum();
        reused.Update(AsymmetricKernel(20), 20, 512);
        reused.Update(AsymmetricKernel(3), 3, 64);
        var fresh = new KernelSpectrum();
        fresh.Update(AsymmetricKernel(3), 3, 64);

        Assert.Equal(fresh.Size, reused.Size);
        Assert.Equal(fresh.Radius, reused.Radius);
        Assert.Equal(fresh.Kernel.ToArray(), reused.Kernel.ToArray());
        Assert.Equal(fresh.Twiddles.ToArray(), reused.Twiddles.ToArray());
        Assert.Equal(fresh.Spectrum.ToArray(), reused.Spectrum.ToArray());
    }

    [Fact]
    public void ANegativeWeightIsRejected()
    {
        var values = AsymmetricKernel(1);
        values[4] = -1e-9;

        Assert.Throws<ArgumentOutOfRangeException>(() => new KernelSpectrum().Update(values, 1, 64));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonFiniteWeightIsRejected(double weight)
    {
        var values = AsymmetricKernel(1);
        values[0] = weight;

        Assert.Throws<ArgumentOutOfRangeException>(() => new KernelSpectrum().Update(values, 1, 64));
    }

    [Fact]
    public void AnAllZeroKernelIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KernelSpectrum().Update(new double[9], 1, 64));
    }

    [Fact]
    public void AKernelOfTheWrongLengthIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new KernelSpectrum().Update(new double[8], 1, 64));
    }

    [Fact]
    public void ARejectedKernelKeepsThePreviousSpectrum()
    {
        var spectrum = new KernelSpectrum();
        spectrum.Update(AsymmetricKernel(1), 1, 64);

        Assert.Throws<ArgumentOutOfRangeException>(() => spectrum.Update(new double[9], 1, 64));

        Assert.Equal(64, spectrum.Size);
        Assert.Throws<ArgumentOutOfRangeException>(() => spectrum.Update(AsymmetricKernel(40), 40, 64));
        Assert.Equal(64, spectrum.Size);
    }

    [Fact]
    public void SubnormalValuesBecomeSignedZero()
    {
        var subnormal = float.Epsilon * 3;

        Assert.Equal(0f, KernelSpectrum.Flush(subnormal));
        Assert.False(float.IsNegative(KernelSpectrum.Flush(subnormal)));
        Assert.True(float.IsNegative(KernelSpectrum.Flush(-subnormal)));
        Assert.Equal(1.5e-38f, KernelSpectrum.Flush(1.5e-38f));
        Assert.Equal(-0.25f, KernelSpectrum.Flush(-0.25f));
    }
}
