using ComputeWeave;

namespace SpectralConvolution;

internal sealed class ChromaticKernelSpectrum
{
    public const int ChannelCount = 3;

    readonly KernelSpectrum red = new();
    readonly KernelSpectrum green = new();
    readonly KernelSpectrum blue = new();
    readonly UpdateJob updates = new();
    bool ready;

    public KernelSpectrum Red => red;

    public KernelSpectrum Green => green;

    public KernelSpectrum Blue => blue;

    public int Size => ready ? green.Size : 0;

    public int Radius => Size == 0 ? 0 : green.Radius;

    public int HalfColumns => HalfColumnsOf(Size);

    public static int HalfColumnsOf(int size) => size / 2 + 1;

    public ReadOnlySpan<Float2> Twiddles => green.Twiddles;

    public void Update(ReadOnlyMemory<double> redValues, ReadOnlyMemory<double> greenValues, ReadOnlyMemory<double> blueValues, int radius, int size)
    {
        ready = false;
        updates.Prepare(red, green, blue, redValues, greenValues, blueValues, radius, size);
        WorkerPool.Shared.Run(updates, ChannelCount);
        ready = true;
    }

    public ReadOnlySpan<Float2> HalfSpectrum(ChromaticChannel channel)
    {
        var size = Size;
        if (size == 0)
            return default;

        var full = channel switch
        {
            ChromaticChannel.Red => red.Spectrum,
            ChromaticChannel.Green => green.Spectrum,
            ChromaticChannel.Blue => blue.Spectrum,
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };
        return full[..(HalfColumns * size)];
    }

    public void CopyHalfSpectra(Span<Float2> destination)
    {
        var length = HalfColumns * Size;
        if (Size == 0 || destination.Length < ChannelCount * length)
            throw new ArgumentException(null, nameof(destination));

        for (var channel = 0; channel < ChannelCount; channel++)
            HalfSpectrum((ChromaticChannel)channel).CopyTo(destination.Slice(channel * length, length));
    }

}

internal sealed class UpdateJob : IParallelJob
{
    readonly KernelSpectrum[] spectra = new KernelSpectrum[ChromaticKernelSpectrum.ChannelCount];
    readonly ReadOnlyMemory<double>[] values = new ReadOnlyMemory<double>[ChromaticKernelSpectrum.ChannelCount];
    int radius;
    int size;

    public void Prepare(KernelSpectrum red, KernelSpectrum green, KernelSpectrum blue, ReadOnlyMemory<double> redValues, ReadOnlyMemory<double> greenValues, ReadOnlyMemory<double> blueValues, int kernelRadius, int spectrumSize)
    {
        spectra[0] = red;
        spectra[1] = green;
        spectra[2] = blue;
        values[0] = redValues;
        values[1] = greenValues;
        values[2] = blueValues;
        radius = kernelRadius;
        size = spectrumSize;
    }

    public void Execute(int index, int worker) => spectra[index].Update(values[index].Span, radius, size);
}

internal static class ChromaticChannels
{
    public const int Red = 0;
    public const int Green = 1;
    public const int Blue = 2;
}

internal enum ChromaticChannel
{
    Red = ChromaticChannels.Red,
    Green = ChromaticChannels.Green,
    Blue = ChromaticChannels.Blue,
}
