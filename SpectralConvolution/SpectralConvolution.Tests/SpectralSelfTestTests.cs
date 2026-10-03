using ComputeWeave;

namespace SpectralConvolution.Tests;

[Collection("Direct3D12")]
public sealed class SpectralSelfTestTests
{
    static ConvolutionMeasurement[] Run(GraphicsDevice device, int maximumRadius)
    {
        var measurements = new ConvolutionMeasurement[SpectralSelfTest.MeasurementCount];
        var written = SpectralSelfTest.Run(device, maximumRadius, measurements);
        Assert.Equal(SpectralSelfTest.MeasurementCount, written);
        return measurements;
    }

    [Fact]
    public void AHealthyDevicePassesEveryMeasurement()
    {
        var measurements = Run(GraphicsDevice.GetDefault(), 63);

        Assert.All(measurements, measurement => Assert.True(measurement.Passes, $"{measurement}"));
        Assert.Equal(2, measurements.Count(measurement => measurement.Name == SpectralSelfTest.DeterminismName));
        Assert.Equal(2, measurements.Count(measurement => measurement.Name == SpectralSelfTest.FullName));
        Assert.All(measurements.Where(measurement => measurement.Name == SpectralSelfTest.DeterminismName), measurement => Assert.Equal(0d, measurement.Measured));
    }

    [Fact]
    public void TheSoftwareRendererPassesEveryMeasurement()
    {
        var device = GraphicsDevice.EnumerateDevices().First(candidate => !candidate.IsHardwareAccelerated);

        var measurements = Run(device, 15);

        Assert.All(measurements, measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    [Fact]
    public void ThePatternIsPremultipliedAndHoldsTransparentAndOpaqueAreas()
    {
        var pattern = SpectralSelfTest.Pattern(200, 150, 7);

        var transparent = 0;
        var opaque = 0;
        for (var index = 0; index < pattern.Length; index += 4)
        {
            Assert.True(pattern[index] <= pattern[index + 3]);
            Assert.True(pattern[index + 1] <= pattern[index + 3]);
            Assert.True(pattern[index + 2] <= pattern[index + 3]);
            if (pattern[index + 3] == 0)
                transparent++;
            if (pattern[index + 3] == 255)
                opaque++;
        }

        Assert.True(transparent > 1000);
        Assert.True(opaque > 1000);
        Assert.Equal(pattern, SpectralSelfTest.Pattern(200, 150, 7));
        Assert.NotEqual(pattern, SpectralSelfTest.Pattern(200, 150, 8));
    }

    [Fact]
    public void TheKernelIsPositiveAndAsymmetric()
    {
        var kernel = SpectralSelfTest.Kernel(5);

        Assert.All(kernel, value => Assert.True(value > 0d));
        Assert.NotEqual(kernel[0], kernel[10]);
        Assert.NotEqual(kernel[0], kernel[110]);
    }

    [Fact]
    public void TheSamplesIncludeTheCornersTheCenterAndATileSeam()
    {
        var samples = new Int2[GpuTileConvolver.MaximumSamples];

        SpectralSelfTest.Samples(300, 200, 122, samples);

        Assert.Contains(new Int2(0, 0), samples);
        Assert.Contains(new Int2(299, 199), samples);
        Assert.Contains(new Int2(150, 100), samples);
        Assert.Contains(new Int2(121, 121), samples);
        Assert.Contains(new Int2(122, 122), samples);
        Assert.All(samples, sample => Assert.True(sample.X is >= 0 and < 300 && sample.Y is >= 0 and < 200));
    }

    [Fact]
    public void InvalidRequestsAreRejected()
    {
        var device = GraphicsDevice.GetDefault();

        Assert.Throws<ArgumentOutOfRangeException>(() => SpectralSelfTest.Run(device, SpectralSelfTest.FullRadius - 1, new ConvolutionMeasurement[SpectralSelfTest.MeasurementCount]));
        Assert.Throws<ArgumentException>(() => SpectralSelfTest.Run(device, 15, new ConvolutionMeasurement[SpectralSelfTest.MeasurementCount - 1]));
        Assert.Throws<ArgumentException>(() => SpectralSelfTest.Samples(10, 10, 5, new Int2[3]));
    }
}
