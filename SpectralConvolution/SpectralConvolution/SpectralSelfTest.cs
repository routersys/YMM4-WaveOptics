using System.Runtime.InteropServices;
using ComputeWeave;

namespace SpectralConvolution;

internal static class SpectralSelfTest
{
    public const string DeterminismName = "selftest.determinism";
    public const string FullName = "selftest.full";
    public const string ChromaticDeterminismName = "selftest.chromatic.determinism";
    public const string ChromaticFullName = "selftest.chromatic.full";
    public const int FullRadius = 3;
    public const int Margin = 4;

    static readonly int[] Sizes = [TilePlan.SmallSize, TilePlan.LargeSize];

    public static int MeasurementCount => 2 * Sizes.Length * (2 + 2 * GpuTileCheck.MeasurementCount(GpuTileConvolver.MaximumSamples));

    public static int Run(GraphicsDevice device, int maximumRadius, Span<ConvolutionMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRadius, FullRadius);
        if (measurements.Length < MeasurementCount)
            throw new ArgumentException(null, nameof(measurements));

        var written = 0;
        using var convolver = new GpuTileConvolver(device);
        for (var pass = 0; pass < 2; pass++)
        {
            var chromatic = pass == 1;
            foreach (var size in Sizes)
            {
                written += RunFull(device, convolver, size, chromatic, measurements[written..]);
                var radius = Math.Min(maximumRadius, size == TilePlan.SmallSize ? TilePlan.SmallSizeRadiusLimit : TilePlan.MaximumRadius);
                written += RunChecked(device, convolver, size, Math.Min(radius, (size - 2) / 2), chromatic, measurements[written..]);
            }
        }

