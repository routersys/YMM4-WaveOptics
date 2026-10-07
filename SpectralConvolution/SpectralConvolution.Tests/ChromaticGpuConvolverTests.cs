using System.Runtime.InteropServices;
using ComputeWeave;

namespace SpectralConvolution.Tests;

[Collection("Direct3D12")]
public sealed class ChromaticGpuConvolverTests
{
    sealed record GpuRun(byte[] Output, float[] Store, uint[] Report, GpuTileJob Job, Int2[] Samples);

    static GraphicsDevice HardwareOrDefault() => GraphicsDevice.GetDefault();

    static Int2[] Samples(in TilePlan plan)
    {
        var width = plan.RegionWidth;
        var height = plan.RegionHeight;
        var valid = plan.ValidSize;
        List<Int2> samples =
        [
            new(0, 0), new(width - 1, 0), new(0, height - 1), new(width - 1, height - 1), new(width / 2, height / 2),
            new(Math.Min(valid - 1, width - 1), Math.Min(valid - 1, height - 1)), new(Math.Min(valid, width - 1), Math.Min(valid, height - 1)),
        ];
        var random = new Random(width * 31 + height);
        while (samples.Count < GpuTileConvolver.MaximumSamples)
            samples.Add(new Int2(random.Next(width), random.Next(height)));
        return [.. samples];
    }

    static GpuRun Run(GraphicsDevice device, GpuTileConvolver convolver, ChromaticConvolutionScene scene, float gain = 1f)
    {
        var plan = scene.Plan;
        var samples = Samples(plan);
        convolver.Upload(scene.Spectrum);
        var job = convolver.Prepare(plan, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, gain, samples, true, scene.Light, true);
        using var source = device.AllocateReadWriteTexture2D<Bgra32, Float4>(scene.SourceWidth, scene.SourceHeight);
        source.CopyFrom(MemoryMarshal.Cast<byte, Bgra32>(scene.Source));
        using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(plan.RegionWidth, plan.RegionHeight);
        using (var context = device.CreateComputeContext())
        {
            GpuTileConvolver.Record(
                in context, source, output, convolver.Tiles, convolver.Twiddles, convolver.Spectrum,
                convolver.Report, convolver.Samples, convolver.StoreFor(job), job);
        }

        var report = convolver.ReadReport(job).ToArray();
        var store = new Float4[plan.RegionWidth * plan.RegionHeight];
        convolver.ReadStore(job, store);
        var pixels = new Bgra32[plan.RegionWidth * plan.RegionHeight];
        output.CopyTo(pixels);
        return new GpuRun(MemoryMarshal.Cast<Bgra32, byte>(pixels).ToArray(), MemoryMarshal.Cast<Float4, float>(store).ToArray(), report, job, samples);
    }

    static GpuRun Run(GraphicsDevice device, ChromaticConvolutionScene scene, float gain = 1f)
    {
        using var convolver = new GpuTileConvolver(device);
        return Run(device, convolver, scene, gain);
    }

    static ChromaticConvolutionScene Scene(int size, int radius, int width, int height, int margin, int seed, LightOptions light = default)
        => ChromaticConvolutionScene.Create(ConvolutionScene.RandomSource(width, height, seed, 0.45), width, height, margin, radius, size, light);

    [Theory]
    [InlineData(64, 5, 150, 97, 11)]
    [InlineData(128, 23, 140, 90, 24)]
    [InlineData(256, 40, 110, 70, 44)]
    [InlineData(512, 3, 1100, 560, 8)]
    [InlineData(512, 63, 600, 300, 64)]
    public void TheResultStaysWithinTheBoundOfTheExactCorrelationOfEachChannel(int size, int radius, int width, int height, int margin)
    {
        var scene = Scene(size, radius, width, height, margin, size + radius);

        var run = Run(HardwareOrDefault(), scene);

        Assert.True(scene.Plan.TileCount > 1 || size == 512);
        Assert.InRange(scene.WorstRatio(run.Store, ConvolutionBound.GpuOperationError), 0d, 1d);
    }

