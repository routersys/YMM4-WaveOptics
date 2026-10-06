using System.Runtime.InteropServices;
using ComputeWeave;

namespace SpectralConvolution.Tests;

[Collection("Direct3D12")]
public sealed class GpuTileConvolverTests
{
    sealed record GpuRun(byte[] Output, float[] Store, uint[] Report, GpuTileJob Job, Int2[] Samples, float[] Spectrum);

    static GraphicsDevice HardwareOrDefault() => GraphicsDevice.GetDefault();

    static GraphicsDevice Software() => GraphicsDevice.EnumerateDevices().First(device => !device.IsHardwareAccelerated);

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

    static GpuRun Run(GraphicsDevice device, ConvolutionScene scene, float gain = 1f, Func<float[], float[]>? tamper = null, Int2[]? chosenSamples = null)
    {
        using var convolver = new GpuTileConvolver(device);
        return Run(device, convolver, scene, gain, tamper, chosenSamples);
    }

    static GpuRun Run(GraphicsDevice device, GpuTileConvolver convolver, ConvolutionScene scene, float gain = 1f, Func<float[], float[]>? tamper = null, Int2[]? chosenSamples = null)
    {
        var plan = scene.Plan;
        var samples = chosenSamples ?? Samples(plan);
        convolver.Upload(scene.Spectrum);
        var spectrum = MemoryMarshal.Cast<Float2, float>(scene.Spectrum.Spectrum).ToArray();
        if (tamper is not null)
        {
            spectrum = tamper(spectrum);
            convolver.Spectrum.CopyFrom(MemoryMarshal.Cast<float, Float2>(spectrum));
        }

        var job = convolver.Prepare(plan, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, gain, samples, true, scene.Light);
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
        return new GpuRun(
            MemoryMarshal.Cast<Bgra32, byte>(pixels).ToArray(),
            MemoryMarshal.Cast<Float4, float>(store).ToArray(),
            report,
            job,
            samples,
            spectrum);
    }

    static ConvolutionMeasurement[] Measure(ConvolutionScene scene, GpuRun run)
    {
        var measurements = new ConvolutionMeasurement[GpuTileCheck.MeasurementCount(run.Samples.Length)];
        GpuTileCheck.Evaluate(run.Report, run.Job, run.Samples, scene.Spectrum.Kernel, measurements);
        return measurements;
    }

    static ConvolutionScene Scene(int size, int radius, int width, int height, int margin, int seed)
        => ConvolutionScene.Create(ConvolutionScene.RandomSource(width, height, seed, 0.45), width, height, margin, radius, size);

    [Theory]
    [InlineData(64, 5, 150, 97, 11)]
    [InlineData(128, 23, 140, 90, 24)]
    [InlineData(512, 3, 1100, 560, 8)]
    public void TheResultStaysWithinTheBoundOfTheExactCorrelation(int size, int radius, int width, int height, int margin)
    {
        var scene = Scene(size, radius, width, height, margin, size + radius);

        var run = Run(HardwareOrDefault(), scene);

        Assert.True(scene.Plan.TileCount > 1);
        Assert.InRange(scene.WorstRatio(run.Store, ConvolutionBound.GpuOperationError), 0d, 1d);
    }

