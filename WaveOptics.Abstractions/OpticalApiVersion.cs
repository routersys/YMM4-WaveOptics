namespace WaveOptics.Abstractions;

public readonly record struct OpticalApiVersion(int Major, int Minor)
{
    public static OpticalApiVersion Current { get; } = new(1, 0);
}
