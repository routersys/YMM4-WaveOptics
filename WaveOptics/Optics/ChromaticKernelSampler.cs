using WaveOptics.Abstractions;
using WaveOptics.Effects;

namespace WaveOptics.Optics;

internal sealed class ChromaticKernelSampler
{
    readonly FraunhoferKernelSampler sampler = new();
    double[] scratch = [];
    readonly double[] scales = new double[SpectralPlan.MaximumSamples];

    public bool TrySample(
        in PsfSpecification reference,
        WaveOpticsColorMode mode,
        WaveOpticsQuality quality,
        Span<double> red,
        Span<double> green,
        Span<double> blue)
    {
        if (red.Length != green.Length || red.Length != blue.Length)
            throw new ArgumentException(null, nameof(green));

        var bands = SpectralPlan.For(mode, quality, IsAberrationFree(reference.Aberration));
        if (scratch.Length < red.Length)
            scratch = new double[red.Length];
        for (var channel = 0; channel < SpectralPlan.ChannelCount; channel++)
        {
            var kernel = channel switch { 0 => red, 1 => green, _ => blue };
            kernel.Clear();
            foreach (var node in bands[channel])
            {
                if (!ComputeNodeIntensity(in reference, node))
                    return false;

                foreach (var sample in node.Samples)
                {
                    var scale = node.Wavelength / sample.Wavelength;
                    var values = scratch.AsSpan(0, kernel.Length);
                    if (!sampler.TrySampleKernel(in reference, scale, values))
                        return false;
                    for (var index = 0; index < kernel.Length; index++)
                        kernel[index] += sample.Weight * values[index];
                }
            }
        }

        return true;
    }

    bool ComputeNodeIntensity(in PsfSpecification reference, SpectralNode node)
    {
        var count = node.Samples.Length;
        for (var index = 0; index < count; index++)
            scales[index] = node.Wavelength / node.Samples[index].Wavelength;
        var phaseScale = SpectralPlan.ReferenceWavelength / node.Wavelength;
        return sampler.TryComputeIntensity(in reference, reference.PupilDiameterSamples * phaseScale, phaseScale, scales.AsSpan(0, count));
    }

    static bool IsAberrationFree(WavefrontAberration aberration)
        => aberration.DefocusWaves == 0d
            && aberration.AstigmatismVerticalWaves == 0d
            && aberration.AstigmatismObliqueWaves == 0d
            && aberration.ComaHorizontalWaves == 0d
            && aberration.ComaVerticalWaves == 0d
            && aberration.TrefoilHorizontalWaves == 0d
            && aberration.TrefoilVerticalWaves == 0d
            && aberration.SphericalWaves == 0d;
}
