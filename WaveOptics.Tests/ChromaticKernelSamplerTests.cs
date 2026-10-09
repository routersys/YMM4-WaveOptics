using WaveOptics.Abstractions;
using WaveOptics.Effects;
using WaveOptics.Optics;

namespace WaveOptics.Tests.Psf;

public sealed class ChromaticKernelSamplerTests
{
    readonly FraunhoferKernelSampler single = new();
    readonly ChromaticKernelSampler sampler = new();

    static PsfSpecification Specification(
        int gridSize = 256,
        int kernelRadius = 15,
        double fNumber = 8,
        double pixelPitch = 4,
        ApertureShape shape = ApertureShape.Circular,
        int blades = 6,
        double obstruction = 0,
        WavefrontAberration aberration = default,
        double wavelength = SpectralPlan.ReferenceWavelength)
        => new(gridSize, gridSize / 4, kernelRadius * 2 + 1, wavelength, fNumber, pixelPitch, shape, blades, 0, obstruction, aberration);

    static double Radius(ReadOnlySpan<double> kernel, double fraction)
    {
        var size = (int)Math.Sqrt(kernel.Length);
        var center = size / 2;
        var pairs = new List<(double Distance, double Value)>();
        for (var index = 0; index < kernel.Length; index++)
            pairs.Add((Math.Sqrt(Math.Pow(index % size - center, 2) + Math.Pow(index / size - center, 2)), kernel[index]));
        pairs.Sort((left, right) => left.Distance.CompareTo(right.Distance));
        var total = 0d;
        foreach (var (distance, value) in pairs)
        {
            total += value;
            if (total >= fraction)
                return distance;
        }

        return pairs[^1].Distance;
    }

