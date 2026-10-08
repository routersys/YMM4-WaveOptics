using ComputeWeave;

namespace SpectralConvolution;

internal sealed class KernelSpectrum
{
    readonly ComplexLines rows = new();
    readonly ComplexLines column = new();
    double[] kernel = [];
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
        TilePlan.ValidateKernel(size, radius);
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

        var rowArea = kernelSize * size;
        Size = 0;
        Radius = 0;
        if (kernel.Length < kernelArea)
            kernel = new double[kernelArea];
        rows.Ensure(rowArea);
        column.Ensure(size);
        if (spectrum.Length < size * size)
            spectrum = new Float2[size * size];
        if (twiddles.Length < size / 2)
            twiddles = new Float2[size / 2];

        for (var index = 0; index < kernelArea; index++)
            kernel[index] = values[index] / sum;

        for (var index = 0; index < size / 2; index++)
        {
            var angle = -2d * Math.PI * index / size;
            twiddles[index] = new Float2(Flush((float)Math.Cos(angle)), Flush((float)Math.Sin(angle)));
        }

        var mask = size - 1;
        var rowReal = rows.Real;
        var rowImaginary = rows.Imaginary;
        for (var row = 0; row < kernelSize; row++)
        {
            var realLine = rowReal.Slice(row * size, size);
            var imaginaryLine = rowImaginary.Slice(row * size, size);
            realLine.Clear();
            imaginaryLine.Clear();
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
                realLine[(-offsetX) & mask] = kernel[row * kernelSize + offsetX + radius];

            FastFourierTransform.Forward(realLine, imaginaryLine);
        }

        var half = size / 2;
        var real = column.Real[..size];
        var imaginary = column.Imaginary[..size];
        for (var frequencyX = 0; frequencyX <= half; frequencyX++)
        {
            real.Clear();
            imaginary.Clear();
            for (var row = 0; row < kernelSize; row++)
            {
                var target = (radius - row) & mask;
                real[target] = rowReal[row * size + frequencyX];
                imaginary[target] = rowImaginary[row * size + frequencyX];
            }

            FastFourierTransform.Forward(real, imaginary);
            var stored = spectrum.AsSpan(frequencyX * size, size);
            for (var frequencyY = 0; frequencyY < size; frequencyY++)
                stored[frequencyY] = new Float2(Flush((float)real[frequencyY]), Flush((float)imaginary[frequencyY]));
        }

        for (var frequencyX = half + 1; frequencyX < size; frequencyX++)
        {
            var source = spectrum.AsSpan((size - frequencyX) * size, size);
            var stored = spectrum.AsSpan(frequencyX * size, size);
            for (var frequencyY = 0; frequencyY < size; frequencyY++)
            {
                var conjugate = source[(size - frequencyY) & mask];
                stored[frequencyY] = new Float2(conjugate.X, -conjugate.Y);
            }
        }

        Size = size;
        Radius = radius;
    }

    public static float Flush(float value) => float.IsSubnormal(value) ? float.CopySign(0f, value) : value;
}
