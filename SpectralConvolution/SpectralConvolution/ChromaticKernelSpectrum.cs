using ComputeWeave;

namespace SpectralConvolution;

internal sealed class ChromaticKernelSpectrum
{
    public const int ChannelCount = 3;

    readonly KernelSpectrum red = new();
    readonly KernelSpectrum green = new();
    readonly KernelSpectrum blue = new();
    bool ready;

    public KernelSpectrum Red => red;

    public KernelSpectrum Green => green;

    public KernelSpectrum Blue => blue;

    public int Size => ready ? green.Size : 0;

    public int Radius => Size == 0 ? 0 : green.Radius;

    public int HalfColumns => HalfColumnsOf(Size);

    public static int HalfColumnsOf(int size) => size / 2 + 1;

    public ReadOnlySpan<Float2> Twiddles => green.Twiddles;

    public void Update(ReadOnlySpan<double> redValues, ReadOnlySpan<double> greenValues, ReadOnlySpan<double> blueValues, int radius, int size)
    {
        ready = false;
        red.Update(redValues, radius, size);
        green.Update(greenValues, radius, size);
        blue.Update(blueValues, radius, size);
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

internal enum ChromaticChannel
{
    Red,
    Green,
    Blue,
}
