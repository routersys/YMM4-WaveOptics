using WaveOptics.Effects;

namespace WaveOptics.Optics;

internal readonly record struct SpectralSample(double Wavelength, double Weight);

internal readonly record struct SpectralNode(double Wavelength, SpectralSample[] Samples);

internal static class SpectralPlan
{
    public const double ReferenceWavelength = 550d;
    public const double RedWavelength = 610d;
    public const double GreenWavelength = 550d;
    public const double BlueWavelength = 465d;
    public const double BandSigma = 21d;
    public const double BandSpan = 2.5;
    public const int ChannelCount = 3;
    public const int MaximumSamples = 27;
    public const int MaximumNodes = 5;

    static readonly SpectralNode[][] Primaries =
    [
        [new(RedWavelength, [new(RedWavelength, 1d)])],
        [new(GreenWavelength, [new(GreenWavelength, 1d)])],
        [new(BlueWavelength, [new(BlueWavelength, 1d)])],
    ];

    static readonly SpectralNode[][][] Broadband =
    [
        Bands(1, 25),
        Bands(3, 9),
        Bands(5, 5),
    ];

    public static SpectralNode[][] For(WaveOpticsColorMode mode, WaveOpticsQuality quality, bool aberrationFree)
        => mode switch
        {
            WaveOpticsColorMode.Primaries => Primaries,
            WaveOpticsColorMode.Broadband => Broadband[aberrationFree ? (int)WaveOpticsQuality.Draft : (int)quality],
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    public static int SampleCount(SpectralNode[] nodes)
    {
        var count = 0;
        foreach (var node in nodes)
            count += node.Samples.Length;
        return count;
    }

    static SpectralNode[][] Bands(int nodeCount, int samplesPerNode)
        => [Band(RedWavelength, nodeCount, samplesPerNode), Band(GreenWavelength, nodeCount, samplesPerNode), Band(BlueWavelength, nodeCount, samplesPerNode)];

    static SpectralNode[] Band(double center, int nodeCount, int samplesPerNode)
    {
        var total = nodeCount * samplesPerNode;
        var samples = new SpectralSample[total];
        var sum = 0d;
        for (var index = 0; index < total; index++)
        {
            var offset = -BandSpan + (index + 0.5) * 2d * BandSpan / total;
            var weight = Math.Exp(-0.5 * offset * offset);
            samples[index] = new SpectralSample(center + BandSigma * offset, weight);
            sum += weight;
        }

        var nodes = new SpectralNode[nodeCount];
        for (var node = 0; node < nodeCount; node++)
        {
            var group = samples.AsSpan(node * samplesPerNode, samplesPerNode).ToArray();
            for (var index = 0; index < group.Length; index++)
                group[index] = group[index] with { Weight = group[index].Weight / sum };
            nodes[node] = new SpectralNode(group[samplesPerNode / 2].Wavelength, group);
        }

        return nodes;
    }
}
