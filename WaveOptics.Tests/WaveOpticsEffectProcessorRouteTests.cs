using ComputeGuard;
using ComputeWeave;
using WaveOptics.Effects;
using WaveOptics.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Tests;

[Collection("Direct2D")]
public sealed class WaveOpticsEffectProcessorRouteTests
{
    const int Size = 72;
    const int Length = 30;

    static readonly Bgra Warm = Bgra.Opaque(220, 160, 90);

    static Bgra Shape(int x, int y)
        => x is >= 20 and < 52 && y is >= 18 and < 50 && (x + 2 * y) % 9 != 0 ? Warm : Bgra.Transparent;

    sealed class Reports
    {
        public List<ComputeAnomalyException> Received { get; } = [];

        public ComputeGuardian CreateGuardian() => new(new ComputeGuardianOptions { Report = Received.Add });
    }

    static void RequireInterop(IGraphicsDevicesAndContext devices)
    {
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = WaveOpticsInteropProvider.TryCreate(devices, scheduler, out var device);
        if (provider is null || device is null)
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
    }

    static void RequireHardware(WaveOpticsEffectProcessor processor)
    {
        if (processor.Device is not { IsSoftware: false })
            Assert.Skip("The GPU route needs a hardware adapter.");
    }

    static WaveOpticsEffect Effect()
    {
        var effect = new WaveOpticsEffect { KernelRadius = 9 };
        effect.Defocus.Values[0].Value = 0.6d;
        effect.ComaHorizontal.Values[0].Value = 0.8d;
        return effect;
    }

    static WaveOpticsEffectProcessor Processor(IGraphicsDevicesAndContext devices, WaveOpticsEffect effect, ComputeGuardian guardian, bool allowGpu, Func<ComputeDevice, IReadOnlyList<ComputeCheck>>? selfTest = null)
        => new(devices, effect, guardian, selfTest, allowGpu);

    static Rendering RenderFrame(IGraphicsDevicesAndContext devices, IVideoEffectProcessor processor, int frame)
    {
        processor.Update(EffectDescriptions.At(frame, Length));
        return Rendering.Capture(devices, processor.Output);
    }

    static Rendering CpuReference(IGraphicsDevicesAndContext devices, SourceImage source, WaveOpticsEffect effect, int frame)
    {
        using var processor = Processor(devices, effect, new Reports().CreateGuardian(), false);
        processor.SetInput(source.Bitmap);
        return RenderFrame(devices, processor, frame);
    }

    static ComputeCheck FailingCheck => ComputeCheck.Count("broken", 1);