        return written;
    }

    public static double[] ChromaticKernel(int radius, ChromaticChannel channel)
    {
        var (spread, lean) = channel switch
        {
            ChromaticChannel.Red => (1.35, 0.5),
            ChromaticChannel.Green => (1d, 0.6),
            _ => (0.7, 0.7),
        };
        var kernelSize = radius * 2 + 1;
        var values = new double[kernelSize * kernelSize];
        var scale = (radius * radius + 1d) * spread;
        for (var y = 0; y < kernelSize; y++)
        {
            for (var x = 0; x < kernelSize; x++)
            {
                var dx = x - radius * lean;
                var dy = y - radius * (1.9 - lean);
                values[y * kernelSize + x] = Math.Exp(-(dx * dx * 0.4 + dy * dy * 0.15) / scale);
            }
        }

        values[(kernelSize - 1) * kernelSize] += 0.5;
        return values;
    }

    public static double[] Kernel(int radius)
    {
        var kernelSize = radius * 2 + 1;
        var values = new double[kernelSize * kernelSize];
        var scale = radius * radius + 1d;
        for (var y = 0; y < kernelSize; y++)
        {
            for (var x = 0; x < kernelSize; x++)
            {
                var dx = x - radius * 0.6;
                var dy = y - radius * 1.3;
                values[y * kernelSize + x] = Math.Exp(-(dx * dx * 0.4 + dy * dy * 0.15) / scale);
            }
        }

        values[(kernelSize - 1) * kernelSize] += 0.5;
        return values;
    }

    public static byte[] Pattern(int width, int height, int seed)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if ((x / 37 + y / 23) % 5 == 0)
                    continue;

                var hash = Hash((uint)x, (uint)y, (uint)seed);
                var alpha = x % 61 < 3 ? ByteColor.Mask : hash & ByteColor.Mask;
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)(alpha * (hash >> 8 & ByteColor.Mask) / ByteColor.Mask);
                pixels[offset + 1] = (byte)(alpha * (hash >> 16 & ByteColor.Mask) / ByteColor.Mask);
                pixels[offset + 2] = (byte)(alpha * (hash >> 24) / ByteColor.Mask);
                pixels[offset + 3] = (byte)alpha;
            }
        }

        return pixels;
    }

    public static void Samples(int width, int height, int validSize, Span<Int2> samples)
    {
        if (samples.Length < GpuTileConvolver.MaximumSamples)
            throw new ArgumentException(null, nameof(samples));
        samples[0] = new Int2(0, 0);
        samples[1] = new Int2(width - 1, 0);
        samples[2] = new Int2(0, height - 1);
        samples[3] = new Int2(width - 1, height - 1);
        samples[4] = new Int2(width / 2, height / 2);
        samples[5] = new Int2(Math.Min(validSize - 1, width - 1), Math.Min(validSize - 1, height - 1));
        samples[6] = new Int2(Math.Min(validSize, width - 1), Math.Min(validSize, height - 1));
        for (var index = 7; index < GpuTileConvolver.MaximumSamples; index++)
            samples[index] = new Int2((int)(index * 7919L % width), (int)(index * 104729L % height));
    }

    static int RunFull(GraphicsDevice device, GpuTileConvolver convolver, int size, bool chromatic, Span<ConvolutionMeasurement> measurements)
    {
        var spectrum = new KernelSpectrum();
        var spectra = new ChromaticKernelSpectrum();
        if (chromatic)
            spectra.Update(ChromaticKernel(FullRadius, ChromaticChannel.Red), ChromaticKernel(FullRadius, ChromaticChannel.Green), ChromaticKernel(FullRadius, ChromaticChannel.Blue), FullRadius, size);
        else
            spectrum.Update(Kernel(FullRadius), FullRadius, size);
        var validSize = size - FullRadius * 2;
        var sourceWidth = validSize + 17;
        var sourceHeight = validSize + 13;
        var plan = TilePlan.Create(size, FullRadius, 0, 0, sourceWidth + Margin * 2, sourceHeight + Margin * 2);
        var source = Pattern(sourceWidth, sourceHeight, size);
        var first = Convolve(device, convolver, chromatic ? null : spectrum, chromatic ? spectra : null, plan, source, sourceWidth, sourceHeight, measurements[2..]);
        var second = Convolve(device, convolver, chromatic ? null : spectrum, chromatic ? spectra : null, plan, source, sourceWidth, sourceHeight, []);

        var different = 0;
        for (var index = 0; index < first.Length; index++)
        {
            if (BitConverter.SingleToUInt32Bits(first[index]) != BitConverter.SingleToUInt32Bits(second[index]))
                different++;
        }
        measurements[0] = new ConvolutionMeasurement(chromatic ? ChromaticDeterminismName : DeterminismName, different, 0d, 0d);

        var norms = new (double RedGreen, double BlueAlpha)[plan.TileCount];
        for (var tile = 0; tile < plan.TileCount; tile++)
            norms[tile] = TileInput.Norms(source, Margin, Margin, sourceWidth, sourceHeight, plan, tile);

        Span<double> expected = stackalloc double[4];
        var relative = chromatic
            ? ConvolutionBound.ChromaticRelative(size, ConvolutionBound.GpuOperationError)
            : ConvolutionBound.Relative(size, ConvolutionBound.GpuOperationError);
        var flushed = chromatic ? ConvolutionBound.ChromaticFlushedOperations(size) : ConvolutionBound.FlushedOperations(size);
        var absolute = flushed * ConvolutionBound.SmallestNormal + DirectCorrelation.ErrorBound((FullRadius * 2 + 1) * (FullRadius * 2 + 1));
        var worst = 0d;
        for (var y = 0; y < plan.RegionHeight; y++)
        {
            for (var x = 0; x < plan.RegionWidth; x++)
            {
                if (chromatic)
                    DirectCorrelation.Evaluate(source, sourceWidth, sourceHeight, spectra.Red.Kernel, spectra.Green.Kernel, spectra.Blue.Kernel, FullRadius, x - Margin, y - Margin, expected);
                else
                    DirectCorrelation.Evaluate(source, sourceWidth, sourceHeight, spectrum.Kernel, FullRadius, x - Margin, y - Margin, expected);
                var (redGreen, blueAlpha) = norms[plan.TileAt(x, y)];
                for (var channel = 0; channel < 4; channel++)
                {
                    var bound = relative * (channel < 2 ? redGreen : blueAlpha) + absolute;
                    var error = Math.Abs(first[(y * plan.RegionWidth + x) * 4 + channel] - expected[channel]);
                    worst = Math.Max(worst, double.IsNaN(error) ? double.PositiveInfinity : error / bound);
                }
            }
        }
        measurements[1] = new ConvolutionMeasurement(chromatic ? ChromaticFullName : FullName, worst, 0d, 1d);
        return 2 + GpuTileCheck.MeasurementCount(GpuTileConvolver.MaximumSamples);
    }

    static int RunChecked(GraphicsDevice device, GpuTileConvolver convolver, int size, int radius, bool chromatic, Span<ConvolutionMeasurement> measurements)
    {
        var spectrum = new KernelSpectrum();
        var spectra = new ChromaticKernelSpectrum();
        if (chromatic)
            spectra.Update(ChromaticKernel(radius, ChromaticChannel.Red), ChromaticKernel(radius, ChromaticChannel.Green), ChromaticKernel(radius, ChromaticChannel.Blue), radius, size);
        else
            spectrum.Update(Kernel(radius), radius, size);
        var validSize = size - radius * 2;
        var sourceWidth = validSize + 9;
        var sourceHeight = validSize + 5;
        var margin = radius + Margin;
        var plan = TilePlan.Create(size, radius, 0, 0, sourceWidth + margin * 2, sourceHeight + margin * 2);
        var source = Pattern(sourceWidth, sourceHeight, size + radius);
        _ = Convolve(device, convolver, chromatic ? null : spectrum, chromatic ? spectra : null, plan, source, sourceWidth, sourceHeight, measurements, margin);
        return GpuTileCheck.MeasurementCount(GpuTileConvolver.MaximumSamples);
    }

    static float[] Convolve(
        GraphicsDevice device,
        GpuTileConvolver convolver,
        KernelSpectrum? spectrum,
        ChromaticKernelSpectrum? spectra,
        in TilePlan plan,
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        Span<ConvolutionMeasurement> measurements,
        int margin = Margin)
    {
        Span<Int2> samples = stackalloc Int2[GpuTileConvolver.MaximumSamples];
        Samples(plan.RegionWidth, plan.RegionHeight, plan.ValidSize, samples);
        if (spectra is not null)
            convolver.Upload(spectra);
        else
            convolver.Upload(spectrum!);
        var job = convolver.Prepare(plan, margin, margin, sourceWidth, sourceHeight, 1f, samples, true, default, spectra is not null);
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(sourceWidth, sourceHeight);
        sourceTexture.CopyFrom(MemoryMarshal.Cast<byte, Bgra32>(source));
        using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(plan.RegionWidth, plan.RegionHeight);
        using (var context = device.CreateComputeContext())
        {
            GpuTileConvolver.Record(
                in context, sourceTexture, output, convolver.Tiles, convolver.Twiddles, convolver.Spectrum,
                convolver.Report, convolver.Samples, convolver.StoreFor(job), job);
        }

        if (!measurements.IsEmpty)
        {
            if (spectra is not null)
                GpuTileCheck.Evaluate(convolver.ReadReport(job), job, samples, spectra.Red.Kernel, spectra.Green.Kernel, spectra.Blue.Kernel, measurements);
            else
                GpuTileCheck.Evaluate(convolver.ReadReport(job), job, samples, spectrum!.Kernel, measurements);
        }
        var values = new Float4[plan.RegionWidth * plan.RegionHeight];
        convolver.ReadStore(job, values);
        convolver.ReleaseStore();
        return MemoryMarshal.Cast<Float4, float>(values.AsSpan()).ToArray();
    }

    static uint Hash(uint x, uint y, uint seed)
    {
        var value = x * 0x9E3779B9u ^ y * 0x85EBCA6Bu ^ seed * 0xC2B2AE35u;
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }
}
