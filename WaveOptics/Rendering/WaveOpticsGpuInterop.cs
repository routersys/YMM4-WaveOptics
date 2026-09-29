using ComputeWeave;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsInteropProvider : ComputeExternalDirect3D11Provider
{
    private readonly ID2D1DeviceContext6 _renderContext;

    private WaveOpticsInteropProvider(
        ID3D11Device1 device,
        ID3D11DeviceContext4 context,
        ID2D1DeviceContext6 renderContext,
        ComputeExternalQueueScheduler scheduler)
        : base(device.NativePointer, context.NativePointer, renderContext.NativePointer, scheduler)
    {
        _renderContext = renderContext;
    }

    public ID2D1DeviceContext6 RenderContext => _renderContext;

    public static WaveOpticsInteropProvider? TryCreate(
        IGraphicsDevicesAndContext devices,
        ComputeExternalQueueScheduler scheduler,
        out GraphicsDevice? graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        graphicsDevice = null;

        ID3D11Device1? device = null;
        ID3D11DeviceContext4? context = null;
        ID2D1DeviceContext6? renderContext = null;
        try
        {
            if (!GraphicsDevice.TryGetDevice(new ExternalAdapterIdentity(devices.DXGI.Adapter.Description.Luid), out graphicsDevice))
                return null;

            device = devices.D3D.Device.QueryInterface<ID3D11Device1>();
            context = devices.D3D.DeviceContext.QueryInterface<ID3D11DeviceContext4>();
            renderContext = devices.D2D.Device.CreateDeviceContext(DeviceContextOptions.EnableMultithreadedOptimizations);
            var provider = new WaveOpticsInteropProvider(device, context, renderContext, scheduler);
            renderContext = null;
            return provider;
        }
        catch
        {
            graphicsDevice = null;
            return null;
        }
        finally
        {
            renderContext?.Dispose();
            context?.Dispose();
            device?.Dispose();
        }
    }

    protected override void DisposeCore()
    {
        _renderContext.Dispose();
    }
}

[ComputeInteropResourceSet]
internal sealed partial class WaveOpticsResourceSet
{
    [ComputeSharedTexture(
        ComputeResourceResizePolicy.Exact,
        ComputeResourceAccess.ReadWrite,
        ExternalResourceAccess.Write,
        ExternalTextureUsage.RenderTarget,
        ComputeAlphaMode.Premultiplied,
        ComputeSharedTextureInitialOwner.External,
        ComputeResourceRecovery.RecreateFromHost)]
    private readonly SharedTextureSlot<Bgra32, Float4, ExternalDirect3D11TextureView> _source;

    [ComputeSharedTexture(
        ComputeResourceResizePolicy.GrowOnly,
        ComputeResourceAccess.ReadWrite,
        ExternalResourceAccess.Read,
        ExternalTextureUsage.Sampled,
        ComputeAlphaMode.Premultiplied,
        ComputeSharedTextureInitialOwner.Compute,
        ComputeResourceRecovery.Recompute)]
    private readonly SharedTextureSlot<Bgra32, Float4, ExternalDirect3D11TextureView> _output;
}
