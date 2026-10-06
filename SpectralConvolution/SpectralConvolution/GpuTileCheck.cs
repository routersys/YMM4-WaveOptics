using ComputeWeave;

namespace SpectralConvolution;

internal readonly record struct ConvolutionMeasurement(string Name, double Measured, double Reference, double Tolerance)
{
    public bool Passes => Math.Abs(Measured - Reference) <= Tolerance;
}

internal static class GpuTileCheck
{
    public const string NonFiniteName = "nonfinite";
    public const int GroupAdditions = 9;
    public const int GroupElements = TilePlan.GroupElements;
    public const int InputStatisticsOperations = 16;
    public static readonly double LightTransformError = Math.ScaleB(1d, -16);

    static readonly string[] SumNames = ["sum.red", "sum.green", "sum.blue", "sum.alpha"];
    static readonly string[] SpotNames = ["spot.red", "spot.green", "spot.blue", "spot.alpha"];

    public static int MeasurementCount(int sampleCount) => 1 + SumNames.Length + sampleCount * SpotNames.Length;

    public static void Evaluate(
        ReadOnlySpan<uint> report,
        in GpuTileJob job,
        ReadOnlySpan<Int2> samples,
        ReadOnlySpan<double> kernel,
        Span<ConvolutionMeasurement> measurements)
    {
        var plan = job.Plan;
        var layout = job.Layout;
        if (report.Length < layout.Length)
            throw new ArgumentException(null, nameof(report));
        if (samples.Length != job.SampleCount)
            throw new ArgumentException(null, nameof(samples));
        if (kernel.Length != job.KernelArea)
            throw new ArgumentException(null, nameof(kernel));
        if (measurements.Length < MeasurementCount(job.SampleCount))
            throw new ArgumentException(null, nameof(measurements));

        var light = job.Light;
        var floating = !light.IsDefault;
        var relative = ConvolutionBound.Relative(plan.Size, ConvolutionBound.GpuOperationError);
        var flushed = ConvolutionBound.FlushedOperations(plan.Size) * ConvolutionBound.SmallestNormal;
        measurements[0] = new ConvolutionMeasurement(NonFiniteName, report[GpuTileLayout.CountOffset], 0d, 0d);

        Span<ulong> totals = stackalloc ulong[4];
        Span<double> floatTotals = stackalloc double[4];
        Span<double> errors = stackalloc double[4];
        for (var tile = 0; tile < plan.TileCount; tile++)
        {
            var (redGreen, blueAlpha) = Norms(report, plan, tile, floating);
            var root = Math.Sqrt((double)plan.ValidWidth(tile) * plan.ValidHeight(tile));
            errors[0] += root * (relative * redGreen + flushed);
            errors[1] += root * (relative * redGreen + flushed);
            errors[2] += root * (relative * blueAlpha + flushed);
            errors[3] += root * (relative * blueAlpha + flushed);
            for (var group = tile * plan.GroupsPerTile; group < (tile + 1) * plan.GroupsPerTile; group++)
            {
                var offset = GpuTileLayout.StatisticsOffset + group * GpuTileLayout.StatisticsPerGroup;
                for (var channel = 0; channel < 4; channel++)
                {
                    if (floating)
                        floatTotals[channel] += BitConverter.UInt32BitsToSingle(report[offset + channel]);
                    else
                        totals[channel] += report[offset + channel];
                }
            }
        }

        var groupRounding = ConvolutionBound.Gamma(GroupAdditions, ConvolutionBound.GpuOperationError);
        var kernelRounding = ConvolutionBound.Gamma(job.KernelArea + 1, ConvolutionBound.DoubleRounding);
        var accumulation = ConvolutionBound.Gamma(plan.GroupCount, ConvolutionBound.DoubleRounding);
        var groupFlushes = (double)plan.GroupCount * (GroupElements - 1) * ConvolutionBound.SmallestNormal;
        var inputRounding = ConvolutionBound.Gamma(InputStatisticsOperations, ConvolutionBound.GpuOperationError);
        for (var channel = 0; channel < 4; channel++)
        {
            var measured = 0d;
            for (var group = 0; group < plan.GroupCount; group++)
                measured += BitConverter.UInt32BitsToSingle(report[layout.SumsOffset + group * GpuTileLayout.SumsPerGroup + channel]);

            var reference = floating ? floatTotals[channel] : totals[channel] / 255d;
            var magnitude = reference * (1d + kernelRounding) + errors[channel];
            var tolerance = errors[channel] + groupRounding * magnitude + groupFlushes
                + accumulation * ((1d + groupRounding) * magnitude + groupFlushes) + reference * kernelRounding;
            if (floating)
                tolerance += reference * inputRounding / (1d - inputRounding);
            measurements[1 + channel] = new ConvolutionMeasurement(SumNames[channel], measured, reference, tolerance);
        }

        Span<double> expected = stackalloc double[4];
        var referenceError = DirectCorrelation.ErrorBound(kernel.Length);
        for (var sample = 0; sample < job.SampleCount; sample++)
        {
            var position = samples[sample];
            var tile = plan.TileAt(plan.RegionX + position.X, plan.RegionY + position.Y);
            var (redGreen, blueAlpha) = Norms(report, plan, tile, floating);
            DirectCorrelation.Evaluate(report.Slice(layout.GatheredOffset + sample * job.KernelArea, job.KernelArea), kernel, light, expected);
            for (var channel = 0; channel < 4; channel++)
            {
                var norm = channel < 2 ? redGreen : blueAlpha;
                var measured = BitConverter.UInt32BitsToSingle(report[layout.SpotsOffset + sample * GpuTileLayout.ValuesPerSpot + channel]);
                var tolerance = relative * norm + flushed + referenceError;
                if (light.Linear)
                    tolerance += LightTransformError * norm;
                measurements[1 + SumNames.Length + sample * 4 + channel] = new ConvolutionMeasurement(SpotNames[channel], measured, expected[channel], tolerance);
            }
        }
    }

    public static (double RedGreen, double BlueAlpha) Norms(ReadOnlySpan<uint> report, in TilePlan plan, int tile, bool floating)
    {
        if (!floating)
            return Norms(report, plan, tile);

        var redGreenSquares = 0d;
        var blueAlphaSquares = 0d;
        for (var group = tile * plan.GroupsPerTile; group < (tile + 1) * plan.GroupsPerTile; group++)
        {
            var offset = GpuTileLayout.StatisticsOffset + group * GpuTileLayout.StatisticsPerGroup;
            redGreenSquares += BitConverter.UInt32BitsToSingle(report[offset + 4]);
            blueAlphaSquares += BitConverter.UInt32BitsToSingle(report[offset + 5]);
        }

        var rounding = ConvolutionBound.Gamma(InputStatisticsOperations, ConvolutionBound.GpuOperationError);
        var inflate = 1d / (1d - rounding);
        return (Math.Sqrt(redGreenSquares * inflate), Math.Sqrt(blueAlphaSquares * inflate));
    }

    public static (double RedGreen, double BlueAlpha) Norms(ReadOnlySpan<uint> report, in TilePlan plan, int tile)
    {
        var redGreen = 0UL;
        var blueAlpha = 0UL;
        for (var group = tile * plan.GroupsPerTile; group < (tile + 1) * plan.GroupsPerTile; group++)
        {
            var offset = GpuTileLayout.StatisticsOffset + group * GpuTileLayout.StatisticsPerGroup;
            redGreen += report[offset + 4];
            blueAlpha += report[offset + 5];
        }

        return (Math.Sqrt(redGreen) / 255d, Math.Sqrt(blueAlpha) / 255d);
    }
}
