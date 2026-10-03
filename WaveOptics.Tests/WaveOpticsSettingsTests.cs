using SpectralConvolution;
using WaveOptics.Abstractions;
using WaveOptics.Effects;
using WaveOptics.Optics;

namespace WaveOptics.Tests;

public sealed class WaveOpticsSettingsTests
{
    [Theory]
    [InlineData(WaveOpticsQuality.Draft, 128)]
    [InlineData(WaveOpticsQuality.Standard, 256)]
    [InlineData(WaveOpticsQuality.High, 512)]
    public void EveryQualityChoosesItsPupilGrid(WaveOpticsQuality quality, int gridSize)
        => Assert.Equal(gridSize, WaveOpticsSettings.GetPupilGridSize(quality));

    [Theory]
    [InlineData(128, 32)]
    [InlineData(256, 64)]
    [InlineData(512, 128)]
    public void ThePupilSpansAQuarterOfTheGrid(int gridSize, int diameter)
        => Assert.Equal(diameter, WaveOpticsSettings.GetPupilDiameterSamples(gridSize));

    [Theory]
    [InlineData(1, 3)]
    [InlineData(7, 15)]
    [InlineData(15, 31)]
    public void TheKernelSpansTheRadiusOnBothSidesOfTheCenter(int radius, int size)
        => Assert.Equal(size, WaveOpticsSettings.GetKernelSize(radius));

    [Fact]
    public void TheLargestKernelFitsInTheLargestTile()
    {
        Assert.Equal(WaveOpticsSettings.MaximumKernelSize, WaveOpticsSettings.GetKernelSize(WaveOpticsSettings.MaximumKernelRadius));
        Assert.True(WaveOpticsSettings.MaximumKernelRadius <= TilePlan.MaximumRadius);
        Assert.Equal(TilePlan.LargeSize, TilePlan.SelectSize(WaveOpticsSettings.MaximumKernelRadius));
    }

    public static readonly TheoryData<string, WavefrontAberration> Aberrations = new()
    {
        { "defocus", new WavefrontAberration(defocusWaves: 1) },
        { "astigmatism", new WavefrontAberration(astigmatismObliqueWaves: 1) },
        { "coma", new WavefrontAberration(comaHorizontalWaves: 3) },
        { "spherical", new WavefrontAberration(sphericalWaves: 1) },
        { "every term at the limit", new WavefrontAberration(defocusWaves: 10, astigmatismVerticalWaves: 10, astigmatismObliqueWaves: 10, comaHorizontalWaves: 10, comaVerticalWaves: 10, sphericalWaves: 10) },
    };

    [Theory]
    [MemberData(nameof(Aberrations))]
    public void EveryAberrationGivesAKernelTheConvolutionAccepts(string aberrationName, WavefrontAberration aberration)
    {
        var gridSize = WaveOpticsSettings.GetPupilGridSize(WaveOpticsQuality.Standard);
        var descriptor = new PsfDescriptor(
            gridSize,
            WaveOpticsSettings.GetPupilDiameterSamples(gridSize),
            WaveOpticsSettings.MaximumKernelSize,
            550, 8, 4, ApertureShape.Circular, 6, 0, 0, aberration);
        var kernel = new FraunhoferPsfGenerator().Generate(descriptor).Kernel;
        var spectrum = new KernelSpectrum();

        spectrum.Update(kernel.Values.Span, WaveOpticsSettings.MaximumKernelRadius, TilePlan.SelectSize(WaveOpticsSettings.MaximumKernelRadius));

        Assert.True(Math.Abs(spectrum.Spectrum[0].X - 1f) <= 1e-6f, aberrationName);
    }

    [Theory]
    [InlineData(WaveOpticsSettings.MinimumKernelRadius)]
    [InlineData(WaveOpticsSettings.DefaultKernelRadius)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(48)]
    [InlineData(WaveOpticsSettings.MaximumKernelRadius)]
    public void TheCanvasMarginHoldsTheBlurOnAFourPixelGrid(int radius)
    {
        var margin = WaveOpticsSettings.GetCanvasMargin(radius);

        Assert.InRange(margin, radius, Math.Max(radius + 3, WaveOpticsSettings.DefaultCanvasMargin));
        Assert.Equal(0, margin % 4);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    public void UpToTheDefaultRadiusTheCanvasKeepsItsMargin(int radius)
        => Assert.Equal(16, WaveOpticsSettings.GetCanvasMargin(radius));

    [Fact]
    public void EveryScratchValueHasItsOwnSlot()
    {
        int[] slots =
        [
            WaveOpticsSettings.ScratchLitCount,
            WaveOpticsSettings.ScratchBoundsMinX,
            WaveOpticsSettings.ScratchBoundsMinY,
            WaveOpticsSettings.ScratchBoundsMaxX,
            WaveOpticsSettings.ScratchBoundsMaxY,
            WaveOpticsSettings.ScratchHashSum,
            WaveOpticsSettings.ScratchHashMix,
        ];

        Assert.Equal(slots.Length, slots.Distinct().Count());
        Assert.All(slots, slot => Assert.InRange(slot, 0, WaveOpticsSettings.ScratchLength - 1));
    }
}
