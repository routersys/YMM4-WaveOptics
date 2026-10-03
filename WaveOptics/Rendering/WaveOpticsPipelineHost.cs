using ComputeWeave;
using SpectralConvolution;

namespace WaveOptics.Rendering;

[ComputePipelineHost("_device", 1)]
internal sealed partial class WaveOpticsPipelineHost
{
    private readonly GraphicsDevice _device;

    [ComputePipeline]
    private void RecordSourceHash(
        in ComputeContext context,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        in WaveOpticsPipeline.PixelRect sourceRect)
    {
        _ = _device;

        RecordSourceHashStage(in context, source, scratch, in sourceRect);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedSourceHash(
        in ComputeContext context,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        in WaveOpticsPipeline.PixelRect sourceRect)
    {
        _ = _device;

        RecordSourceHashStage(in context, source, scratch, in sourceRect);
    }

    [ComputePipeline]
    private void RecordConvolution(
        in ComputeContext context,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<Float4> tiles,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<Float2> twiddles,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<Float2> spectrum,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<uint> report,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<Int2> samples,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<Float4> store,
        in GpuTileJob job)
    {
        _ = _device;

        GpuTileConvolver.Record(in context, source, output, tiles, twiddles, spectrum, report, samples, store, in job);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedConvolution(
        in ComputeContext context,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> output,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<Float4> tiles,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<Float2> twiddles,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<Float2> spectrum,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<uint> report,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<Int2> samples,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<Float4> store,
        in GpuTileJob job)
    {
        _ = _device;

        GpuTileConvolver.Record(in context, source, output, tiles, twiddles, spectrum, report, samples, store, in job);
    }

    [ComputePipeline]
    private void RecordStoredRender(
        in ComputeContext context,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<Float4> store,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        in WaveOpticsPipeline.PixelRect rect,
        float gain)
    {
        _ = _device;

        GpuTileConvolver.RecordStored(in context, store, output, rect.Width, rect.Height, gain);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedStoredRender(
        in ComputeContext context,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<Float4> store,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> output,
        in WaveOpticsPipeline.PixelRect rect,
        float gain)
    {
        _ = _device;

        GpuTileConvolver.RecordStored(in context, store, output, rect.Width, rect.Height, gain);
    }

    private static void RecordSourceHashStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteBuffer<int> scratch,
        in WaveOpticsPipeline.PixelRect sourceRect)
    {
        context.For(1, new SourceHashResetShader(scratch));
        context.Barrier(scratch);
        context.For(GetBlockCount(sourceRect.Width, WaveOpticsSettings.SourceHashSpan), sourceRect.Height, new SourceHashShader(
            source, scratch, sourceRect.X, sourceRect.Y, sourceRect.Width, sourceRect.Height));
        context.Barrier(scratch);
    }

    private static int GetBlockCount(int length, int block)
        => (length + block - 1) / block;
}