    [Fact]
    public void SeveralBatchesProduceEveryTile()
    {
        var scene = Scene(512, 3, 1100, 560, 8, 2);

        Assert.True(scene.Plan.TileCount > scene.Plan.BatchTiles);
        var run = Run(HardwareOrDefault(), scene);

        Assert.InRange(scene.WorstRatio(run.Store, ConvolutionBound.GpuOperationError), 0d, 1d);
        Assert.All(Measure(scene, run), measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    [Fact]
    public void AnOpaqueWhiteSceneStaysWithinTheBound()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.Uniform(120, 120, 255), 120, 120, 8, 7, 64);

        var run = Run(HardwareOrDefault(), scene);

        Assert.InRange(scene.WorstRatio(run.Store, ConvolutionBound.GpuOperationError), 0d, 1d);
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
    public void TheOutputIsTheGainAppliedToTheStoredValue()
    {
        var scene = Scene(64, 4, 90, 60, 8, 9);

        var run = Run(HardwareOrDefault(), scene, 2.5f);

        for (var index = 0; index < run.Output.Length; index += 4)
        {
            Assert.InRange(run.Output[index] - CpuTileConvolver.ToUnorm(run.Store[index + 2] * 2.5f), -1, 1);
            Assert.InRange(run.Output[index + 1] - CpuTileConvolver.ToUnorm(run.Store[index + 1] * 2.5f), -1, 1);
            Assert.InRange(run.Output[index + 2] - CpuTileConvolver.ToUnorm(run.Store[index] * 2.5f), -1, 1);
            Assert.InRange(run.Output[index + 3] - CpuTileConvolver.ToUnorm(run.Store[index + 3] * 2.5f), -1, 1);
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2.75f)]
    [InlineData(0.3f)]
    public void RenderingTheStoreMatchesTheConvolutionBitForBit(float gain)
    {
        var scene = Scene(64, 5, 150, 97, 11, 47);
        var device = HardwareOrDefault();
        var fused = Run(device, scene, gain);
        using var convolver = new GpuTileConvolver(device);
        var stored = Run(device, convolver, scene, 1f);
        var width = scene.Plan.RegionWidth;
        var height = scene.Plan.RegionHeight;
        using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);

        using (var context = device.CreateComputeContext())
            GpuTileConvolver.RecordStored(in context, convolver.StoreFor(stored.Job), output, width, height, gain);

        var pixels = new Bgra32[width * height];
        output.CopyTo(pixels);
        Assert.Equal(fused.Output, MemoryMarshal.Cast<Bgra32, byte>(pixels).ToArray());
    }

    [Fact]
    public void TheStatisticsDescribeTheSource()
    {
        var scene = Scene(64, 5, 150, 97, 11, 3);

        var run = Run(HardwareOrDefault(), scene);

        for (var tile = 0; tile < scene.Plan.TileCount; tile++)
        {
            var expected = TileInput.Norms(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Plan, tile);
            Assert.Equal(expected, GpuTileCheck.Norms(run.Report, scene.Plan, tile));
        }

        var measurements = Measure(scene, run);
        var totals = new ulong[4];
        for (var index = 0; index < scene.SourceWidth * scene.SourceHeight; index++)
        {
            totals[0] += scene.Source[index * 4 + 2];
            totals[1] += scene.Source[index * 4 + 1];
            totals[2] += scene.Source[index * 4];
            totals[3] += scene.Source[index * 4 + 3];
        }
        for (var channel = 0; channel < 4; channel++)
            Assert.Equal(totals[channel] / 255d, measurements[1 + channel].Reference);
    }

