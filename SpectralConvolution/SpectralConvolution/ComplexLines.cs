namespace SpectralConvolution;

internal sealed class ComplexLines
{
    public const int PageBytes = 4096;
    public const int StaggerBytes = PageBytes * 3 / 8;
    public const int MinimumSeparation = 64;

    const int Period = PageBytes / sizeof(double);
    const int Offset = StaggerBytes / sizeof(double);

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
