namespace WaveOptics.Tests;

internal readonly record struct Bgra(byte Blue, byte Green, byte Red, byte Alpha)
{
    public static Bgra Transparent => default;

    public static Bgra Opaque(byte blue, byte green, byte red) => new(blue, green, red, byte.MaxValue);

    public Bgra Premultiplied() => new(Premultiply(Blue), Premultiply(Green), Premultiply(Red), Alpha);

    byte Premultiply(byte value) => (byte)Math.Round(value * (double)Alpha / byte.MaxValue, MidpointRounding.AwayFromZero);
}
