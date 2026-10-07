using SpectralConvolution;
using WaveOptics.Abstractions;
using WaveOptics.Effects;

namespace WaveOptics.Optics;

internal sealed class ChromaticKernelSampler
{
    static readonly WorkerScratch[] Scratches = [.. Enumerable.Range(0, WorkerPool.Shared.Parallelism).Select(static _ => new WorkerScratch())];

    readonly NodeJob job = new();
    double[][] contributions = [];

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
        var items = 0;
        foreach (var nodes in bands)
            items += nodes.Length;
        if (contributions.Length < items)
            Array.Resize(ref contributions, items);
        var item = 0;
        for (var channel = 0; channel < SpectralPlan.ChannelCount; channel++)
        {
            for (var node = 0; node < bands[channel].Length; node++, item++)
            {
                if (contributions[item] is null || contributions[item].Length < red.Length)
                    contributions[item] = new double[red.Length];
                job.Assign(item, channel, node);
            }
        }

        job.Prepare(bands, in reference, contributions, red.Length);
        WorkerPool.Shared.Run(job, items);
        if (job.Failed)
            return false;

        item = 0;
        for (var channel = 0; channel < SpectralPlan.ChannelCount; channel++)
        {
            var kernel = channel switch { 0 => red, 1 => green, _ => blue };
            for (var node = 0; node < bands[channel].Length; node++, item++)
            {
                var values = contributions[item];
                if (node == 0)
                {
                    values.AsSpan(0, kernel.Length).CopyTo(kernel);
                    continue;
                }

                for (var index = 0; index < kernel.Length; index++)
                    kernel[index] += values[index];
            }
        }

        return true;
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

    sealed class WorkerScratch
    {
        public readonly FraunhoferKernelSampler Sampler = new();
        public readonly double[] Scales = new double[SpectralPlan.MaximumSamples];
        public double[] Kernel = [];
    }

    sealed class NodeJob : IParallelJob
    {
        readonly int[] itemChannels = new int[SpectralPlan.ChannelCount * SpectralPlan.MaximumNodes];
        readonly int[] itemNodes = new int[SpectralPlan.ChannelCount * SpectralPlan.MaximumNodes];
        SpectralNode[][] bands = [];
        PsfSpecification reference;
        double[][] outputs = [];
        int area;
        int failed;

        public bool Failed => Volatile.Read(ref failed) != 0;

        public void Assign(int item, int channel, int node)
        {
            itemChannels[item] = channel;
            itemNodes[item] = node;
        }

        public void Prepare(SpectralNode[][] plan, in PsfSpecification specification, double[][] buffers, int kernelArea)
        {
            bands = plan;
            reference = specification;
            outputs = buffers;
            area = kernelArea;
            failed = 0;
        }

        public void Execute(int item, int worker)
        {
            if (Failed)
                return;

            var node = bands[itemChannels[item]][itemNodes[item]];
            var scratch = Scratches[worker];
            if (scratch.Kernel.Length < area)
                scratch.Kernel = new double[area];
            var count = node.Samples.Length;
            for (var index = 0; index < count; index++)
                scratch.Scales[index] = node.Wavelength / node.Samples[index].Wavelength;
            var phaseScale = SpectralPlan.ReferenceWavelength / node.Wavelength;
            if (!scratch.Sampler.TryComputeIntensity(in reference, reference.PupilDiameterSamples * phaseScale, phaseScale, scratch.Scales.AsSpan(0, count)))
            {
                Volatile.Write(ref failed, 1);
                return;
            }

            var output = outputs[item].AsSpan(0, area);
            output.Clear();
            var values = scratch.Kernel.AsSpan(0, area);
            foreach (var sample in node.Samples)
            {
                if (!scratch.Sampler.TrySampleKernel(in reference, node.Wavelength / sample.Wavelength, values))
                {
                    Volatile.Write(ref failed, 1);
                    return;
                }

                for (var index = 0; index < area; index++)
                    output[index] += sample.Weight * values[index];
            }
        }
    }
}
