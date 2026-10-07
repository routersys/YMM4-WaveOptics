using SpectralConvolution;

namespace WaveOptics.Optics;

internal sealed class FraunhoferKernelSampler
{
    readonly ComplexLines grid = new();
    readonly ComplexLines line = new();
    double[] intensity = [];
    bool[] sampledColumns = [];

    public bool TrySample(in PsfSpecification specification, Span<double> kernel)
        => TryComputeIntensity(in specification, specification.PupilDiameterSamples, 1d, [1d])
            && TrySampleKernel(in specification, 1d, kernel);

    public bool TryComputeIntensity(in PsfSpecification specification, double pupilDiameter, double phaseScale, ReadOnlySpan<double> positionScales)
    {
        var gridSize = specification.PupilGridSize;
        EnsureCapacity(gridSize);

        var real = grid.Real;
        var imaginary = grid.Imaginary;
        var columnReal = line.Real;
        var columnImaginary = line.Imaginary;
        var center = gridSize / 2;
        var reach = (int)(pupilDiameter / 2d);
        var firstRow = Math.Max(center - reach, 0);
        var lastRow = Math.Min(center + reach, gridSize - 1);
        if (FraunhoferPsfGenerator.BuildPupil(in specification, pupilDiameter, phaseScale, real, imaginary, firstRow, lastRow) == 0)
            return false;

        for (var row = firstRow; row <= lastRow; row++)
            FastFourierTransform.Forward(real.Slice(row * gridSize, gridSize), imaginary.Slice(row * gridSize, gridSize));

        MarkSampledColumns(in specification, center, positionScales);
        for (var shiftedColumn = 0; shiftedColumn < gridSize; shiftedColumn++)
        {
            if (!sampledColumns[shiftedColumn])
                continue;

            var column = (shiftedColumn + center) % gridSize;
            columnReal[..firstRow].Clear();
            columnImaginary[..firstRow].Clear();
            for (var row = firstRow; row <= lastRow; row++)
            {
                columnReal[row] = real[row * gridSize + column];
                columnImaginary[row] = imaginary[row * gridSize + column];
            }
            columnReal.Slice(lastRow + 1, gridSize - lastRow - 1).Clear();
            columnImaginary.Slice(lastRow + 1, gridSize - lastRow - 1).Clear();

            FastFourierTransform.Forward(columnReal[..gridSize], columnImaginary[..gridSize]);
            var split = gridSize - center;
            for (var row = 0; row < split; row++)
            {
                var a = columnReal[row];
                var b = columnImaginary[row];
                intensity[(row + center) * gridSize + shiftedColumn] = a * a + b * b;
            }
            for (var row = split; row < gridSize; row++)
            {
                var a = columnReal[row];
                var b = columnImaginary[row];
                intensity[(row - split) * gridSize + shiftedColumn] = a * a + b * b;
            }
        }

        return true;
    }

    public bool TrySampleKernel(in PsfSpecification specification, double positionScale, Span<double> kernel)
    {
        var energy = FraunhoferPsfGenerator.SampleKernel(in specification, intensity, kernel, positionScale);
        if (!double.IsFinite(energy) || energy <= 0)
            return false;

        for (var index = 0; index < kernel.Length; index++)
            kernel[index] /= energy;
        return true;
    }

    void MarkSampledColumns(in PsfSpecification specification, int center, ReadOnlySpan<double> positionScales)
    {
        var gridSize = specification.PupilGridSize;
        var focalPlaneSamplePitch = FraunhoferPsfGenerator.FocalPlaneSamplePitch(in specification);
        var kernelRadius = specification.KernelSize / 2;
        Array.Clear(sampledColumns, 0, gridSize);
        foreach (var positionScale in positionScales)
        {
            for (var index = 0; index < specification.KernelSize; index++)
            {
                var position = FraunhoferPsfGenerator.SamplePosition(index, kernelRadius, center, specification.SensorPixelPitchMicrometers, focalPlaneSamplePitch, positionScale);
                if (position < 0 || position > gridSize - 1)
                    continue;

                var lower = (int)Math.Floor(position);
                sampledColumns[lower] = true;
                sampledColumns[Math.Min(lower + 1, gridSize - 1)] = true;
            }
        }
    }

    void EnsureCapacity(int gridSize)
    {
        var area = gridSize * gridSize;
        grid.Ensure(area);
        line.Ensure(gridSize);
        if (intensity.Length < area)
            intensity = new double[area];
        if (sampledColumns.Length < gridSize)
            sampledColumns = new bool[gridSize];
    }
}