    [Theory]
    [InlineData(true, 0f, 0f)]
    [InlineData(true, 0.8f, 20f)]
    public void TheLinearLightResultStaysWithinTheBound(bool linear, float threshold, float boost)
    {
        var scene = Scene(128, 9, 130, 90, 12, 21, new LightOptions(linear, false, threshold, boost));

        var run = Run(HardwareOrDefault(), scene);

        Assert.InRange(scene.WorstRatio(run.Store, ConvolutionBound.GpuOperationError), 0d, 1d);
    }

    [Fact]
    public void TheResultMatchesTheCpuResultWithinTheBoundsOfBoth()
    {
        var scene = Scene(128, 15, 200, 120, 20, 7);
        var cpu = new float[scene.RegionLength];
        var cpuOutput = new byte[scene.RegionLength];
        using (var convolver = new CpuTileConvolver(2))
            convolver.Convolve(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, scene.Plan, cpuOutput, 1f, cpu);

        var run = Run(HardwareOrDefault(), scene);

        var tolerance = ConvolutionBound.ChromaticAbsolute(128, ConvolutionBound.GpuOperationError, 1e4) + ConvolutionBound.ChromaticAbsolute(128, ConvolutionBound.CpuOperationError, 1e4);
        for (var index = 0; index < cpu.Length; index++)
            Assert.True(Math.Abs(cpu[index] - run.Store[index]) <= tolerance, $"{index}: {cpu[index]} {run.Store[index]}");
    }

    [Fact]
    public void EveryMeasurementOfTheCheckPasses()
    {
        var scene = Scene(512, 20, 700, 400, 24, 3);

        var run = Run(HardwareOrDefault(), scene);

        var measurements = new ConvolutionMeasurement[GpuTileCheck.MeasurementCount(run.Samples.Length)];
        GpuTileCheck.Evaluate(run.Report, run.Job, run.Samples, scene.Spectrum.Red.Kernel, scene.Spectrum.Green.Kernel, scene.Spectrum.Blue.Kernel, measurements);
        Assert.All(measurements, measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    [Fact]
    public void ACheckWithTheWrongKernelOfOneChannelFails()
    {
        var scene = Scene(128, 12, 160, 100, 16, 11);
        var run = Run(HardwareOrDefault(), scene);
        var measurements = new ConvolutionMeasurement[GpuTileCheck.MeasurementCount(run.Samples.Length)];

        GpuTileCheck.Evaluate(run.Report, run.Job, run.Samples, scene.Spectrum.Red.Kernel, scene.Spectrum.Green.Kernel, scene.Spectrum.Green.Kernel, measurements);

        Assert.Contains(measurements, measurement => measurement.Name == "spot.blue" && !measurement.Passes);
        Assert.All(measurements.Where(measurement => measurement.Name is "spot.red" or "spot.green" or "spot.alpha"), measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    [Fact]
    public void TwoRunsAgreeBitForBit()
    {
        var scene = Scene(128, 9, 300, 170, 16, 5);

        var first = Run(HardwareOrDefault(), scene, 1.5f);
        var second = Run(HardwareOrDefault(), scene, 1.5f);

        Assert.Equal(first.Output, second.Output);
        Assert.Equal(first.Store, second.Store);
        Assert.Equal(first.Report, second.Report);
    }

    [Fact]
    public void AMonochromeRunAfterAChromaticOneUsesTheMonochromeSpectrum()
    {
        var device = HardwareOrDefault();
        var chromatic = Scene(128, 9, 150, 100, 16, 5);
        var single = ConvolutionScene.Create(ConvolutionScene.RandomSource(150, 100, 5, 0.45), 150, 100, 16, 9, 128);
        using var convolver = new GpuTileConvolver(device);

        _ = Run(device, convolver, chromatic);
        convolver.Upload(single.Spectrum);
        var job = convolver.Prepare(single.Plan, single.SourceX, single.SourceY, single.SourceWidth, single.SourceHeight, 1f, [], false);

        Assert.False(job.Chromatic);
        Assert.Throws<InvalidOperationException>(() => convolver.Prepare(single.Plan, single.SourceX, single.SourceY, single.SourceWidth, single.SourceHeight, 1f, [], false, default, true));
    }
}
