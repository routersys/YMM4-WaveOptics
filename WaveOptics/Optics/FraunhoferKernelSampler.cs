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
        var center = gridSize / 2;
        var reach = (int)(pupilDiameter / 2d);
        var firstRow = Math.Max(center - reach, 0);
        var lastRow = Math.Min(center + reach, gridSize - 1);
        EnsureCapacity(gridSize, lastRow - firstRow + 1);
        if (FraunhoferPsfGenerator.BuildPupil(in specification, pupilDiameter, phaseScale, grid.Real, grid.Imaginary, firstRow, lastRow, firstRow) == 0)
            return false;

        for (var row = firstRow; row <= lastRow; row++)
        {
            var offset = (row - firstRow) * gridSize;
            FastFourierTransform.Forward(grid.Real.Slice(offset, gridSize), grid.Imaginary.Slice(offset, gridSize));
        }

        MarkSampledColumns(in specification, center, positionScales);
        for (var shiftedColumn = 0; shiftedColumn < gridSize; shiftedColumn++)
        {
            if (sampledColumns[shiftedColumn])
                TransformColumn(shiftedColumn, gridSize, firstRow, lastRow);
        }

        return true;
    }

    void TransformColumn(int shiftedColumn, int gridSize, int firstRow, int lastRow)
    {
        var center = gridSize / 2;
        var column = (shiftedColumn + center) % gridSize;
        GatherColumn(column, gridSize, firstRow, lastRow);

        var columnReal = line.Real[..gridSize];
        var columnImaginary = line.Imaginary[..gridSize];
        FastFourierTransform.Forward(columnReal, columnImaginary);
        var split = gridSize - center;
        var destination = intensity.AsSpan(shiftedColumn * gridSize, gridSize);
        FraunhoferPsfGenerator.Power(columnReal[..split], columnImaginary[..split], destination[center..]);
        FraunhoferPsfGenerator.Power(columnReal[split..], columnImaginary[split..], destination[..center]);
    }

    void GatherColumn(int column, int gridSize, int firstRow, int lastRow)
    {
        var real = grid.Real;
        var imaginary = grid.Imaginary;
        var columnReal = line.Real;
        var columnImaginary = line.Imaginary;
        columnReal[..firstRow].Clear();
        columnImaginary[..firstRow].Clear();
        for (var row = firstRow; row <= lastRow; row++)
        {
            var index = (row - firstRow) * gridSize + column;
            columnReal[row] = real[index];
            columnImaginary[row] = imaginary[index];
        }

        columnReal.Slice(lastRow + 1, gridSize - lastRow - 1).Clear();
        columnImaginary.Slice(lastRow + 1, gridSize - lastRow - 1).Clear();
    }

    public bool TrySampleKernel(in PsfSpecification specification, double positionScale, Span<double> kernel)
    {
        var energy = FraunhoferPsfGenerator.SampleKernelTransposed(in specification, intensity, kernel, positionScale);
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

    void EnsureCapacity(int gridSize, int pupilRows)
    {
        var area = gridSize * gridSize;
        grid.Ensure(pupilRows * gridSize);
        line.Ensure(gridSize);
        if (intensity.Length < area)
            intensity = new double[area];
        if (sampledColumns.Length < gridSize)
            sampledColumns = new bool[gridSize];
    }
}
