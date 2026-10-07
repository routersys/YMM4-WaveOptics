namespace SpectralConvolution;

internal sealed class ComplexLines
{
    const int Period = 512;
    const int Offset = 192;
    const int MinimumSeparation = 64;

    double[] storage = [];
    int length;
    int separation;

    public Span<double> Real => storage.AsSpan(0, length);

    public Span<double> Imaginary => storage.AsSpan(length + separation, length);

    public void Ensure(int count)
    {
        if (length >= count)
            return;

        length = count;
        separation = (Offset - count % Period + Period) % Period;
        if (separation < MinimumSeparation)
            separation += Period;
        storage = new double[count * 2 + separation];
    }
}
