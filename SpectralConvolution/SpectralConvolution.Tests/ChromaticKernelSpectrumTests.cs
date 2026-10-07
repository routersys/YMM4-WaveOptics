namespace SpectralConvolution.Tests;

public sealed class ChromaticKernelSpectrumTests
{
    static ChromaticKernelSpectrum Updated(int radius, int size)
    {
        var spectrum = new ChromaticKernelSpectrum();
        spectrum.Update(ChromaticConvolutionScene.Kernel(radius, 1.3, 0.6), ChromaticConvolutionScene.Kernel(radius, 1d, 0.9), ChromaticConvolutionScene.Kernel(radius, 0.7, 1.2), radius, size);
        return spectrum;
    }

    [Fact]
    public void ASpectrumThatHasNotBeenUpdatedIsEmpty()
    {
        var spectrum = new ChromaticKernelSpectrum();

        Assert.Equal(0, spectrum.Size);
        Assert.Equal(0, spectrum.Radius);
        Assert.True(spectrum.HalfSpectrum(ChromaticChannel.Green).IsEmpty);
    }

    [Theory]
    [InlineData(3, 64)]
    [InlineData(23, 128)]
    [InlineData(63, 512)]
    public void EveryChannelHoldsItsOwnNormalizedKernel(int radius, int size)
    {
        var spectrum = Updated(radius, size);

        Assert.Equal(size, spectrum.Size);
        Assert.Equal(radius, spectrum.Radius);
        Assert.Equal(1d, spectrum.Red.Kernel.ToArray().Sum(), 12);
        Assert.Equal(1d, spectrum.Green.Kernel.ToArray().Sum(), 12);
        Assert.Equal(1d, spectrum.Blue.Kernel.ToArray().Sum(), 12);
        Assert.NotEqual(spectrum.Red.Kernel.ToArray(), spectrum.Green.Kernel.ToArray());
        Assert.NotEqual(spectrum.Blue.Kernel.ToArray(), spectrum.Green.Kernel.ToArray());
    }

    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(512)]
    public void TheHalfSpectrumHoldsTheColumnsFromZeroToHalfTheSize(int size)
    {
        var spectrum = Updated(3, size);

        Assert.Equal(size / 2 + 1, spectrum.HalfColumns);
        Assert.Equal(spectrum.HalfColumns * size, spectrum.HalfSpectrum(ChromaticChannel.Red).Length);
        Assert.True(spectrum.HalfSpectrum(ChromaticChannel.Red).SequenceEqual(spectrum.Red.Spectrum[..(spectrum.HalfColumns * size)]));
        Assert.True(spectrum.HalfSpectrum(ChromaticChannel.Green).SequenceEqual(spectrum.Green.Spectrum[..(spectrum.HalfColumns * size)]));
        Assert.True(spectrum.HalfSpectrum(ChromaticChannel.Blue).SequenceEqual(spectrum.Blue.Spectrum[..(spectrum.HalfColumns * size)]));
    }

    [Fact]
    public void TheHalfSpectraAreCopiedOneChannelAfterAnother()
    {
        var spectrum = Updated(5, 128);
        var length = spectrum.HalfColumns * 128;
        var copy = new ComputeWeave.Float2[3 * length];

        spectrum.CopyHalfSpectra(copy);

        Assert.True(spectrum.HalfSpectrum(ChromaticChannel.Red).SequenceEqual(copy.AsSpan(0, length)));
        Assert.True(spectrum.HalfSpectrum(ChromaticChannel.Green).SequenceEqual(copy.AsSpan(length, length)));
        Assert.True(spectrum.HalfSpectrum(ChromaticChannel.Blue).SequenceEqual(copy.AsSpan(2 * length, length)));
    }

    [Fact]
    public void CopyingToAShortBufferOrFromAnEmptySpectrumIsRejected()
    {
        var spectrum = Updated(5, 64);

        Assert.Throws<ArgumentException>(() => spectrum.CopyHalfSpectra(new ComputeWeave.Float2[3 * spectrum.HalfColumns * 64 - 1]));
        Assert.Throws<ArgumentException>(() => new ChromaticKernelSpectrum().CopyHalfSpectra(new ComputeWeave.Float2[3 * 64]));
    }

    [Fact]
    public void AnUnknownChannelIsRejected()
    {
        var spectrum = Updated(3, 64);

        Assert.Throws<ArgumentOutOfRangeException>(() => spectrum.HalfSpectrum((ChromaticChannel)7));
    }

    [Fact]
    public void AKernelThatCannotBeNormalizedLeavesTheSpectrumEmpty()
    {
        var spectrum = Updated(3, 64);
        var good = ChromaticConvolutionScene.Kernel(3, 1d, 1d);

        Assert.Throws<ArgumentOutOfRangeException>(() => spectrum.Update(good, good, new double[good.Length], 3, 64));

        Assert.Equal(0, spectrum.Size);
    }
}
