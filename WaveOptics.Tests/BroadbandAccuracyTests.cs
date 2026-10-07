using WaveOptics.Abstractions;
using WaveOptics.Effects;
using WaveOptics.Optics;

namespace WaveOptics.Tests.Psf;

public sealed class BroadbandAccuracyTests
{
    const int DenseSamples = 61;

    static PsfSpecification Specification(WavefrontAberration aberration)
        => new(512, 128, 63 * 2 + 1, SpectralPlan.ReferenceWavelength, 8, 4, ApertureShape.Circular, 6, 0, 0, aberration);

    static double[] DenseKernel(in PsfSpecification specification, double center)
    {
        var sampler = new FraunhoferKernelSampler();
        var area = specification.KernelSize * specification.KernelSize;
        var kernel = new double[area];
        var values = new double[area];
        var weights = new double[DenseSamples];
        var sum = 0d;
        for (var index = 0; index < DenseSamples; index++)
        {
            var offset = -SpectralPlan.BandSpan + 2d * SpectralPlan.BandSpan * index / (DenseSamples - 1);
            weights[index] = Math.Exp(-0.5 * offset * offset);
            sum += weights[index];
        }

        for (var index = 0; index < DenseSamples; index++)
        {
            var offset = -SpectralPlan.BandSpan + 2d * SpectralPlan.BandSpan * index / (DenseSamples - 1);
            var wavelength = center + SpectralPlan.BandSigma * offset;
            var phaseScale = SpectralPlan.ReferenceWavelength / wavelength;
            Assert.True(sampler.TryComputeIntensity(in specification, specification.PupilDiameterSamples * phaseScale, phaseScale, [1d]));
            Assert.True(sampler.TrySampleKernel(in specification, 1d, values));
            for (var element = 0; element < area; element++)
                kernel[element] += weights[index] / sum * values[element];
        }

        return kernel;
    }

    static double Distance(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
    {
        var sum = 0d;
        for (var index = 0; index < left.Length; index++)
            sum += Math.Abs(left[index] - right[index]);
        return sum;
    }

    public static TheoryData<string> Names => ["defocus", "coma", "spherical"];

    static WavefrontAberration Aberration(string name)
        => name switch
        {
            "defocus" => new WavefrontAberration(defocusWaves: 3),
            "coma" => new WavefrontAberration(comaHorizontalWaves: 1),
            _ => new WavefrontAberration(sphericalWaves: -1, defocusWaves: 0.5),
        };

    [Theory]
    [MemberData(nameof(Names))]
    public void TheQualityTiersApproachTheDenseBroadbandKernelInTheirOwnOrder(string name)
    {
        var specification = Specification(Aberration(name));
        var area = specification.KernelSize * specification.KernelSize;
        var sampler = new ChromaticKernelSampler();
        double[] distances = new double[3];
        foreach (var quality in Enum.GetValues<WaveOpticsQuality>())
        {
            var red = new double[area];
            var green = new double[area];
            var blue = new double[area];
            Assert.True(sampler.TrySample(in specification, WaveOpticsColorMode.Broadband, quality, red, green, blue));
            distances[(int)quality] = Math.Max(Distance(green, DenseKernel(in specification, SpectralPlan.GreenWavelength)), Distance(blue, DenseKernel(in specification, SpectralPlan.BlueWavelength)));
        }

        Assert.True(distances[0] > distances[1], $"{name}: {string.Join(" ", distances)}");
        Assert.True(distances[1] > distances[2], $"{name}: {string.Join(" ", distances)}");
        Assert.InRange(distances[0], 0.06, 0.1);
        Assert.InRange(distances[1], 0.025, 0.07);
        Assert.InRange(distances[2], 0.01, 0.03);
    }
}
