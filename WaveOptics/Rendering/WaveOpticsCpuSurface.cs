using System.Numerics;
using SpectralConvolution;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsCpuSurface : IDisposable
{
    private const int Channels = ByteColor.Channels;
    private const float Dpi = 96f;

    private static readonly PixelFormat SurfaceFormat = new(Vortice.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied);

    private readonly ID2D1DeviceContext _context;
    private ID2D1Bitmap1? _target;
    private ID2D1Bitmap1? _readBack;
    private ID2D1Bitmap1? _output;
    private int _sourceWidth;
    private int _sourceHeight;
    private int _outputWidth;
    private int _outputHeight;
    private byte[] _pixels = [];

    public WaveOpticsCpuSurface(ID2D1Device device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _context = device.CreateDeviceContext(DeviceContextOptions.EnableMultithreadedOptimizations);
    }

    public ID2D1Bitmap1? Output => _output;

    public byte[] Read(ID2D1Image input, Vector2 offset, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (target, readBack) = EnsureSource(width, height);

        var previousTarget = _context.Target;
        _context.Target = target;
        _context.BeginDraw();
        _context.Clear(null);
        _context.DrawImage(input, offset, null, InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
        _context.EndDraw();
        _context.Target = previousTarget;

        readBack.CopyFromBitmap(target);
        var mapped = readBack.Map(MapOptions.Read);
        try
        {
            var stride = width * Channels;
            for (var y = 0; y < height; y++)
                CopyRow(mapped.Bits + y * mapped.Pitch, _pixels.AsSpan(y * stride, stride));
        }
        finally
        {
            readBack.Unmap();
        }

        return _pixels;
    }

    public ID2D1Bitmap1 Write(ReadOnlySpan<byte> pixels, int width, int height, out bool changed)
    {
        changed = false;
        if (_output is null || _outputWidth < width || _outputHeight < height)
        {
            _output?.Dispose();
            _output = null;
            _output = _context.CreateBitmap(new SizeI(width, height), new BitmapProperties1(SurfaceFormat, Dpi, Dpi, BitmapOptions.None));
            _outputWidth = width;
            _outputHeight = height;
            changed = true;
        }

        _output.CopyFromMemory(new RectI(0, 0, width, height), pixels, width * Channels);
        return _output;
    }

    public void Dispose()
    {
        _target?.Dispose();
        _readBack?.Dispose();
        _output?.Dispose();
        _target = null;
        _readBack = null;
        _output = null;
        _context.Dispose();
    }

    private (ID2D1Bitmap1 Target, ID2D1Bitmap1 ReadBack) EnsureSource(int width, int height)
    {
        if (_target is { } currentTarget && _readBack is { } currentReadBack && _sourceWidth == width && _sourceHeight == height)
            return (currentTarget, currentReadBack);

        _target?.Dispose();
        _readBack?.Dispose();
        _target = null;
        _readBack = null;
        var size = new SizeI(width, height);
        var target = _context.CreateBitmap(size, new BitmapProperties1(SurfaceFormat, Dpi, Dpi, BitmapOptions.Target));
        _target = target;
        var readBack = _context.CreateBitmap(size, new BitmapProperties1(SurfaceFormat, Dpi, Dpi, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        _readBack = readBack;
        if (_pixels.Length < width * height * Channels)
            _pixels = new byte[width * height * Channels];
        _sourceWidth = width;
        _sourceHeight = height;
        return (target, readBack);
    }

    private static unsafe void CopyRow(nint source, Span<byte> destination)
        => new ReadOnlySpan<byte>((void*)source, destination.Length).CopyTo(destination);
}
