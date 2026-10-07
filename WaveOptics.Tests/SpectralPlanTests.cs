using WaveOptics.Effects;
using WaveOptics.Optics;

namespace WaveOptics.Tests.Psf;

public sealed class SpectralPlanTests
{
    [Fact]
    public void ThePrimariesUseOneWavelengthPerChannelWithFullWeight()
    {
        var plan = SpectralPlan.For(WaveOpticsColorMode.Primaries, WaveOpticsQuality.High, false);

        Assert.Equal(3, plan.Length);
        double[] wavelengths = [SpectralPlan.RedWavelength, SpectralPlan.GreenWavelength, SpectralPlan.BlueWavelength];
        for (var channel = 0; channel < 3; channel++)
        {
            var node = Assert.Single(plan[channel]);
            Assert.Equal(wavelengths[channel], node.Wavelength);
            var sample = Assert.Single(node.Samples);
            Assert.Equal(wavelengths[channel], sample.Wavelength);
            Assert.Equal(1d, sample.Weight);
        }
    }

    [Fact]
    public void TheWavelengthsAndTheWidthOfTheBandsAreTheDocumentedValues()
    {
        Assert.Equal(610d, SpectralPlan.RedWavelength);
        Assert.Equal(550d, SpectralPlan.GreenWavelength);
        Assert.Equal(465d, SpectralPlan.BlueWavelength);
        Assert.Equal(550d, SpectralPlan.ReferenceWavelength);
        Assert.Equal(21d, SpectralPlan.BandSigma);
        Assert.Equal(2.5, SpectralPlan.BandSpan);
    }

    [Fact]
    public void TheGreenPrimaryIsTheReferenceWavelength()
    {
        Assert.Equal(SpectralPlan.ReferenceWavelength, SpectralPlan.GreenWavelength);
    }

    [Theory]
    [InlineData(WaveOpticsQuality.Draft, 1, 25)]
    [InlineData(WaveOpticsQuality.Standard, 3, 27)]
    [InlineData(WaveOpticsQuality.High, 5, 25)]
    public void ABroadbandIsDividedIntoNodesAndSamplesByQuality(WaveOpticsQuality quality, int nodes, int samples)
    {
        var plan = SpectralPlan.For(WaveOpticsColorMode.Broadband, quality, false);

        foreach (var channel in plan)
        {
            Assert.Equal(nodes, channel.Length);
            Assert.Equal(samples, SpectralPlan.SampleCount(channel));
            Assert.True(SpectralPlan.SampleCount(channel) <= SpectralPlan.MaximumSamples);
            Assert.All(channel, node => Assert.True(node.Samples.Length <= SpectralPlan.MaximumSamples));
        }
    }

    [Theory]
    [InlineData(WaveOpticsQuality.Draft)]
    [InlineData(WaveOpticsQuality.Standard)]
    [InlineData(WaveOpticsQuality.High)]
    public void TheWeightsOfABandSumToOneAndFollowAGaussian(WaveOpticsQuality quality)
    {
        var plan = SpectralPlan.For(WaveOpticsColorMode.Broadband, quality, false);
        double[] centers = [SpectralPlan.RedWavelength, SpectralPlan.GreenWavelength, SpectralPlan.BlueWavelength];

        for (var channel = 0; channel < 3; channel++)
        {
            var samples = plan[channel].SelectMany(node => node.Samples).ToArray();
            Assert.Equal(1d, samples.Sum(sample => sample.Weight), 12);
            Assert.Equal(centers[channel], samples.Sum(sample => sample.Weight * sample.Wavelength), 3);
            var middle = samples[samples.Length / 2];
            Assert.Equal(centers[channel], middle.Wavelength, 9);
            Assert.Equal(samples.Max(sample => sample.Weight), middle.Weight);
            Assert.InRange(samples[0].Wavelength, centers[channel] - SpectralPlan.BandSpan * SpectralPlan.BandSigma, centers[channel] - 2d * SpectralPlan.BandSigma);
            Assert.True(samples.Zip(samples.Skip(1)).All(pair => pair.Second.Wavelength > pair.First.Wavelength));
        }
    }

    [Fact]
    public void ANodeSitsAtTheMiddleSampleOfItsGroup()
    {
        var plan = SpectralPlan.For(WaveOpticsColorMode.Broadband, WaveOpticsQuality.Standard, false);

        foreach (var channel in plan)
        {
            foreach (var node in channel)
                Assert.Equal(node.Samples[node.Samples.Length / 2].Wavelength, node.Wavelength);
        }
    }

    [Theory]
    [InlineData(WaveOpticsQuality.Standard)]
    [InlineData(WaveOpticsQuality.High)]
    public void WithoutAberrationABroadbandUsesOneNodePerChannel(WaveOpticsQuality quality)
    {
        var plan = SpectralPlan.For(WaveOpticsColorMode.Broadband, quality, true);

        Assert.All(plan, channel => Assert.Single(channel));
        Assert.Same(SpectralPlan.For(WaveOpticsColorMode.Broadband, WaveOpticsQuality.Draft, false), plan);
    }

    [Fact]
    public void TheMonochromeModeHasNoPlan()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpectralPlan.For(WaveOpticsColorMode.Monochrome, WaveOpticsQuality.Standard, false));
    }
}
