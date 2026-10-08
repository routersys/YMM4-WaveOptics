using ComputeWeave;
using SpectralConvolution;

namespace WaveOptics.Rendering;

internal static class SourceHashMath
{
    public const uint IndexMultiplier = 0x9E3779B9u;
    public const uint MixMultiplier = 0x85EBCA6Bu;
    public const uint AccumulateMultiplier = 0xC2B2AE35u;
    public const int FirstShift = 16;
    public const int SecondShift = 13;

    public static uint Quantize(Float4 color)
        => ByteColor.ToLevel(color.X) << 3 * ByteColor.Bits
            | ByteColor.ToLevel(color.Y) << 2 * ByteColor.Bits
            | ByteColor.ToLevel(color.Z) << ByteColor.Bits
            | ByteColor.ToLevel(color.W);

    public static uint Mixed(int index, uint quantized)
    {
        var mixed = (uint)index * IndexMultiplier ^ quantized * MixMultiplier;
        mixed ^= mixed >> FirstShift;
        mixed *= MixMultiplier;
        mixed ^= mixed >> SecondShift;
        return mixed;
    }
}
