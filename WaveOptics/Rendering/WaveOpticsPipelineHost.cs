using ComputeWeave;

namespace WaveOptics.Rendering;

[ComputeResourceGroup]
internal sealed partial class WaveOpticsCanvasResources
{
    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float4> Horizontal { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float4> Convolved { get; }
}

[ComputePipelineHost("_device", 1)]
internal sealed partial class WaveOpticsPipelineHost
{
    private readonly GraphicsDevice _device;

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite, ComputeResourceRecovery.Recompute)]
    private readonly ComputeResourceGroupSlot<WaveOpticsCanvasResources> _canvas = new();

    [ComputePipeline]
    private void RecordFullPipeline(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_canvas))] WaveOpticsCanvasResources canvas,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<float> weights,
        in WaveOpticsPipeline.PixelRect sourceRect,
        in WaveOpticsPipeline.Kernel kernel,
        float gain)
    {
        _ = _device;

        RecordConvolutionStage(in context, canvas, source, weights, in sourceRect, in sourceRect, sourceRect.Width, in kernel);
        RecordRenderStage(in context, canvas, output, in sourceRect, sourceRect.Width, gain);
    }

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
        [ComputeOwnedResource(nameof(_canvas))] WaveOpticsCanvasResources canvas,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<float> weights,
        in WaveOpticsPipeline.PixelRect sourceRect,
        in WaveOpticsPipeline.PixelRect rect,
        int canvasWidth,
        in WaveOpticsPipeline.Kernel kernel)
    {
        _ = _device;

        RecordConvolutionStage(in context, canvas, source, weights, in sourceRect, in rect, canvasWidth, in kernel);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedConvolution(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_canvas))] WaveOpticsCanvasResources canvas,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<float> weights,
        in WaveOpticsPipeline.PixelRect sourceRect,
        in WaveOpticsPipeline.PixelRect rect,
        int canvasWidth,
        in WaveOpticsPipeline.Kernel kernel)
    {
        _ = _device;

        RecordConvolutionStage(in context, canvas, source, weights, in sourceRect, in rect, canvasWidth, in kernel);
    }

    [ComputePipeline]
    private void RecordRender(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_canvas))] WaveOpticsCanvasResources canvas,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        in WaveOpticsPipeline.PixelRect rect,
        int canvasWidth,
        float gain)
    {
        _ = _device;

        RecordRenderStage(in context, canvas, output, in rect, canvasWidth, gain);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedRender(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_canvas))] WaveOpticsCanvasResources canvas,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> output,
        in WaveOpticsPipeline.PixelRect rect,
        int canvasWidth,
        float gain)
    {
        _ = _device;

        RecordRenderStage(in context, canvas, output, in rect, canvasWidth, gain);
    }

    private static void RecordSourceHashStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteBuffer<int> scratch,
        in WaveOpticsPipeline.PixelRect sourceRect)
    {
        context.For(1, new SourceHashResetShader(scratch));
        context.Barrier(scratch);
        context.For(sourceRect.Width, sourceRect.Height, new SourceHashShader(
            source, scratch, sourceRect.X, sourceRect.Y, sourceRect.Width, sourceRect.Height));
        context.Barrier(scratch);
    }

    private static void RecordConvolutionStage(
        in ComputeContext context,
        WaveOpticsCanvasResources canvas,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadOnlyBuffer<float> weights,
        in WaveOpticsPipeline.PixelRect sourceRect,
        in WaveOpticsPipeline.PixelRect rect,
        int canvasWidth,
        in WaveOpticsPipeline.Kernel kernel)
    {
        var top = Math.Max(rect.Y - kernel.Radius, sourceRect.Y);
        var bottom = Math.Min(rect.Y + rect.Height + kernel.Radius, sourceRect.Y + sourceRect.Height);
        for (var term = 0; term < kernel.Rank; term++)
        {
            context.For(GetBlockCount(rect.Width), bottom - top, new HorizontalPassShader(
                source, weights, canvas.Horizontal,
                WaveOpticsSettings.GetWeightOffset(term, 0), kernel.Radius,
                rect.X, top, rect.Width, bottom - top, canvasWidth,
                sourceRect.X, sourceRect.Y, sourceRect.Width));
            context.Barrier(canvas.Horizontal);
            context.For(rect.Width, GetBlockCount(rect.Height), new VerticalPassShader(
                canvas.Horizontal, weights, canvas.Convolved,
                WaveOpticsSettings.GetWeightOffset(term, 1), kernel.Radius,
                rect.X, rect.Y, rect.Width, rect.Height, canvasWidth,
                sourceRect.Y, sourceRect.Height, term > 0));
            context.Barrier(canvas.Horizontal);
            context.Barrier(canvas.Convolved);
        }
    }

    private static int GetBlockCount(int length)
        => (length + WaveOpticsSettings.ConvolutionBlock - 1) / WaveOpticsSettings.ConvolutionBlock;

    private static void RecordRenderStage(
        in ComputeContext context,
        WaveOpticsCanvasResources canvas,
        ReadWriteTexture2D<Bgra32, Float4> output,
        in WaveOpticsPipeline.PixelRect rect,
        int canvasWidth,
        float gain)
    {
        context.For(rect.Width, rect.Height, new RenderShader(
            canvas.Convolved, output, rect.X, rect.Y, rect.Width, rect.Height, canvasWidth, gain));
    }
}
