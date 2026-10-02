using SpectralConvolution;

namespace WaveOptics.Optics;

internal sealed class FraunhoferKernelSampler
{
    double[] real = [];
    double[] imaginary = [];
    double[] intensity = [];
    double[] columnReal = [];
    double[] columnImaginary = [];
    bool[] sampledColumns = [];

    public bool TrySample(in PsfSpecification specification, Span<double> kernel)
    {
        var gridSize = specification.PupilGridSize;
        EnsureCapacity(gridSize);

        var center = gridSize / 2;
        var reach = (int)(specification.PupilDiameterSamples / 2d);
        var firstRow = Math.Max(center - reach, 0);
        var lastRow = Math.Min(center + reach, gridSize - 1);
        if (FraunhoferPsfGenerator.BuildPupil(in specification, real, imaginary, firstRow, lastRow) == 0)
            return false;

        for (var row = firstRow; row <= lastRow; row++)
            FastFourierTransform.Forward(real.AsSpan(row * gridSize, gridSize), imaginary.AsSpan(row * gridSize, gridSize));

        MarkSampledColumns(in specification, center);
        for (var shiftedColumn = 0; shiftedColumn < gridSize; shiftedColumn++)
        {
            if (!sampledColumns[shiftedColumn])
                continue;

            var column = (shiftedColumn + center) % gridSize;
            for (var row = 0; row < gridSize; row++)
            {
                var open = row >= firstRow && row <= lastRow;
                columnReal[row] = open ? real[row * gridSize + column] : 0d;
                columnImaginary[row] = open ? imaginary[row * gridSize + column] : 0d;
            }

            FastFourierTransform.Forward(columnReal.AsSpan(0, gridSize), columnImaginary.AsSpan(0, gridSize));
            for (var row = 0; row < gridSize; row++)
            {
                var a = columnReal[row];
                var b = columnImaginary[row];
                intensity[(row + center) % gridSize * gridSize + shiftedColumn] = a * a + b * b;
            }
        }

        var energy = FraunhoferPsfGenerator.SampleKernel(in specification, intensity, kernel);
        if (!double.IsFinite(energy) || energy <= 0)
            return false;

        for (var index = 0; index < kernel.Length; index++)
            kernel[index] /= energy;
        return true;
    }

    void MarkSampledColumns(in PsfSpecification specification, int center)
    {
        var gridSize = specification.PupilGridSize;
        var focalPlaneSamplePitch = FraunhoferPsfGenerator.FocalPlaneSamplePitch(in specification);
        var kernelRadius = specification.KernelSize / 2;
        Array.Clear(sampledColumns, 0, gridSize);
        for (var index = 0; index < specification.KernelSize; index++)
        {
            var position = FraunhoferPsfGenerator.SamplePosition(index, kernelRadius, center, specification.SensorPixelPitchMicrometers, focalPlaneSamplePitch);
            if (position < 0 || position > gridSize - 1)
                continue;

            var lower = (int)Math.Floor(position);
            sampledColumns[lower] = true;
            sampledColumns[Math.Min(lower + 1, gridSize - 1)] = true;
        }
    }

    void EnsureCapacity(int gridSize)
    {
        var area = gridSize * gridSize;
        if (real.Length < area)
        {
            real = new double[area];
            imaginary = new double[area];
            intensity = new double[area];
        }
        if (columnReal.Length < gridSize)
        {
            columnReal = new double[gridSize];
            columnImaginary = new double[gridSize];
            sampledColumns = new bool[gridSize];
        }
    }
}