    [Fact]
    public void WithoutTheGpuTheBlurIsDrawnOnTheCpuCloseToTheGpu()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Shape);
        var reports = new Reports();
        var effect = Effect();
        using var gpu = Processor(context, effect, reports.CreateGuardian(), true);
        using var cpu = Processor(context, effect, reports.CreateGuardian(), false);
        gpu.SetInput(source.Bitmap);
        cpu.SetInput(source.Bitmap);

        var expected = RenderFrame(context, gpu, 0);
        var actual = RenderFrame(context, cpu, 0);

        Assert.Null(cpu.Device);
        Assert.NotNull(gpu.Device);
        Assert.Equal((expected.Left, expected.Top, expected.Width, expected.Height), (actual.Left, actual.Top, actual.Width, actual.Height));
        Assert.All(expected.Coordinates(), point =>
        {
            var a = expected[point.X, point.Y];
            var b = actual[point.X, point.Y];
            Assert.InRange(a.Blue - b.Blue, -2, 2);
            Assert.InRange(a.Green - b.Green, -2, 2);
            Assert.InRange(a.Red - b.Red, -2, 2);
            Assert.InRange(a.Alpha - b.Alpha, -2, 2);
        });
        Assert.Contains(actual.Coordinates(), point => Shape(point.X, point.Y).Alpha == 0 && actual[point.X, point.Y].Alpha > 0);
        Assert.Empty(reports.Received);
    }

    [Fact]
    public void AHealthyAdapterIsTrustedWithoutAReport()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Shape);
        var reports = new Reports();
        var guardian = reports.CreateGuardian();
        using var processor = Processor(context, Effect(), guardian, true);
        RequireHardware(processor);
        processor.SetInput(source.Bitmap);

        RenderFrame(context, processor, 0);

        Assert.Equal(ComputeHealth.Trusted, guardian.HealthOf(processor.Device!.Value.Luid));
        Assert.Empty(reports.Received);
    }

    public static readonly TheoryData<string, Func<Float2[], Float2[]>> Faults = new()
    {
        { "nan", spectrum => { var broken = (Float2[])spectrum.Clone(); broken[5] = new Float2(float.NaN, 0f); return broken; } },
        { "scale", spectrum => [.. spectrum.Select(value => new Float2(value.X * 1.002f, value.Y * 1.002f))] },
        { "mirror", spectrum => [.. spectrum.Select(value => new Float2(value.X, -value.Y))] },
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public void AFrameThatFailsItsChecksShowsTheCpuResultPinsTheAdapterAndReportsOnce(string fault, Func<Float2[], Float2[]> tamper)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Shape);
        var reports = new Reports();
        var guardian = reports.CreateGuardian();
        var effect = Effect();
        using var processor = Processor(context, effect, guardian, true);
        RequireHardware(processor);
        processor.SetInput(source.Bitmap);
        processor.Pipeline!.SpectrumTamper = tamper;

        var first = RenderFrame(context, processor, 0);
        effect.Gain.Values[0].Value = 150d;
        var second = RenderFrame(context, processor, 1);

        Assert.True(first.SamePixelsAs(CpuReference(context, source, Effect(), 0)), fault);
        var brighter = Effect();
        brighter.Gain.Values[0].Value = 150d;
        Assert.True(second.SamePixelsAs(CpuReference(context, source, brighter, 1)), fault);
        Assert.Equal(ComputeHealth.Pinned, guardian.HealthOf(processor.Device!.Value.Luid));
        var report = Assert.Single(reports.Received);
        Assert.Equal(ComputeFailure.CheckFailed, report.Anomaly.Failure);
    }

    [Fact]
    public void AFailedSelfTestStartsOnTheCpuAndReportsOnce()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Shape);
        var reports = new Reports();
        var guardian = reports.CreateGuardian();
        using var processor = Processor(context, Effect(), guardian, true, _ => [FailingCheck]);
        RequireHardware(processor);
        processor.SetInput(source.Bitmap);

        var first = RenderFrame(context, processor, 0);
        var second = RenderFrame(context, processor, 1);

        Assert.True(first.SamePixelsAs(CpuReference(context, source, Effect(), 0)));
        Assert.True(second.SamePixelsAs(first));
        Assert.Equal(ComputeHealth.Pinned, guardian.HealthOf(processor.Device!.Value.Luid));
        var report = Assert.Single(reports.Received);
        Assert.Equal(ComputeFailure.SelfTestFailed, report.Anomaly.Failure);
        Assert.Equal("broken", report.Anomaly.Check?.Name);
    }

    [Fact]
    public void ASelfTestThatThrowsStartsOnTheCpu()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Shape);
        var reports = new Reports();
        var guardian = reports.CreateGuardian();
        using var processor = Processor(context, Effect(), guardian, true, _ => throw new InvalidOperationException("self-test"));
        RequireHardware(processor);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        Assert.True(rendering.SamePixelsAs(CpuReference(context, source, Effect(), 0)));
        var report = Assert.Single(reports.Received);
        Assert.Equal(ComputeFailure.SelfTestFailed, report.Anomaly.Failure);
        Assert.IsType<InvalidOperationException>(report.InnerException);
    }

    [Fact]
    public void ReturningToAFrameOnTheCpuReproducesItExactly()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, Shape);
        var effect = Effect();
        using var processor = Processor(context, effect, new Reports().CreateGuardian(), false);
        processor.SetInput(source.Bitmap);

        var before = RenderFrame(context, processor, 0);
        effect.Gain.Values[0].Value = 250d;
        RenderFrame(context, processor, 0);
        effect.Gain.Values[0].Value = 60d;
        RenderFrame(context, processor, 0);
        effect.Gain.Values[0].Value = 100d;
        var after = RenderFrame(context, processor, 0);

        Assert.True(after.SamePixelsAs(before));
    }

    [Fact]
    public void AZeroAmountOnTheCpuPassesTheImageThrough()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, Shape);
        var effect = Effect();
        effect.Amount.Values[0].Value = 0d;
        using var processor = Processor(context, effect, new Reports().CreateGuardian(), false);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        Assert.Equal((0, 0, Size, Size), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.Equal(source[point.X, point.Y], rendering[point.X, point.Y]));
    }
}