    [Theory]
    [InlineData(64, 5, 150, 97, 11)]
    [InlineData(128, 23, 140, 90, 24)]
    public void AHealthyRunPassesEveryCheck(int size, int radius, int width, int height, int margin)
    {
        var scene = Scene(size, radius, width, height, margin, 17);

        var measurements = Measure(scene, Run(HardwareOrDefault(), scene));

        Assert.Equal(GpuTileCheck.NonFiniteName, measurements[0].Name);
        Assert.Equal(0d, measurements[0].Measured);
        Assert.All(measurements, measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    [Fact]
    public void AScaledSpectrumFailsTheSumCheck()
    {
        var scene = Scene(64, 5, 150, 97, 11, 21);

        var measurements = Measure(scene, Run(HardwareOrDefault(), scene, tamper: spectrum => [.. spectrum.Select(value => value * 1.0005f)]));

        Assert.Contains(measurements, measurement => measurement.Name.StartsWith("sum.", StringComparison.Ordinal) && !measurement.Passes);
    }

    [Fact]
    public void AMirroredKernelFailsTheSpotCheckButKeepsTheSum()
    {
        var scene = Scene(64, 5, 150, 97, 16, 23);

        var measurements = Measure(scene, Run(HardwareOrDefault(), scene, tamper: spectrum =>
        {
            var mirrored = (float[])spectrum.Clone();
            for (var index = 1; index < mirrored.Length; index += 2)
                mirrored[index] = -mirrored[index];
            return mirrored;
        }));

        Assert.Contains(measurements, measurement => measurement.Name.StartsWith("spot.", StringComparison.Ordinal) && !measurement.Passes);
        Assert.All(measurements.Where(measurement => measurement.Name.StartsWith("sum.", StringComparison.Ordinal)), measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    [Fact]
    public void ANaNIsCounted()
    {
        var scene = Scene(64, 5, 150, 97, 11, 29);

        var measurements = Measure(scene, Run(HardwareOrDefault(), scene, tamper: spectrum =>
        {
            var broken = (float[])spectrum.Clone();
            broken[10] = float.NaN;
            return broken;
        }));

        Assert.True(measurements[0].Measured > 0d);
        Assert.False(measurements[0].Passes);
    }

    [Fact]
    public void AReusedConvolverCountsOnlyTheCurrentRun()
    {
        var scene = Scene(64, 5, 150, 97, 11, 29);
        var device = HardwareOrDefault();
        using var convolver = new GpuTileConvolver(device);

        var broken = Run(device, convolver, scene, tamper: spectrum =>
        {
            var values = (float[])spectrum.Clone();
            values[10] = float.NaN;
            return values;
        });
        var healthy = Run(device, convolver, scene);

        Assert.True(broken.Report[GpuTileLayout.CountOffset] > 0u);
        Assert.Equal(0u, healthy.Report[GpuTileLayout.CountOffset]);
        Assert.All(Measure(scene, healthy), measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    [Fact]
    public void TheSoftwareRendererStaysWithinTheBound()
    {
        var scene = Scene(64, 3, 70, 50, 4, 31);

        var run = Run(Software(), scene);

        Assert.InRange(scene.WorstRatio(run.Store, ConvolutionBound.GpuOperationError), 0d, 1d);
        Assert.All(Measure(scene, run), measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    [Fact]
    public void TheGpuAndTheCpuDifferByLessThanBothBounds()
    {
        var scene = Scene(128, 12, 200, 130, 16, 37);

        var gpu = Run(HardwareOrDefault(), scene);
        var cpu = new float[scene.RegionLength];
        using (var convolver = new CpuTileConvolver(2))
            convolver.Convolve(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, scene.Plan, new byte[scene.RegionLength], 1f, cpu);

        for (var y = 0; y < scene.Plan.RegionHeight; y++)
        {
            for (var x = 0; x < scene.Plan.RegionWidth; x++)
            {
                var (redGreen, blueAlpha) = scene.Norms(scene.Plan.RegionX + x, scene.Plan.RegionY + y);
                for (var channel = 0; channel < 4; channel++)
                {
                    var norm = channel < 2 ? redGreen : blueAlpha;
                    var bound = ConvolutionBound.Absolute(128, ConvolutionBound.GpuOperationError, norm) + ConvolutionBound.Absolute(128, ConvolutionBound.CpuOperationError, norm);
                    var index = (y * scene.Plan.RegionWidth + x) * 4 + channel;
                    Assert.True(Math.Abs(gpu.Store[index] - cpu[index]) <= bound);
                }
            }
        }
    }

    [Fact]
    public void PreparationRejectsInconsistentRequests()
    {
        var scene = Scene(64, 5, 40, 30, 6, 41);
        using var convolver = new GpuTileConvolver(HardwareOrDefault());
        var samples = Samples(scene.Plan);

        Assert.Throws<InvalidOperationException>(() => convolver.Prepare(scene.Plan, 6, 6, 40, 30, 1f, samples, false));
        convolver.Upload(scene.Spectrum);
        var other = TilePlan.Create(64, 4, 0, 0, 52, 42);
        Assert.Throws<InvalidOperationException>(() => convolver.Prepare(other, 6, 6, 40, 30, 1f, samples, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.Prepare(scene.Plan, 6, 6, 40, 30, float.NaN, samples, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.Prepare(scene.Plan, 6, 6, 40, 30, 1f, new Int2[GpuTileConvolver.MaximumSamples + 1], false));
        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.Prepare(scene.Plan, 6, 6, 40, 30, 1f, [new Int2(scene.Plan.RegionWidth, 0)], false));
        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.Prepare(scene.Plan, 6, 6, 0, 30, 1f, samples, false));
    }

    [Fact]
    public void TheStoreExistsOnlyWhileItIsRequested()
    {
        var scene = Scene(64, 5, 40, 30, 6, 43);
        using var convolver = new GpuTileConvolver(HardwareOrDefault());
        convolver.Upload(scene.Spectrum);

        var without = convolver.Prepare(scene.Plan, 6, 6, 40, 30, 1f, [], false);
        Assert.False(convolver.HasStore);
        var with = convolver.Prepare(scene.Plan, 6, 6, 40, 30, 1f, [], true);
        Assert.True(convolver.HasStore);
        Assert.NotSame(convolver.StoreFor(without), convolver.StoreFor(with));
        convolver.ReleaseStore();

        Assert.False(convolver.HasStore);
        Assert.Throws<InvalidOperationException>(() => convolver.StoreFor(with));
    }

    static ConvolutionScene LightScene(int size, int radius, int width, int height, int margin, int seed, LightOptions light)
        => ConvolutionScene.Create(ConvolutionScene.RandomSource(width, height, seed, 0.45), width, height, margin, radius, size, light);

    [Theory]
    [InlineData(64, 5, 150, 97, 11, false, true, 0f, 0f)]
    [InlineData(64, 5, 150, 97, 11, true, false, 0f, 0f)]
    [InlineData(128, 23, 140, 90, 24, true, true, 0.9f, 20f)]
    [InlineData(512, 3, 1100, 560, 8, true, false, 0.8f, 100f)]
    public void ALightRunPassesEveryCheckAndStaysWithinTheBound(int size, int radius, int width, int height, int margin, bool linear, bool dither, float threshold, float boost)
    {
        var scene = LightScene(size, radius, width, height, margin, size + radius, new LightOptions(linear, dither, threshold, boost));

        var run = Run(HardwareOrDefault(), scene);

        Assert.InRange(scene.WorstRatio(run.Store, ConvolutionBound.GpuOperationError), 0d, 1d);
        Assert.All(Measure(scene, run), measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    static void AssertOutputIsTheTransformOfTheStore(ConvolutionScene scene, GpuRun run, float gain, LightOptions light)
    {
        var width = scene.Plan.RegionWidth;
        var differing = 0;
        for (var y = 0; y < scene.Plan.RegionHeight; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                var expected = LightTransform.ToBytes(run.Store[index], run.Store[index + 1], run.Store[index + 2], run.Store[index + 3], gain, light, scene.Plan.RegionX + x, scene.Plan.RegionY + y);
                Assert.InRange(run.Output[index] - expected.Blue, -1, 1);
                Assert.InRange(run.Output[index + 1] - expected.Green, -1, 1);
                Assert.InRange(run.Output[index + 2] - expected.Red, -1, 1);
                Assert.InRange(run.Output[index + 3] - expected.Alpha, -1, 1);
                differing += (run.Output[index] != expected.Blue ? 1 : 0) + (run.Output[index + 1] != expected.Green ? 1 : 0)
                    + (run.Output[index + 2] != expected.Red ? 1 : 0) + (run.Output[index + 3] != expected.Alpha ? 1 : 0);
            }
        }

        // The power function of the GPU differs from the table of the CPU by a few millionths, which moves the
        // rounded value by one step in a few of ten thousand places. A wrong threshold or a wrong rounding
        // moves it in about a third of them.
        Assert.InRange(differing, 0, run.Output.Length / 2000);
    }

    [Theory]
    [InlineData(true, false, 1f)]
    [InlineData(true, true, 1f)]
    [InlineData(true, true, 2.5f)]
    [InlineData(false, true, 1f)]
    [InlineData(false, true, 1.7f)]
    public void TheLightOutputIsTheTransformOfTheStoredValue(bool linear, bool dither, float gain)
    {
        var light = new LightOptions(linear, dither, 0.8f, linear ? 8f : 0f);
        var scene = LightScene(64, 4, 90, 60, 8, 9, light);

        var run = Run(HardwareOrDefault(), scene, gain);

        AssertOutputIsTheTransformOfTheStore(scene, run, gain, light);
    }

    [Theory]
    [InlineData(false, true, 1f)]
    [InlineData(true, false, 2.75f)]
    [InlineData(true, true, 0.3f)]
    public void RenderingTheStoreMatchesTheLightConvolutionBitForBit(bool linear, bool dither, float gain)
    {
        var light = new LightOptions(linear, dither, 0.85f, linear ? 12f : 0f);
        var scene = LightScene(64, 5, 150, 97, 11, 47, light);
        var device = HardwareOrDefault();
        var fused = Run(device, scene, gain);
        using var convolver = new GpuTileConvolver(device);
        var stored = Run(device, convolver, scene, 1f);
        var width = scene.Plan.RegionWidth;
        var height = scene.Plan.RegionHeight;
        using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);

        using (var context = device.CreateComputeContext())
            GpuTileConvolver.RecordStored(in context, convolver.StoreFor(stored.Job), output, width, height, gain, in light, scene.Plan.RegionX, scene.Plan.RegionY);

        var pixels = new Bgra32[width * height];
        output.CopyTo(pixels);
        Assert.Equal(fused.Output, MemoryMarshal.Cast<Bgra32, byte>(pixels).ToArray());
    }

    [Fact]
    public void TheLightStatisticsDescribeTheTransformedSource()
    {
        var light = new LightOptions(true, false, 0.9f, 20f);
        var scene = LightScene(64, 5, 150, 97, 11, 3, light);

        var run = Run(HardwareOrDefault(), scene);

        for (var tile = 0; tile < scene.Plan.TileCount; tile++)
        {
            var (redGreen, blueAlpha) = scene.Norms(scene.Plan.RegionX + tile % scene.Plan.TilesX * scene.Plan.ValidSize, scene.Plan.RegionY + tile / scene.Plan.TilesX * scene.Plan.ValidSize);
            var measured = GpuTileCheck.Norms(run.Report, scene.Plan, tile, true);
            Assert.InRange(measured.RedGreen, redGreen * (1d - 1e-4), redGreen * (1d + 1e-3));
            Assert.InRange(measured.BlueAlpha, blueAlpha * (1d - 1e-4), blueAlpha * (1d + 1e-3));
        }

        var measurements = Measure(scene, run);
        var values = scene.Transformed();
        var totals = new double[4];
        for (var index = 0; index < scene.SourceWidth * scene.SourceHeight; index++)
        {
            for (var channel = 0; channel < 4; channel++)
                totals[channel] += values[index * 4 + channel];
        }
        for (var channel = 0; channel < 4; channel++)
            Assert.Equal(totals[channel], measurements[1 + channel].Reference, totals[channel] * 1e-5);
    }

    [Fact]
    public void AScaledSpectrumFailsTheLightSumCheck()
    {
        var scene = LightScene(64, 5, 150, 97, 11, 21, new LightOptions(true, false, 0.9f, 20f));

        var measurements = Measure(scene, Run(HardwareOrDefault(), scene, tamper: spectrum => [.. spectrum.Select(value => value * 1.0005f)]));

        Assert.Contains(measurements, measurement => measurement.Name.StartsWith("sum.", StringComparison.Ordinal) && !measurement.Passes);
    }

    [Fact]
    public void AMirroredKernelFailsTheLightSpotCheck()
    {
        var scene = LightScene(64, 5, 150, 97, 16, 23, new LightOptions(true, false, 0.9f, 20f));

        var measurements = Measure(scene, Run(HardwareOrDefault(), scene, tamper: spectrum =>
        {
            var mirrored = (float[])spectrum.Clone();
            for (var index = 1; index < mirrored.Length; index += 2)
                mirrored[index] = -mirrored[index];
            return mirrored;
        }));

        Assert.Contains(measurements, measurement => measurement.Name.StartsWith("spot.", StringComparison.Ordinal) && !measurement.Passes);
    }

    [Fact]
    public void AWrongHighlightFailsTheLightSpotCheck()
    {
        var claimed = new LightOptions(true, false, 0.9f, 20f);
        var actual = LightScene(64, 5, 150, 97, 16, 25, new LightOptions(true, false, 0.9f, 2f));

        var run = Run(HardwareOrDefault(), actual);
        var measurements = new ConvolutionMeasurement[GpuTileCheck.MeasurementCount(run.Samples.Length)];
        GpuTileCheck.Evaluate(run.Report, run.Job with { Light = claimed }, run.Samples, actual.Spectrum.Kernel, measurements);

        Assert.Contains(measurements, measurement => !measurement.Passes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADitheredOpaqueWhiteSceneNeverDropsToBlackAnywhere(bool linear)
    {
        var light = new LightOptions(linear, true, 0f, 0f);
        var scene = ConvolutionScene.Create(ConvolutionScene.Uniform(1100, 700, 255), 1100, 700, 8, 3, 512, light);

        var run = Run(HardwareOrDefault(), scene);

        var wrong = 0;
        for (var y = 8 + 4; y < 8 + 700 - 4; y++)
        {
            for (var x = 8 + 4; x < 8 + 1100 - 4; x++)
            {
                var index = (y * scene.Plan.RegionWidth + x) * 4;
                if (run.Output[index] < 250 || run.Output[index + 1] < 250 || run.Output[index + 2] < 250 || run.Output[index + 3] < 250)
                    wrong++;
            }
        }

        Assert.Equal(0, wrong);
    }

    static bool Marked(Int2[] entries, int tile)
    {
        var entry = entries[GpuTileConvolver.MaximumSamples + (tile >> 6)];
        return (((tile & 32) == 0 ? entry.X : entry.Y) >> (tile & 31) & 1) != 0;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSamplesBufferMarksTheTilesThatHoldASample(bool linear)
    {
        var light = new LightOptions(linear, false, 0f, 0f);
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(1400, 900, 5, 0.4), 1400, 900, 8, 5, 128, light);
        var plan = scene.Plan;
        using var convolver = new GpuTileConvolver(HardwareOrDefault());
        convolver.Upload(scene.Spectrum);
        var samples = Samples(plan);

        convolver.Prepare(plan, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, 1f, samples, false, light);

        var entries = new Int2[convolver.Samples.Length];
        convolver.Samples.CopyTo(entries);
        Assert.True(plan.TileCount > 64);
        Assert.Equal(samples, entries.Take(GpuTileConvolver.MaximumSamples));
        var expected = new HashSet<int>(samples.Select(sample => plan.TileAt(plan.RegionX + sample.X, plan.RegionY + sample.Y)));
        Assert.True(expected.Count > 1);
        for (var tile = 0; tile < (plan.TileCount + 63) / 64 * 64; tile++)
            Assert.Equal(expected.Contains(tile), Marked(entries, tile));
    }

    [Fact]
    public void TheMarksFollowTheSamplesOfTheLatestJob()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(1400, 900, 6, 0.4), 1400, 900, 8, 5, 128);
        var plan = scene.Plan;
        using var convolver = new GpuTileConvolver(HardwareOrDefault());
        convolver.Upload(scene.Spectrum);
        Int2[] first = [new(0, 0)];
        Int2[] second = [new(plan.RegionWidth - 1, plan.RegionHeight - 1)];

        convolver.Prepare(plan, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, 1f, first, false);
        convolver.Prepare(plan, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, 1f, second, false);

        var entries = new Int2[convolver.Samples.Length];
        convolver.Samples.CopyTo(entries);
        var lastTile = plan.TileAt(plan.RegionX + second[0].X, plan.RegionY + second[0].Y);
        for (var tile = 0; tile < (plan.TileCount + 63) / 64 * 64; tile++)
            Assert.Equal(tile == lastTile, Marked(entries, tile));
        Assert.Equal(second[0], entries[0]);
    }

    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, 1f)]
    [InlineData(true, 1.8f)]
    public void ADitheredTranslucentSceneKeepsEveryColorBelowItsOpacity(bool linear, float gain)
    {
        var light = new LightOptions(linear, true, 0f, 0f);
        var scene = ConvolutionScene.Create(ConvolutionScene.Uniform(240, 160, 128), 240, 160, 8, 3, 64, light);

        var run = Run(HardwareOrDefault(), scene, gain);

        var exceeding = 0;
        var translucent = 0;
        for (var index = 0; index < run.Output.Length; index += 4)
        {
            if (run.Output[index + 3] is > 0 and < 255)
                translucent++;
            if (run.Output[index] > run.Output[index + 3] || run.Output[index + 1] > run.Output[index + 3] || run.Output[index + 2] > run.Output[index + 3])
                exceeding++;
        }

        Assert.True(translucent > 1000, $"{translucent}");
        Assert.Equal(0, exceeding);
    }

    [Theory]
    [InlineData(true, false, 1f)]
    [InlineData(false, true, 1.7f)]
    [InlineData(true, true, 1f)]
    [InlineData(true, true, 2.2f)]
    public void ARegionAwayFromTheOriginDrawsTheSameLightImageAsTheCpu(bool linear, bool dither, float gain)
    {
        var light = new LightOptions(linear, dither, 0.8f, linear ? 8f : 0f);
        var source = ConvolutionScene.RandomSource(120, 80, 77, 0.5);
        var scene = ConvolutionScene.CreateShifted(source, 43, 29, 120, 80, 37, 23, 132, 92, 4, 64, light);
        var plan = scene.Plan;
        var cpu = new byte[scene.RegionLength];
        using (var convolver = new CpuTileConvolver(3))
            convolver.Convolve(scene.Source, scene.SourceX, scene.SourceY, scene.SourceWidth, scene.SourceHeight, scene.Spectrum, plan, cpu, gain, null, light);

        var run = Run(HardwareOrDefault(), scene, gain);

        var differing = 0;
        for (var index = 0; index < cpu.Length; index++)
        {
            Assert.InRange(run.Output[index] - cpu[index], -1, 1);
            if (run.Output[index] != cpu[index])
                differing++;
        }

        Assert.InRange(differing, 0, cpu.Length / 2000);
        Assert.All(Measure(scene, run), measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    [Theory]
    [InlineData(true, true, 1.3f)]
    [InlineData(false, true, 2f)]
    public void RenderingTheStoreMatchesTheLightConvolutionForARegionAwayFromTheOrigin(bool linear, bool dither, float gain)
    {
        var light = new LightOptions(linear, dither, 0.85f, linear ? 12f : 0f);
        var source = ConvolutionScene.RandomSource(120, 80, 78, 0.5);
        var scene = ConvolutionScene.CreateShifted(source, 43, 29, 120, 80, 37, 23, 132, 92, 4, 64, light);
        var device = HardwareOrDefault();
        var fused = Run(device, scene, gain);
        using var convolver = new GpuTileConvolver(device);
        var stored = Run(device, convolver, scene, 1f);
        var width = scene.Plan.RegionWidth;
        var height = scene.Plan.RegionHeight;
        using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);

        using (var context = device.CreateComputeContext())
            GpuTileConvolver.RecordStored(in context, convolver.StoreFor(stored.Job), output, width, height, gain, in light, scene.Plan.RegionX, scene.Plan.RegionY);

        var pixels = new Bgra32[width * height];
        output.CopyTo(pixels);
        Assert.Equal(fused.Output, MemoryMarshal.Cast<Bgra32, byte>(pixels).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySampledTileReportsItsSpotWhateverItsIndex(bool linear)
    {
        var light = new LightOptions(linear, false, 0f, 0f);
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(1400, 900, 12, 0.6), 1400, 900, 8, 5, 128, light);
        var plan = scene.Plan;
        Assert.True(plan.TileCount > 90);
        int[] tiles = [0, 31, 32, 33, 63, 64, 65, 95];
        var samples = tiles.Select(tile => new Int2(
            Math.Min(tile % plan.TilesX * plan.ValidSize + plan.ValidSize / 2, plan.RegionWidth - 1),
            Math.Min(tile / plan.TilesX * plan.ValidSize + plan.ValidSize / 2, plan.RegionHeight - 1))).ToArray();
        Assert.Equal(tiles, samples.Select(sample => plan.TileAt(plan.RegionX + sample.X, plan.RegionY + sample.Y)).ToArray());

        var run = Run(HardwareOrDefault(), scene, 1f, null, samples);

        var measurements = Measure(scene, run);
        Assert.All(measurements, measurement => Assert.True(measurement.Passes, $"{measurement}"));
        for (var sample = 0; sample < samples.Length; sample++)
            Assert.True(measurements[1 + 4 + sample * 4 + 3].Measured > 0d, $"tile {tiles[sample]}");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ADarkSceneStaysWithinTheBoundInLinearLight(bool linear, bool dither)
    {
        var random = new Random(5);
        var source = new byte[150 * 97 * 4];
        for (var index = 0; index < source.Length; index += 4)
        {
            source[index] = (byte)random.Next(0, 9);
            source[index + 1] = (byte)random.Next(0, 9);
            source[index + 2] = (byte)random.Next(0, 9);
            source[index + 3] = 255;
        }

        var light = new LightOptions(linear, dither, 0f, 0f);
        var scene = ConvolutionScene.Create(source, 150, 97, 11, 5, 64, light);

        var run = Run(HardwareOrDefault(), scene);

        Assert.All(Measure(scene, run), measurement => Assert.True(measurement.Passes, $"{measurement}"));
        Assert.InRange(scene.WorstRatio(run.Store, ConvolutionBound.GpuOperationError + GpuTileCheck.LightTransformError), 0d, 1d);
        AssertOutputIsTheTransformOfTheStore(scene, run, 1f, light);
    }

    [Fact]
    public void PreparingInvalidLightOptionsIsRejected()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(100, 60, 3, 0.5), 100, 60, 8, 4, 64);
        var plan = scene.Plan;
        using var convolver = new GpuTileConvolver(HardwareOrDefault());
        convolver.Upload(scene.Spectrum);

        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.Prepare(plan, 8, 8, 100, 60, 1f, [], false, new LightOptions(true, false, 1.5f, 5f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => convolver.Prepare(plan, 8, 8, 100, 60, 1f, [], false, new LightOptions(true, false, 0.5f, 2000f)));
        convolver.Prepare(plan, 8, 8, 100, 60, 1f, [], false, new LightOptions(true, false, 0.5f, 5f));
    }

    [Fact]
    public void TheLinearLightToleranceExceedsTheOtherOneByTheTransformError()
    {
        var scene = ConvolutionScene.Create(ConvolutionScene.RandomSource(150, 97, 31, 0.45), 150, 97, 11, 5, 64, new LightOptions(true, false, 0f, 0f));
        var run = Run(HardwareOrDefault(), scene);
        var plan = scene.Plan;
        var sampleCount = run.Samples.Length;
        var linear = new ConvolutionMeasurement[GpuTileCheck.MeasurementCount(sampleCount)];
        var plain = new ConvolutionMeasurement[linear.Length];
        var linearJob = run.Job with { Light = new LightOptions(true, true, 0f, 0f) };
        var plainJob = run.Job with { Light = new LightOptions(false, true, 0f, 0f) };

        GpuTileCheck.Evaluate(run.Report, linearJob, run.Samples, scene.Spectrum.Kernel, linear);
        GpuTileCheck.Evaluate(run.Report, plainJob, run.Samples, scene.Spectrum.Kernel, plain);

        for (var sample = 0; sample < sampleCount; sample++)
        {
            var tile = plan.TileAt(plan.RegionX + run.Samples[sample].X, plan.RegionY + run.Samples[sample].Y);
            var (redGreen, blueAlpha) = GpuTileCheck.Norms(run.Report, plan, tile, true);
            for (var channel = 0; channel < 4; channel++)
            {
                var index = 1 + 4 + sample * 4 + channel;
                var norm = channel < 2 ? redGreen : blueAlpha;
                Assert.Equal(GpuTileCheck.LightTransformError * norm, linear[index].Tolerance - plain[index].Tolerance, norm * 1e-12);
            }
        }

        Assert.Equal(Math.ScaleB(1d, -16), GpuTileCheck.LightTransformError);
    }
}