    static double Distance(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
    {
        var sum = 0d;
        for (var index = 0; index < left.Length; index++)
            sum += Math.Abs(left[index] - right[index]);
        return sum;
    }

    (double[] Red, double[] Green, double[] Blue) Sample(in PsfSpecification specification, WaveOpticsColorMode mode, WaveOpticsQuality quality)
    {
        var area = specification.KernelSize * specification.KernelSize;
        var red = new double[area];
        var green = new double[area];
        var blue = new double[area];
        Assert.True(sampler.TrySample(in specification, mode, quality, red, green, blue));
        return (red, green, blue);
    }

    public static TheoryData<string> CaseNames => ["clear", "defocus", "coma", "polygon", "obstructed", "astigmatism"];

    static PsfSpecification Case(string name)
        => name switch
        {
            "clear" => Specification(),
            "defocus" => Specification(aberration: new WavefrontAberration(defocusWaves: 1.5)),
            "coma" => Specification(kernelRadius: 30, aberration: new WavefrontAberration(comaHorizontalWaves: 1.2, sphericalWaves: -0.4)),
            "polygon" => Specification(shape: ApertureShape.RegularPolygon, blades: 7, fNumber: 16, pixelPitch: 2),
            "obstructed" => Specification(gridSize: 512, obstruction: 0.5, aberration: new WavefrontAberration(defocusWaves: 2)),
            _ => Specification(aberration: new WavefrontAberration(astigmatismVerticalWaves: 0.8, astigmatismObliqueWaves: -0.6)),
        };

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void TheGreenKernelOfThePrimariesIsTheMonochromeKernelAtTheReferenceWavelength(string name)
    {
        var specification = Case(name);
        var expected = new double[specification.KernelSize * specification.KernelSize];
        Assert.True(single.TrySample(in specification, expected));

        var (_, green, _) = Sample(in specification, WaveOpticsColorMode.Primaries, WaveOpticsQuality.Standard);

        Assert.Equal(expected, green);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryChannelOfEveryModeIsANormalizedNonNegativeKernel(string name)
    {
        var specification = Case(name);

        foreach (var mode in new[] { WaveOpticsColorMode.Primaries, WaveOpticsColorMode.Broadband })
        {
            foreach (var quality in Enum.GetValues<WaveOpticsQuality>())
            {
                var (red, green, blue) = Sample(in specification, mode, quality);

                foreach (var kernel in new[] { red, green, blue })
                {
                    Assert.Equal(1d, kernel.Sum(), 9);
                    Assert.All(kernel, value => Assert.True(value >= 0d && double.IsFinite(value)));
                }
            }
        }
    }

    [Fact]
    public void AGeometricBlurHasTheSameSizeInEveryChannelWhereAFixedNumberOfWavesDoesNot()
    {
        var aberration = new WavefrontAberration(defocusWaves: 3);
        var specification = Specification(gridSize: 512, kernelRadius: 63, aberration: aberration);

        var (red, green, blue) = Sample(in specification, WaveOpticsColorMode.Primaries, WaveOpticsQuality.High);

        var chromaticRatio = Radius(red, 0.9) / Radius(blue, 0.9);
        Assert.InRange(chromaticRatio, 0.94, 1.06);
        Assert.InRange(Radius(green, 0.9) / Radius(blue, 0.9), 0.94, 1.06);
        var redWaves = new double[red.Length];
        var blueWaves = new double[red.Length];
        var redSpecification = Specification(gridSize: 512, kernelRadius: 63, aberration: aberration, wavelength: SpectralPlan.RedWavelength);
        var blueSpecification = Specification(gridSize: 512, kernelRadius: 63, aberration: aberration, wavelength: SpectralPlan.BlueWavelength);
        Assert.True(single.TrySample(in redSpecification, redWaves));
        Assert.True(single.TrySample(in blueSpecification, blueWaves));
        Assert.True(Radius(redWaves, 0.9) / Radius(blueWaves, 0.9) > 1.2);
    }

    [Fact]
    public void WithoutAberrationTheDiffractionPatternGrowsWithTheWavelength()
    {
        var specification = Specification(kernelRadius: 40, fNumber: 16, pixelPitch: 2);

        var (red, green, blue) = Sample(in specification, WaveOpticsColorMode.Primaries, WaveOpticsQuality.Standard);

        var redPeak = red.Max();
        var greenPeak = green.Max();
        var bluePeak = blue.Max();
        Assert.True(redPeak < greenPeak && greenPeak < bluePeak, $"{redPeak} {greenPeak} {bluePeak}");
        var expected = Math.Pow(SpectralPlan.BlueWavelength / SpectralPlan.RedWavelength, 2);
        Assert.InRange(redPeak / bluePeak, expected * 0.85, expected * 1.15);
    }

    [Theory]
    [InlineData(WaveOpticsColorMode.Primaries, 1e-6)]
    [InlineData(WaveOpticsColorMode.Broadband, 0.05)]
    public void AVanishingAberrationGivesTheKernelOfNoAberration(WaveOpticsColorMode mode, double tolerance)
    {
        var clear = Specification(kernelRadius: 30);
        var nearlyClear = Specification(kernelRadius: 30, aberration: new WavefrontAberration(defocusWaves: 1e-9));

        var (clearRed, clearGreen, clearBlue) = Sample(in clear, mode, WaveOpticsQuality.Standard);
        var (nearRed, nearGreen, nearBlue) = Sample(in nearlyClear, mode, WaveOpticsQuality.High);

        Assert.True(Distance(clearRed, nearRed) < tolerance, $"{Distance(clearRed, nearRed)}");
        Assert.True(Distance(clearGreen, nearGreen) < tolerance, $"{Distance(clearGreen, nearGreen)}");
        Assert.True(Distance(clearBlue, nearBlue) < tolerance, $"{Distance(clearBlue, nearBlue)}");
    }

    [Fact]
    public void WithoutAberrationTheQualityOnlyChangesTheGridNotTheWavelengthSampling()
    {
        var specification = Specification(kernelRadius: 30);

        var draft = Sample(in specification, WaveOpticsColorMode.Broadband, WaveOpticsQuality.Draft);
        var high = Sample(in specification, WaveOpticsColorMode.Broadband, WaveOpticsQuality.High);

        Assert.Equal(draft.Red, high.Red);
        Assert.Equal(draft.Green, high.Green);
        Assert.Equal(draft.Blue, high.Blue);
    }

    [Fact]
    public void ABroadbandKernelIsASmoothedVersionOfThePrimaryKernel()
    {
        var specification = Specification(kernelRadius: 30, fNumber: 16, pixelPitch: 2);

        var (_, primary, _) = Sample(in specification, WaveOpticsColorMode.Primaries, WaveOpticsQuality.Standard);
        var (_, broadband, _) = Sample(in specification, WaveOpticsColorMode.Broadband, WaveOpticsQuality.Standard);

        Assert.InRange(Distance(primary, broadband), 0.005, 0.8);
        Assert.Equal(primary.Sum(), broadband.Sum(), 9);
    }

    [Fact]
    public void ALowerQualityApproachesTheHighestQualityOnlyAsCloselyAsItsNodesAllow()
    {
        var specification = Specification(gridSize: 256, kernelRadius: 30, aberration: new WavefrontAberration(comaHorizontalWaves: 1d, defocusWaves: 1d));

        var draft = Sample(in specification, WaveOpticsColorMode.Broadband, WaveOpticsQuality.Draft);
        var standard = Sample(in specification, WaveOpticsColorMode.Broadband, WaveOpticsQuality.Standard);
        var high = Sample(in specification, WaveOpticsColorMode.Broadband, WaveOpticsQuality.High);

        var draftDistance = Distance(draft.Blue, high.Blue) + Distance(draft.Green, high.Green) + Distance(draft.Red, high.Red);
        var standardDistance = Distance(standard.Blue, high.Blue) + Distance(standard.Green, high.Green) + Distance(standard.Red, high.Red);
        Assert.True(standardDistance < draftDistance, $"{standardDistance} {draftDistance}");
        Assert.True(draftDistance < 0.45, $"{draftDistance}");
    }

    [Fact]
    public void TheChannelsOfAnAberratedLensDifferWhileTheirSumsStayEqual()
    {
        var specification = Case("coma");

        var (red, green, blue) = Sample(in specification, WaveOpticsColorMode.Primaries, WaveOpticsQuality.Standard);

        Assert.True(Distance(red, green) > 0.01);
        Assert.True(Distance(blue, green) > 0.01);
        Assert.Equal(red.Sum(), blue.Sum(), 9);
    }

    [Fact]
    public void ARequestForTheMonochromeModeIsRejected()
    {
        var specification = Specification();
        var area = specification.KernelSize * specification.KernelSize;

        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.TrySample(in specification, WaveOpticsColorMode.Monochrome, WaveOpticsQuality.Standard, new double[area], new double[area], new double[area]));
        Assert.Throws<ArgumentException>(() => sampler.TrySample(in specification, WaveOpticsColorMode.Primaries, WaveOpticsQuality.Standard, new double[area], new double[area - 1], new double[area]));
        Assert.Throws<ArgumentException>(() => sampler.TrySample(in specification, WaveOpticsColorMode.Primaries, WaveOpticsQuality.Standard, new double[area], new double[area], new double[area + 1]));
    }

    [Fact]
    public void AClosedApertureFailsWithoutThrowing()
    {
        var specification = Specification(gridSize: 64, shape: ApertureShape.RegularPolygon, blades: 4, obstruction: 0.95, kernelRadius: 3);
        var area = specification.KernelSize * specification.KernelSize;

        var sampled = sampler.TrySample(in specification, WaveOpticsColorMode.Broadband, WaveOpticsQuality.Draft, new double[area], new double[area], new double[area]);

        Assert.False(sampled);
    }

    [Theory]
    [InlineData(WaveOpticsColorMode.Primaries, "clear")]
    [InlineData(WaveOpticsColorMode.Broadband, "clear")]
    [InlineData(WaveOpticsColorMode.Primaries, "coma")]
    [InlineData(WaveOpticsColorMode.Broadband, "coma")]
    public void AWarmSamplerAllocatesNoManagedMemory(WaveOpticsColorMode mode, string name)
    {
        var specification = Case(name);
        var area = specification.KernelSize * specification.KernelSize;
        var red = new double[area];
        var green = new double[area];
        var blue = new double[area];
        void Sample() => Assert.True(sampler.TrySample(in specification, mode, WaveOpticsQuality.High, red, green, blue));

        Sample();
        Sample();
        AllocationProbe.Settle();

        Assert.Equal(0, AllocationProbe.MinimumAllocatedBytes(Sample, 8));
    }

    [Theory]
    [InlineData(WaveOpticsColorMode.Primaries)]
    [InlineData(WaveOpticsColorMode.Broadband)]
    public void TheOutputBuffersNeedNotBeEmpty(WaveOpticsColorMode mode)
    {
        var specification = Case("defocus");
        var area = specification.KernelSize * specification.KernelSize;
        var fresh = Sample(in specification, mode, WaveOpticsQuality.High);
        var red = new double[area];
        var green = new double[area];
        var blue = new double[area];
        Array.Fill(red, 3d);
        Array.Fill(green, 5d);
        Array.Fill(blue, 7d);

        Assert.True(sampler.TrySample(in specification, mode, WaveOpticsQuality.High, red, green, blue));

        Assert.Equal(fresh.Red, red);
        Assert.Equal(fresh.Green, green);
        Assert.Equal(fresh.Blue, blue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EveryAberrationTermMakesTheBroadbandUseTheNodesOfTheQuality(int term)
    {
        var values = new double[8];
        values[term] = 0.8;
        var aberration = new WavefrontAberration(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]);
        var specification = Specification(aberration: aberration);

        var draft = Sample(in specification, WaveOpticsColorMode.Broadband, WaveOpticsQuality.Draft);
        var standard = Sample(in specification, WaveOpticsColorMode.Broadband, WaveOpticsQuality.Standard);

        Assert.NotEqual(draft.Green, standard.Green);
    }
}
