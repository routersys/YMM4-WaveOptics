using ComputeGuard;
using ComputeWeave;
using SpectralConvolution;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace WaveOptics.Rendering;

internal static class WaveOpticsCompute
{
    private const int ColorBytes = 4;
    private const int StoredBytes = 16;

    public static ComputeGuardian Guardian { get; } = new(new ComputeGuardianOptions { Report = WaveOpticsTelemetry.Report });

    public static ComputeDevice DescribeDevice(IGraphicsDevicesAndContext devices)
    {
        var options = devices.D3D.Device.CheckFeatureOptions2();
        return ComputeDevice.FromAdapter(devices.DXGI.Adapter, options.UnifiedMemoryArchitecture);
    }

    public static ComputeWorkload Workload(int canvasWidth, int canvasHeight, int kernelRadius, bool chromatic = false)
        => new(canvasWidth, canvasHeight, GpuMemoryBytes(canvasWidth, canvasHeight, kernelRadius, chromatic));

    public static long GpuMemoryBytes(int canvasWidth, int canvasHeight, int kernelRadius, bool chromatic = false)
    {
        var plan = TilePlan.Create(kernelRadius, 0, 0, canvasWidth, canvasHeight);
        var pixels = (long)canvasWidth * canvasHeight;
        var kernelArea = (kernelRadius * 2 + 1) * (kernelRadius * 2 + 1);
        var layout = new GpuTileLayout(plan.GroupCount, GpuTileConvolver.MaximumSamples, kernelArea);
        return pixels * (ColorBytes * 2 + StoredBytes)
            + (long)plan.BatchTiles * plan.TileElements * TilePlan.ElementBytes
            + (chromatic ? (long)ChromaticKernelSpectrum.ChannelCount * ChromaticKernelSpectrum.HalfColumnsOf(plan.Size) * plan.Size : plan.TileElements) * sizeof(float) * 2
            + (long)plan.Size / 2 * sizeof(float) * 2
            + (long)layout.Length * sizeof(uint) * 2
            + WaveOpticsSettings.ScratchLength * sizeof(int) * 2;
    }

    public static ComputeFailure Classify(Exception exception, bool deviceLost)
    {
        if (deviceLost)
            return ComputeFailure.DeviceLost;
        return exception is GraphicsMemoryAllocationException or OutOfMemoryException
            ? ComputeFailure.ResourceExhausted
            : ComputeFailure.UnexpectedException;
    }

    public static ComputeCheck ToCheck(in ConvolutionMeasurement measurement)
        => new(measurement.Name, measurement.Measured, measurement.Reference, new ComputeTolerance(measurement.Tolerance / ComputeErrorModel.UnitInLastPlace, 1d, 0));

    public static IReadOnlyList<ComputeCheck> SelfTest(GraphicsDevice device)
    {
        var measurements = new ConvolutionMeasurement[SpectralSelfTest.MeasurementCount];
        var count = SpectralSelfTest.Run(device, WaveOpticsSettings.MaximumKernelRadius, measurements);
        var checks = new ComputeCheck[count];
        for (var index = 0; index < count; index++)
            checks[index] = ToCheck(measurements[index]);
        return checks;
    }
}
