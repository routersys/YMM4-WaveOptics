using ComputeGuard;
using ComputeWeave;
using WaveOptics.Effects;
using WaveOptics.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Tests;

[Collection("Direct2D")]
public sealed class WaveOpticsEffectProcessorAllocationTests
{
    internal const int Size = 128;
    internal const int Length = 60;
    const int Rounds = 24;

    internal static Bgra Shape(int x, int y) => x is >= 32 and < 96 && y is >= 32 and < 96 && (x + 2 * y) % 9 != 0 ? Bgra.Opaque(220, 160, 90) : Bgra.Transparent;

    internal static void RequireInterop(IGraphicsDevicesAndContext devices)
    {
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = WaveOpticsInteropProvider.TryCreate(devices, scheduler, out var device);
        if (provider is null || device is null)
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
    }

    internal static WaveOpticsEffectProcessor Processor(IGraphicsDevicesAndContext devices, WaveOpticsEffect effect, bool allowGpu)
        => new(devices, effect, new ComputeGuardian(new ComputeGuardianOptions { Report = _ => { } }), null, allowGpu);

    static long MinimumAllocatedPerUpdate(bool allowGpu, Action<WaveOpticsEffect> configure)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        if (allowGpu)
            RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, Shape);
        var effect = new WaveOpticsEffect();
        configure(effect);
        using var processor = Processor(context, effect, allowGpu);
        processor.SetInput(source.Bitmap);
        var description = EffectDescriptions.At(7, Length);
        for (var warmUp = 0; warmUp < 8; warmUp++)
            processor.Update(description);
        AllocationProbe.Settle();

        return AllocationProbe.MinimumAllocatedBytes(() => processor.Update(description), Rounds);
    }

    [Fact]
    public void AnUnchangedFrameOnTheGpuRouteAllocatesNothing()
        => Assert.Equal(0L, MinimumAllocatedPerUpdate(true, _ => { }));

    [Fact]
    public void AnUnchangedFrameOnTheCpuRouteAllocatesNothing()
        => Assert.Equal(0L, MinimumAllocatedPerUpdate(false, _ => { }));

    [Fact]
    public void AFrameWithoutAmountOnTheGpuRouteAllocatesNothing()
        => Assert.Equal(0L, MinimumAllocatedPerUpdate(true, effect => effect.Amount.Values[0].Value = 0d));

    [Fact]
    public void AFrameWithoutAmountOnTheCpuRouteAllocatesNothing()
        => Assert.Equal(0L, MinimumAllocatedPerUpdate(false, effect => effect.Amount.Values[0].Value = 0d));

    [Fact]
    public void AnUnchangedFrameWithDepthOnTheGpuRouteAllocatesNothing()
        => Assert.Equal(0L, MinimumAllocatedPerUpdate(true, effect =>
        {
            effect.UseDepth = true;
            effect.FocusDistance.Values[0].Value = 800d;
        }));

    [Fact]
    public void AnUnchangedChromaticFrameOnTheGpuRouteAllocatesNothing()
        => Assert.Equal(0L, MinimumAllocatedPerUpdate(true, effect => effect.ColorMode = WaveOpticsColorMode.Broadband));
}
