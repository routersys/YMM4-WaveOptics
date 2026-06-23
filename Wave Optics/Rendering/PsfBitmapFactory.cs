using System.Buffers;
using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using WaveOptics.Abstractions;

namespace WaveOptics.Rendering;

internal static class PsfBitmapFactory
{
    public static ID2D1Bitmap Create(ID2D1DeviceContext deviceContext, PsfKernel kernel)
    {
        var values = kernel.Values.Span;
        var pixels = ArrayPool<float>.Shared.Rent(values.Length * 4);
        try
        {
            for (var index = 0; index < values.Length; index++)
            {
                var value = (float)values[index];
                var offset = index * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 1;
            }

            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                var properties = new BitmapProperties1(
                    new PixelFormat(Format.R32G32B32A32_Float, Vortice.DCommon.AlphaMode.Ignore),
                    96,
                    96,
                    BitmapOptions.None);
                return ((ID2D1DeviceContext1)deviceContext).CreateBitmap(
                    new SizeI(kernel.Size, kernel.Size),
                    handle.AddrOfPinnedObject(),
                    kernel.Size * 4 * sizeof(float),
                    properties);
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(pixels);
        }
    }
}
