using System.Reflection;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using WaveOptics.Effects;
using WaveOptics.Rendering;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Tests;

[Collection("Direct2D")]
public sealed class WaveOpticsEffectProcessorBitmapTests
{
    static object Field(WaveOpticsEffectProcessor processor, string name)
        => typeof(WaveOpticsEffectProcessor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(processor)!;

    static int SourceBitmapReferences(WaveOpticsEffectProcessor processor)
    {
        var resources = (WaveOpticsResourceSet)Field(processor, "_resourceSet");
        using var borrow = resources.BeginSourceExternalOperation();
        return Marshal.Release(borrow.DangerousGetView().AddRefBitmap());
    }

    [Fact]
    public void EveryFrameLeavesTheReferencesOfTheSourceBitmapBalanced()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        WaveOpticsEffectProcessorAllocationTests.RequireInterop(context);
        using var source = new SourceImage(context, WaveOpticsEffectProcessorAllocationTests.Size, WaveOpticsEffectProcessorAllocationTests.Size, WaveOpticsEffectProcessorAllocationTests.Shape);
        using var processor = WaveOpticsEffectProcessorAllocationTests.Processor(context, new WaveOpticsEffect(), true);
        processor.SetInput(source.Bitmap);
        var description = EffectDescriptions.At(7, WaveOpticsEffectProcessorAllocationTests.Length);
        processor.Update(description);
        var baseline = SourceBitmapReferences(processor);

        for (var frame = 0; frame < 10; frame++)
            processor.Update(description);

        Assert.Equal(baseline, SourceBitmapReferences(processor));
    }

    [Fact]
    public void TheSourceBitmapWrapperHoldsNoPointerBetweenFrames()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        WaveOpticsEffectProcessorAllocationTests.RequireInterop(context);
        using var source = new SourceImage(context, WaveOpticsEffectProcessorAllocationTests.Size, WaveOpticsEffectProcessorAllocationTests.Size, WaveOpticsEffectProcessorAllocationTests.Shape);
        using var processor = WaveOpticsEffectProcessorAllocationTests.Processor(context, new WaveOpticsEffect(), true);
        processor.SetInput(source.Bitmap);
        var description = EffectDescriptions.At(7, WaveOpticsEffectProcessorAllocationTests.Length);

        processor.Update(description);
        processor.Update(description);

        Assert.Equal(IntPtr.Zero, ((ID2D1Bitmap1)Field(processor, "_sourceBitmap")).NativePointer);
    }
}
