using ComputeWeave;

namespace SpectralConvolution;

internal sealed class KernelSpectrum
{
    double[] kernel = [];
    double[] real = [];
    double[] imaginary = [];
    Float2[] twiddles = [];
    Float2[] spectrum = [];

    public int Size { get; private set; }

    public int Radius { get; private set; }

    public int KernelSize => Radius * 2 + 1;

    public ReadOnlySpan<double> Kernel => kernel.AsSpan(0, Size == 0 ? 0 : KernelSize * KernelSize);

    public ReadOnlySpan<Float2> Twiddles => twiddles.AsSpan(0, Size / 2);

    public ReadOnlySpan<Float2> Spectrum => spectrum.AsSpan(0, Size * Size);

    public void Update(ReadOnlySpan<double> values, int radius, int size)
    {
        _ = TilePlan.Create(size, radius, 0, 0, 1, 1);
        var kernelSize = radius * 2 + 1;
        var kernelArea = kernelSize * kernelSize;
        if (values.Length != kernelArea)
            throw new ArgumentException(null, nameof(values));

        var sum = 0d;
        foreach (var value in values)
        {
            if (!double.IsFinite(value) || value < 0d)
                throw new ArgumentOutOfRangeException(nameof(values));
            sum += value;
        }
        if (!double.IsFinite(sum) || sum <= 0d)
            throw new ArgumentOutOfRangeException(nameof(values));

        var area = size * size;
        Size = 0;
        Radius = 0;
        if (kernel.Length < kernelArea)
            kernel = new double[kernelArea];
        if (real.Length < area)
        {
            real = new double[area];
            imaginary = new double[area];
            spectrum = new Float2[area];
        }
        if (twiddles.Length < size / 2)
            twiddles = new Float2[size / 2];

        for (var index = 0; index < kernelArea; index++)
            kernel[index] = values[index] / sum;

        for (var index = 0; index < size / 2; index++)
        {
            var (sin, cos) = Math.SinCos(-2d * Math.PI * index / size);
            twiddles[index] = new Float2(Flush((float)cos), Flush((float)sin));
        }

        real.AsSpan(0, area).Clear();
        imaginary.AsSpan(0, area).Clear();
        var mask = size - 1;
        for (var offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
                real[(-offsetY & mask) * size + (-offsetX & mask)] = kernel[(offsetY + radius) * kernelSize + offsetX + radius];
        }

        FastFourierTransform.Forward2D(real, imaginary, size, size);
        for (var frequencyY = 0; frequencyY < size; frequencyY++)
        {
            for (var frequencyX = 0; frequencyX < size; frequencyX++)
            {
                var index = frequencyY * size + frequencyX;
                spectrum[frequencyX * size + frequencyY] = new Float2(Flush((float)real[index]), Flush((float)imaginary[index]));
            }
        }

        Size = size;
        Radius = radius;
    }

    public static float Flush(float value) => float.IsSubnormal(value) ? float.CopySign(0f, value) : value;
}
