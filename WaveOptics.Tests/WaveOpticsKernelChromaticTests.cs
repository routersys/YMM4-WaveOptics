using WaveOptics.Effects;
using WaveOptics.Rendering;

namespace WaveOptics.Tests;

public sealed class WaveOpticsKernelChromaticTests
{
    static WaveOpticsPipeline.PsfParameters Psf(WaveOpticsColorMode mode, float wavelength = 550f, int radius = 15, float defocus = 0f, WaveOpticsApertureShape shape = WaveOpticsApertureShape.Circular, int blades = 6)
        => new(WaveOpticsQuality.Standard, radius, wavelength, 8f, 4f, shape, blades, 0f, 0f, defocus, 0f, 0f, 0f, 0f, 0f, mode);

    [Fact]
    public void AMonochromeKernelIsNotChromatic()
    {
        var kernel = new WaveOpticsKernel();

        Assert.True(kernel.TryUpdate(Psf(WaveOpticsColorMode.Monochrome)));

        Assert.False(kernel.IsChromatic);
        Assert.Equal(128, kernel.Size);
        Assert.Equal(15, kernel.Radius);
    }

    [Theory]
    [InlineData(WaveOpticsColorMode.Primaries)]
    [InlineData(WaveOpticsColorMode.Broadband)]
    public void AChromaticKernelHoldsASpectrumForEveryChannel(WaveOpticsColorMode mode)
    {
        var kernel = new WaveOpticsKernel();

        Assert.True(kernel.TryUpdate(Psf(mode, radius: 30, defocus: 0.5f)));

        Assert.True(kernel.IsChromatic);
        Assert.True(kernel.IsValid);
        Assert.Equal(512, kernel.Size);
        Assert.Equal(30, kernel.Radius);
        Assert.Equal(kernel.ChromaticSpectrum.Size, kernel.Size);
        Assert.NotEqual(kernel.ChromaticSpectrum.Red.Kernel.ToArray(), kernel.ChromaticSpectrum.Blue.Kernel.ToArray());
    }

    [Theory]
    [InlineData(WaveOpticsColorMode.Primaries)]
    [InlineData(WaveOpticsColorMode.Broadband)]
    public void TheWavelengthIsNotPartOfTheKeyInAChromaticMode(WaveOpticsColorMode mode)
    {
        var kernel = new WaveOpticsKernel();
        Assert.True(kernel.TryUpdate(Psf(mode, 450f)));
        var version = kernel.Version;

        Assert.True(kernel.TryUpdate(Psf(mode, 700f)));

        Assert.Equal(version, kernel.Version);
    }

    [Fact]
    public void TheWavelengthIsPartOfTheKeyInTheMonochromeMode()
    {
        var kernel = new WaveOpticsKernel();
        Assert.True(kernel.TryUpdate(Psf(WaveOpticsColorMode.Monochrome, 450f)));
        var version = kernel.Version;

        Assert.True(kernel.TryUpdate(Psf(WaveOpticsColorMode.Monochrome, 700f)));

        Assert.NotEqual(version, kernel.Version);
    }

    [Fact]
    public void ChangingTheModeRecomputesTheKernel()
    {
        var kernel = new WaveOpticsKernel();
        Assert.True(kernel.TryUpdate(Psf(WaveOpticsColorMode.Primaries)));
        var version = kernel.Version;

        Assert.True(kernel.TryUpdate(Psf(WaveOpticsColorMode.Broadband)));
        var second = kernel.Version;
        Assert.True(kernel.TryUpdate(Psf(WaveOpticsColorMode.Monochrome)));

        Assert.NotEqual(version, second);
        Assert.NotEqual(second, kernel.Version);
        Assert.False(kernel.IsChromatic);
    }

    [Fact]
    public void ABladeCountOfACircularApertureIsNotPartOfTheKeyInAChromaticMode()
    {
        var kernel = new WaveOpticsKernel();
        Assert.True(kernel.TryUpdate(Psf(WaveOpticsColorMode.Primaries, blades: 5)));
        var version = kernel.Version;

        Assert.True(kernel.TryUpdate(Psf(WaveOpticsColorMode.Primaries, blades: 9)));

        Assert.Equal(version, kernel.Version);
    }

    [Fact]
    public void TheKeyOfAChromaticKernelIgnoresTheWavelengthOnly()
    {
        var first = WaveOpticsKernel.KeyOf(Psf(WaveOpticsColorMode.Primaries, 450f, defocus: 1f));
        var second = WaveOpticsKernel.KeyOf(Psf(WaveOpticsColorMode.Primaries, 700f, defocus: 1f));
        var third = WaveOpticsKernel.KeyOf(Psf(WaveOpticsColorMode.Primaries, 700f, defocus: 2f));

        Assert.Equal(first, second);
        Assert.NotEqual(second, third);
    }

    [Fact]
    public void AKernelThatCannotBeSampledIsInvalidAndStaysSoUntilTheKeyChanges()
    {
        var kernel = new WaveOpticsKernel();
        var closed = Psf(WaveOpticsColorMode.Primaries, shape: WaveOpticsApertureShape.RegularPolygon, blades: 4) with { Obstruction = 0.95f, Quality = WaveOpticsQuality.Draft, KernelRadius = 1 };
        Assert.True(kernel.TryUpdate(Psf(WaveOpticsColorMode.Primaries)));

        var updated = kernel.TryUpdate(closed);

        Assert.Equal(updated, kernel.IsValid);
        Assert.True(kernel.TryUpdate(Psf(WaveOpticsColorMode.Primaries)));
        Assert.True(kernel.IsValid);
    }
}
