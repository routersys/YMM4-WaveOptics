using System.Runtime.InteropServices;
using ComputeWeave;
using SpectralConvolution;
using WaveOptics.Effects;
using WaveOptics.Rendering;

namespace WaveOptics.Tests;

[Collection("Direct3D12")]
public sealed class WaveOpticsChromaticPipelineTests
{
    const int Width = 160;
    const int Height = 144;
    const int White = unchecked((int)0xFFFFFFFF);
    const int Opaque = unchecked((int)0xFFC0A080);

    static WaveOpticsPipeline.Parameters Parameters(
        WaveOpticsColorMode mode,
        int kernelRadius = 15,
        float defocus = 0.5f,
        float wavelength = 550f,
        float fNumber = 8f,
        float pixelPitch = 4f,
        WaveOpticsQuality quality = WaveOpticsQuality.Standard,
        LightOptions light = default,
        float coma = 0.3f)
        => new(1f, new WaveOpticsPipeline.PsfParameters(
            quality, kernelRadius, wavelength, fNumber, pixelPitch, WaveOpticsApertureShape.Circular, 6, 0f, 0f, defocus, 0f, 0f, coma, 0f, 0f, mode), light);

    static int[] Square(int left, int top, int size, int color)
    {
        var pixels = new int[Width * Height];
        for (var y = top; y < top + size; y++)
        {
            for (var x = left; x < left + size; x++)
                pixels[y * Width + x] = color;
        }

        return pixels;
    }

    static int[] Pattern()
    {
        var pixels = new int[Width * Height];
        for (var y = 50; y < 90; y++)
        {
            for (var x = 40; x < 110; x++)
                pixels[y * Width + x] = (x + y) % 7 == 0 ? 0 : Opaque;
        }

        return pixels;
    }

    static int[] RenderCpu(WaveOpticsCpuPipeline pipeline, int[] source, in WaveOpticsPipeline.Parameters parameters)
    {
        var destination = new int[source.Length];
        pipeline.Process(source, destination, Width, Height, in parameters);
        return destination;
    }

    static int[] RenderGpu(WaveOpticsPipeline pipeline, int[] source, in WaveOpticsPipeline.Parameters parameters)
    {
        var destination = new int[source.Length];
        pipeline.Process(source, destination, Width, Height, in parameters);
        return destination;
    }

    static byte Channel(int pixel, int shift) => (byte)(pixel >> shift);

    public static TheoryData<WaveOpticsColorMode, int, WaveOpticsQuality> Modes => new()
    {
        { WaveOpticsColorMode.Primaries, 15, WaveOpticsQuality.Standard },
        { WaveOpticsColorMode.Primaries, 40, WaveOpticsQuality.High },
        { WaveOpticsColorMode.Broadband, 15, WaveOpticsQuality.Draft },
        { WaveOpticsColorMode.Broadband, 30, WaveOpticsQuality.Standard },
    };

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheCpuAndTheGpuDrawTheSameImageWithinTwoLevels(WaveOpticsColorMode mode, int kernelRadius, WaveOpticsQuality quality)
    {
        using var cpu = new WaveOpticsCpuPipeline(3);
        using var gpu = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(gpu);
        var source = Pattern();
        var parameters = Parameters(mode, kernelRadius, quality: quality);

        var expected = RenderGpu(gpu, source, in parameters);
        var actual = RenderCpu(cpu, source, in parameters);

        var expectedBytes = MemoryMarshal.AsBytes(expected.AsSpan());
        var actualBytes = MemoryMarshal.AsBytes(actual.AsSpan());
        for (var index = 0; index < expectedBytes.Length; index++)
            Assert.InRange(actualBytes[index] - expectedBytes[index], -2, 2);
        Assert.Contains(actualBytes.ToArray(), value => value > 0);
    }

    [Theory]
    [InlineData(WaveOpticsColorMode.Primaries, 15)]
    [InlineData(WaveOpticsColorMode.Primaries, 40)]
    [InlineData(WaveOpticsColorMode.Broadband, 30)]
    public void AHealthyChromaticConvolutionPassesEveryCheck(WaveOpticsColorMode mode, int kernelRadius)
    {
        using var pipeline = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(pipeline);
        var device = GraphicsDevice.GetDefault();
        using var source = device.AllocateReadWriteTexture2D<Bgra32, Float4>(Width, Height);
        source.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(Pattern().AsSpan()));
        using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(Width, Height);
        var parameters = Parameters(mode, kernelRadius);
        var measurements = new ConvolutionMeasurement[WaveOpticsPipeline.MeasurementCount];

        pipeline.Simulate(source, Width, Height, 0, 0, Width, Height, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(Width, Height, in parameters, out var rect));
        var count = pipeline.RenderVisible(source, output, rect, in parameters, measurements);

