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
    public void TheLargestKernelFitsInEveryWeightSlot()
        => Assert.Equal(WaveOpticsSettings.MaximumKernelSize, WaveOpticsSettings.GetKernelSize(WaveOpticsSettings.MaximumKernelRadius));

    [Fact]
    public void EveryTermAndAxisOwnsItsOwnWeightSlot()
    {
        var offsets = Enumerable.Range(0, WaveOpticsSettings.MaximumRank)
            .SelectMany(term => new[] { WaveOpticsSettings.GetWeightOffset(term, 0), WaveOpticsSettings.GetWeightOffset(term, 1) })
            .Order()
            .ToArray();

        Assert.Equal(WaveOpticsSettings.MaximumRank * 2, offsets.Distinct().Count());
        Assert.Equal(0, offsets[0]);
        Assert.All(offsets.Zip(offsets.Skip(1)), pair => Assert.Equal(WaveOpticsSettings.MaximumKernelSize, pair.Second - pair.First));
        Assert.Equal(WaveOpticsSettings.WeightsLength, offsets[^1] + WaveOpticsSettings.MaximumKernelSize);
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
    public void TheRankLimitKeepsEveryAberrationWithinTheResidualBudget(string aberrationName, WavefrontAberration aberration)
    {
        var gridSize = WaveOpticsSettings.GetPupilGridSize(WaveOpticsQuality.Standard);
        var descriptor = new PsfDescriptor(
            gridSize,
            WaveOpticsSettings.GetPupilDiameterSamples(gridSize),
            WaveOpticsSettings.MaximumKernelSize,
            550, 8, 4, ApertureShape.Circular, 6, 0, 0, aberration);
        var kernel = new FraunhoferPsfGenerator().Generate(descriptor).Kernel;

        var separable = SeparableKernel.Decompose(kernel.Values.Span, kernel.Size, WaveOpticsSettings.SeparableResidualRatio, WaveOpticsSettings.MaximumRank);

        Assert.True(separable.ResidualEnergyRatio <= WaveOpticsSettings.SeparableResidualRatio, $"{aberrationName}: {separable.ResidualEnergyRatio}");
    }

    [Fact]
    public void TheCanvasMarginHoldsTheWidestBlurOnAFourPixelGrid()
    {
        Assert.True(WaveOpticsSettings.CanvasMargin >= WaveOpticsSettings.MaximumKernelRadius);
        Assert.Equal(0, WaveOpticsSettings.CanvasMargin % 4);
    }

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
