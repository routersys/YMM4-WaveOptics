using ComputeWeave;

namespace SpectralConvolution;

internal static class ByteColor
{
    public const int Channels = 4;
    public const int Bits = 8;
    public const int Levels = 1 << Bits;
    public const int Maximum = Levels - 1;
    public const uint Mask = Maximum;
    public const float Scale = Maximum;
    public const double ScaleDouble = Maximum;
    public const float Rounding = 0.5f;

    public static uint ToLevel(float value) => (uint)(value * Scale + Rounding);

    public static uint Channel(uint packed, int index) => packed >> (index * Bits) & Mask;

    public static uint Pack(Float4 value)
        => ToLevel(value.X) | (ToLevel(value.Y) << Bits) | (ToLevel(value.Z) << (2 * Bits)) | (ToLevel(value.W) << (3 * Bits));
}