        Assert.Equal(WaveOpticsPipeline.MeasurementCount, count);
        Assert.All(measurements, measurement => Assert.True(measurement.Passes, $"{measurement}"));
    }

    [Fact]
    public void ACorruptedSpectrumOfTheChromaticModeIsFoundByTheCheck()
    {
        using var pipeline = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(pipeline);
        var device = GraphicsDevice.GetDefault();
        using var source = device.AllocateReadWriteTexture2D<Bgra32, Float4>(Width, Height);
        source.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(Pattern().AsSpan()));
        using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(Width, Height);
        var parameters = Parameters(WaveOpticsColorMode.Primaries, 40);
        var measurements = new ConvolutionMeasurement[WaveOpticsPipeline.MeasurementCount];
        pipeline.SpectrumTamper = values =>
        {
            var copy = (Float2[])values.Clone();
            var blueStart = 2 * copy.Length / 3;
            for (var index = blueStart; index < copy.Length; index++)
                copy[index] = new Float2(copy[index].X * 0.5f, copy[index].Y * 0.5f);
            return copy;
        };

        pipeline.Simulate(source, Width, Height, 0, 0, Width, Height, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(Width, Height, in parameters, out var rect));
        pipeline.RenderVisible(source, output, rect, in parameters, measurements);

        Assert.Contains(measurements, measurement => !measurement.Passes && measurement.Name.EndsWith("blue", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARedChannelSpreadsFurtherThanABlueChannelAroundAWhiteSquare(bool onTheGpu)
    {
        var source = Square(60, 50, 40, White);
        var parameters = Parameters(WaveOpticsColorMode.Primaries, 30, defocus: 0f, fNumber: 16f, pixelPitch: 2f, coma: 0f);
        int[] result;
        if (onTheGpu)
        {
            using var gpu = WaveOpticsPipeline.TryCreate();
            Assert.NotNull(gpu);
            result = RenderGpu(gpu, source, in parameters);
        }
        else
        {
            using var cpu = new WaveOpticsCpuPipeline(2);
            result = RenderCpu(cpu, source, in parameters);
        }

        var red = 0d;
        var green = 0d;
        var blue = 0d;
        for (var y = 38; y < 112; y++)
        {
            for (var x = 48; x < 112; x++)
            {
                if (x >= 58 && x < 102 && y >= 48 && y < 92)
                    continue;
                var pixel = result[y * Width + x];
                red += Channel(pixel, 16);
                green += Channel(pixel, 8);
                blue += Channel(pixel, 0);
            }
        }

        Assert.True(red > green && green > blue, $"{red} {green} {blue}");
    }

    [Fact]
    public void AMonochromeFrameAfterAChromaticFrameIsTheMonochromeFrameOfAFreshPipeline()
    {
        var source = Pattern();
        var mono = Parameters(WaveOpticsColorMode.Monochrome);
        var chromatic = Parameters(WaveOpticsColorMode.Primaries);
        using var cpu = new WaveOpticsCpuPipeline(2);
        using var gpu = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(gpu);
        using var freshCpu = new WaveOpticsCpuPipeline(2);
        using var freshGpu = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(freshGpu);

        _ = RenderCpu(cpu, source, in mono);
        _ = RenderCpu(cpu, source, in chromatic);
        _ = RenderGpu(gpu, source, in mono);
        _ = RenderGpu(gpu, source, in chromatic);

        Assert.Equal(RenderCpu(freshCpu, source, in mono), RenderCpu(cpu, source, in mono));
        Assert.Equal(RenderGpu(freshGpu, source, in mono), RenderGpu(gpu, source, in mono));
    }

    [Theory]
    [InlineData(WaveOpticsColorMode.Primaries)]
    [InlineData(WaveOpticsColorMode.Broadband)]
    public void TheWavelengthDoesNotChangeAChromaticFrame(WaveOpticsColorMode mode)
    {
        var source = Pattern();
        using var cpu = new WaveOpticsCpuPipeline(2);
        var first = Parameters(mode, wavelength: 450f);
        var second = Parameters(mode, wavelength: 700f);

        Assert.Equal(RenderCpu(cpu, source, in first), RenderCpu(cpu, source, in second));
    }

    [Fact]
    public void AChromaticFrameIsLinearLightAware()
    {
        var source = Square(60, 50, 4, White);
        var light = new LightOptions(true, false, 0.5f, 20f);
        using var cpu = new WaveOpticsCpuPipeline(2);
        using var gpu = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(gpu);
        var parameters = Parameters(WaveOpticsColorMode.Primaries, 30, defocus: 0f, fNumber: 16f, pixelPitch: 2f, light: light);

        var expected = RenderGpu(gpu, source, in parameters);
        var actual = RenderCpu(cpu, source, in parameters);

        var expectedBytes = MemoryMarshal.AsBytes(expected.AsSpan());
        var actualBytes = MemoryMarshal.AsBytes(actual.AsSpan());
        for (var index = 0; index < expectedBytes.Length; index++)
            Assert.InRange(actualBytes[index] - expectedBytes[index], -3, 3);
    }

    [Theory]
    [InlineData(WaveOpticsColorMode.Primaries)]
    [InlineData(WaveOpticsColorMode.Broadband)]
    public void AWarmChromaticFrameAllocatesNothingOnTheCallingThread(WaveOpticsColorMode mode)
    {
        using var pipeline = new WaveOpticsCpuPipeline(3);
        var sources = new[] { MemoryMarshal.AsBytes(Square(30, 28, 36, Opaque).AsSpan()).ToArray(), MemoryMarshal.AsBytes(Square(31, 28, 36, Opaque).AsSpan()).ToArray() };
        var parameters = Parameters(mode);
        for (var warmUp = 0; warmUp < 4; warmUp++)
        {
            pipeline.Simulate(sources[warmUp % 2], Width, Height, 0, 0, Width, Height, in parameters);
            pipeline.TryGetVisibleBounds(Width, Height, in parameters, out var warmRect);
            pipeline.RenderVisible(warmRect, in parameters);
        }

        void RenderFrames()
        {
            for (var frame = 0; frame < 8; frame++)
            {
                pipeline.Simulate(sources[frame % 2], Width, Height, 0, 0, Width, Height, in parameters);
                pipeline.TryGetVisibleBounds(Width, Height, in parameters, out var rect);
                pipeline.RenderVisible(rect, in parameters);
            }
        }

        RenderFrames();
        AllocationProbe.Settle();

        Assert.Equal(0L, AllocationProbe.MinimumAllocatedBytes(RenderFrames, 8));
    }
}
