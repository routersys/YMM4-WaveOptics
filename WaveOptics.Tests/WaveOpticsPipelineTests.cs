using ComputeWeave;
using ComputeWeave.Interop;
using WaveOptics.Abstractions;
using WaveOptics.Effects;
using WaveOptics.Optics;
using WaveOptics.Rendering;

namespace WaveOptics.Tests;

[Collection("Direct3D12")]
public sealed class WaveOpticsPipelineTests
{
    const int Opaque = unchecked((int)0xFFC0C0C0);
    const int White = unchecked((int)0xFFFFFFFF);

    static readonly bool Direct3D12IsAvailable = GraphicsDevice.EnumerateDevices().Any();

    static WaveOpticsPipeline CreatePipeline()
    {
        if (!Direct3D12IsAvailable)
            Assert.Skip("Direct3D 12 is unavailable.");
        var pipeline = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(pipeline);
        return pipeline;
    }

    static WaveOpticsPipeline.Parameters Parameters(
        float gain = 1f,
        WaveOpticsQuality quality = WaveOpticsQuality.Standard,
        int kernelRadius = 15,
        float pixelPitch = 4f,
        float defocus = 0f,
        float comaHorizontal = 0f)
        => new(gain, new WaveOpticsPipeline.PsfParameters(
            quality, kernelRadius, 550f, 8f, pixelPitch, WaveOpticsApertureShape.Circular, 6, 0f, 0f,
            defocus, 0f, 0f, comaHorizontal, 0f, 0f));

    static int[] Square(int width, int height, int left, int top, int squareWidth, int squareHeight, int color = Opaque)
    {
        var pixels = new int[width * height];
        for (var y = Math.Max(top, 0); y < Math.Min(top + squareHeight, height); y++)
        {
            for (var x = Math.Max(left, 0); x < Math.Min(left + squareWidth, width); x++)
                pixels[y * width + x] = color;
        }

        return pixels;
    }

    static int Alpha(int pixel) => (pixel >> 24) & 255;

    static int Channel(int pixel, int shift) => (pixel >> shift) & 255;

    static (int Left, int Top, int Right, int Bottom) LitBounds(int[] pixels, int width)
    {
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
        for (var index = 0; index < pixels.Length; index++)
        {
            if (pixels[index] == 0)
                continue;
            left = Math.Min(left, index % width);
            top = Math.Min(top, index / width);
            right = Math.Max(right, index % width + 1);
            bottom = Math.Max(bottom, index / width + 1);
        }

        return (left, top, right, bottom);
    }

    static void Upload(ReadWriteTexture2D<Bgra32, Float4> texture, int[] pixels)
        => texture.CopyFrom(pixels.Select(pixel => new Bgra32 { PackedValue = unchecked((uint)pixel) }).ToArray());

    static int[] Render(WaveOpticsPipeline pipeline, int[] source, int width, int height, WaveOpticsPipeline.Parameters parameters)
    {
        var destination = new int[source.Length];
        pipeline.Process(source, destination, width, height, in parameters);
        return destination;
    }

    static int[] RenderSplit(WaveOpticsPipeline pipeline, ReadWriteTexture2D<Bgra32, Float4> source, int width, int height, WaveOpticsPipeline.Parameters parameters)
    {
        var device = GraphicsDevice.GetDefault();
        var pixels = new int[width * height];
        pipeline.Simulate(source, width, height, 0, 0, width, height, in parameters);
        if (!pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect))
            return pixels;

        using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(output, rect, in parameters);
        var visible = new Bgra32[rect.Width * rect.Height];
        output.CopyTo(visible);
        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
                pixels[(rect.Y + y) * width + rect.X + x] = unchecked((int)visible[y * rect.Width + x].PackedValue);
        }

        return pixels;
    }

    static PsfKernel Kernel(WaveOpticsPipeline.PsfParameters psf)
    {
        var gridSize = WaveOpticsSettings.GetPupilGridSize(psf.Quality);
        var descriptor = new PsfDescriptor(
            gridSize,
            WaveOpticsSettings.GetPupilDiameterSamples(gridSize),
            WaveOpticsSettings.GetKernelSize(psf.KernelRadius),
            psf.Wavelength,
            psf.FNumber,
            psf.PixelPitch,
            ApertureShape.Circular,
            psf.BladeCount,
            psf.BladeRotation,
            psf.Obstruction,
            new WavefrontAberration(defocusWaves: psf.Defocus, comaHorizontalWaves: psf.ComaHorizontal));
        return new FraunhoferPsfGenerator().Generate(descriptor).Kernel;
    }

    static SeparableKernel Decompose(WaveOpticsPipeline.PsfParameters psf)
    {
        var kernel = Kernel(psf);
        return SeparableKernel.Decompose(kernel.Values.Span, kernel.Size, WaveOpticsSettings.SeparableResidualRatio, WaveOpticsSettings.MaximumRank);
    }

    [Fact]
    public void ATransparentSourceStaysTransparent()
    {
        using var pipeline = CreatePipeline();
        var destination = Enumerable.Repeat(-1, 64 * 64).ToArray();
        var parameters = Parameters();

        pipeline.Process(new int[64 * 64], destination, 64, 64, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void AZeroGainLeavesNothing()
    {
        using var pipeline = CreatePipeline();
        var destination = Enumerable.Repeat(-1, 96 * 96).ToArray();
        var parameters = Parameters(gain: 0f);

        pipeline.Process(Square(96, 96, 32, 32, 32, 32), destination, 96, 96, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(0, 0)]
    [InlineData(32, 32)]
    [InlineData(3, 29)]
    public void AnImpulseSpreadsIntoTheSeparableKernel(int impulseX, int impulseY)
    {
        using var pipeline = CreatePipeline();
        var parameters = Parameters(kernelRadius: 6, pixelPitch: 1.5f, comaHorizontal: 0.4f);
        var separable = Decompose(parameters.Psf);
        var radius = separable.Size / 2;
        var source = Square(33, 33, impulseX, impulseY, 1, 1, White);

        var rendering = Render(pipeline, source, 33, 33, parameters);

        for (var y = 0; y < 33; y++)
        {
            for (var x = 0; x < 33; x++)
            {
                var expected = 0f;
                var row = impulseY - y + radius;
                var column = impulseX - x + radius;
                if (row >= 0 && row < separable.Size && column >= 0 && column < separable.Size)
                {
                    for (var term = 0; term < separable.Rank; term++)
                        expected += separable.Horizontal[term * separable.Size + column] * separable.Vertical[term * separable.Size + row];
                }

                var value = (int)Math.Round(Math.Clamp(expected / separable.Sum, 0d, 1d) * 255d, MidpointRounding.ToEven);
                Assert.InRange(Alpha(rendering[y * 33 + x]), value - 1, value + 1);
            }
        }
    }

    [Fact]
    public void AnImpulseSpreadsIntoAComaticPsfItself()
    {
        const int Size = 47;
        const int Center = Size / 2;
        const float Gain = 4f;
        using var pipeline = CreatePipeline();
        var parameters = Parameters(gain: Gain, comaHorizontal: 3f);
        var kernel = Kernel(parameters.Psf);
        var radius = kernel.Size / 2;
        var source = Square(Size, Size, Center, Center, 1, 1, White);

        var rendering = Render(pipeline, source, Size, Size, parameters);

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var column = Center - x + radius;
                var row = Center - y + radius;
                var expected = column >= 0 && column < kernel.Size && row >= 0 && row < kernel.Size ? kernel[column, row] : 0d;
                var value = (int)Math.Round(Math.Clamp(expected * Gain, 0d, 1d) * 255d, MidpointRounding.ToEven);
                Assert.InRange(Alpha(rendering[y * Size + x]), value - 1, value + 1);
            }
        }
    }

    [Fact]
    public void TheBlurKeepsTheLightOfTheSource()
    {
        using var pipeline = CreatePipeline();
        var source = Square(128, 128, 48, 48, 32, 32);

        var rendering = Render(pipeline, source, 128, 128, Parameters(defocus: 1f));

        var before = source.Sum(pixel => (long)Alpha(pixel));
        var after = rendering.Sum(pixel => (long)Alpha(pixel));
        Assert.InRange(after / (double)before, 0.99, 1.01);
    }

    [Fact]
    public void TheBlurReachesNoFurtherThanTheKernelRadius()
    {
        using var pipeline = CreatePipeline();
        var source = Square(96, 96, 40, 40, 16, 16);

        var bounds = LitBounds(Render(pipeline, source, 96, 96, Parameters(kernelRadius: 5, pixelPitch: 0.25f)), 96);

        Assert.Equal((40 - 5, 40 - 5, 56 + 5, 56 + 5), bounds);
    }

    [Fact]
    public void TheGainScalesTheBlurredImage()
    {
        using var pipeline = CreatePipeline();
        var source = Square(96, 96, 32, 32, 32, 32);

        var full = Render(pipeline, source, 96, 96, Parameters(defocus: 1f));
        var half = Render(pipeline, source, 96, 96, Parameters(gain: 0.5f, defocus: 1f));

        Assert.All(full.Zip(half), pair =>
        {
            foreach (var shift in new[] { 0, 8, 16, 24 })
                Assert.InRange(Channel(pair.Second, shift), Channel(pair.First, shift) / 2 - 1, Channel(pair.First, shift) / 2 + 1);
        });
    }

    [Fact]
    public void TheSameSettingsAlwaysProduceTheSameImage()
    {
        using var pipeline = CreatePipeline();
        var source = Square(128, 128, 48, 48, 32, 32);

        var first = Render(pipeline, source, 128, 128, Parameters(defocus: 0.5f));
        var second = Render(pipeline, source, 128, 128, Parameters(defocus: 0.5f));

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentOpticsBlurDifferently()
    {
        using var pipeline = CreatePipeline();
        var source = Square(128, 128, 48, 48, 32, 32);

        var sharp = Render(pipeline, source, 128, 128, Parameters());
        var defocused = Render(pipeline, source, 128, 128, Parameters(defocus: 1f));

        Assert.NotEqual(sharp, defocused);
    }

    [Fact]
    public void AWarmPipelineAllocatesNoManagedMemory()
    {
        using var pipeline = CreatePipeline();
        var source = Square(64, 64, 24, 24, 16, 16);
        var destination = new int[source.Length];
        var parameters = Parameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, 64, 64, in parameters);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, 64, 64, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, minimum);
    }

    [Fact]
    public void SharedTexturesProduceTheSameImageAsPackedBuffers()
    {
        using var pipeline = CreatePipeline();
        var source = Square(96, 96, 32, 32, 32, 32);
        var parameters = Parameters(defocus: 0.7f);
        var expected = Render(pipeline, source, 96, 96, parameters);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 96, 96);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 96, 96);
        Upload(sourceTexture, source);

        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, 96, 96, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        Assert.Equal(expected.Select(pixel => unchecked((uint)pixel)), result.Select(pixel => pixel.PackedValue));
    }

    [Fact]
    public void RepeatedSharedTextureSubmissionsAllocateNoManagedMemory()
    {
        using var pipeline = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 64, 64);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 64, 64);
        var parameters = Parameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, 64, 64, in parameters);
        pipeline.WaitForCompletion();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, 64, 64, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        pipeline.WaitForCompletion();

        Assert.Equal(0, minimum);
    }

    [Theory]
    [InlineData(15, 0f)]
    [InlineData(5, 1f)]
    [InlineData(1, 0f)]
    public void TheVisibleBoundsHoldTheWholeBlurAndRenderTheSameImage(int kernelRadius, float defocus)
    {
        using var pipeline = CreatePipeline();
        var source = Square(160, 144, 60, 52, 32, 24);
        var parameters = Parameters(kernelRadius: kernelRadius, defocus: defocus);
        var full = Render(pipeline, source, 160, 144, parameters);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(160, 144);
        Upload(sourceTexture, source);

        var split = RenderSplit(pipeline, sourceTexture, 160, 144, parameters);

        Assert.Equal(full, split);
    }

    [Fact]
    public void TheVisibleBoundsHugTheSourceOnAFourPixelGrid()
    {
        using var pipeline = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(160, 160);
        Upload(sourceTexture, Square(160, 160, 62, 59, 30, 21));
        var parameters = Parameters(kernelRadius: 9);

        pipeline.Simulate(sourceTexture, 160, 160, 0, 0, 160, 160, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(160, 160, in parameters, out var rect));

        Assert.Equal((0, 0, 0, 0), (rect.X % 4, rect.Y % 4, rect.Width % 4, rect.Height % 4));
        Assert.InRange(62 - 9 - rect.X, 0, 3);
        Assert.InRange(59 - 9 - rect.Y, 0, 3);
        Assert.InRange(rect.X + rect.Width - (62 + 30 + 9), 0, 3);
        Assert.InRange(rect.Y + rect.Height - (59 + 21 + 9), 0, 3);
    }

    [Fact]
    public void ASmallerKernelRadiusNarrowsTheVisibleBounds()
    {
        using var pipeline = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(160, 160);
        Upload(sourceTexture, Square(160, 160, 64, 64, 32, 32));
        var wide = Parameters(kernelRadius: 15);
        var narrow = Parameters(kernelRadius: 3);

        pipeline.Simulate(sourceTexture, 160, 160, 0, 0, 160, 160, in wide);
        Assert.True(pipeline.TryGetVisibleBounds(160, 160, in wide, out var wideRect));
        Assert.True(pipeline.TryGetVisibleBounds(160, 160, in narrow, out var narrowRect));

        Assert.True(narrowRect.X > wideRect.X && narrowRect.Y > wideRect.Y);
        Assert.True(narrowRect.X + narrowRect.Width < wideRect.X + wideRect.Width && narrowRect.Y + narrowRect.Height < wideRect.Y + wideRect.Height);
    }

    [Fact]
    public void NothingIsVisibleForATransparentSource()
    {
        using var pipeline = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(128, 128);
        Upload(sourceTexture, new int[128 * 128]);
        var parameters = Parameters();

        pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in parameters);

        Assert.False(pipeline.TryGetVisibleBounds(128, 128, in parameters, out _));
    }

    [Fact]
    public void APipelineUsedAtAnotherSizeDrawsLikeAFreshOne()
    {
        using var pipeline = CreatePipeline();
        using var fresh = CreatePipeline();
        var parameters = Parameters(defocus: 0.5f);
        Render(pipeline, Square(128, 64, 48, 16, 32, 32), 128, 64, parameters);
        var source = Square(64, 64, 16, 16, 32, 32);

        var reused = Render(pipeline, source, 64, 64, parameters);
        var expected = Render(fresh, source, 64, 64, parameters);

        Assert.Equal(expected, reused);
    }

    [Fact]
    public void ARunOfTheWholePipelineIsNotMistakenForTheCachedConvolution()
    {
        using var pipeline = CreatePipeline();
        using var fresh = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(96, 96);
        using var freshTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(96, 96);
        var source = Square(96, 96, 32, 32, 32, 32);
        Upload(sourceTexture, source);
        Upload(freshTexture, source);
        var parameters = Parameters(defocus: 1f);
        pipeline.Simulate(sourceTexture, 96, 96, 0, 0, 96, 96, in parameters);
        Render(pipeline, Square(96, 96, 8, 8, 16, 16), 96, 96, parameters);

        var reused = RenderSplit(pipeline, sourceTexture, 96, 96, parameters);
        var expected = RenderSplit(fresh, freshTexture, 96, 96, parameters);

        Assert.Equal(expected, reused);
    }

    [Fact]
    public void TheConvolutionIsRecomputedOnlyWhenTheSourceOrTheOpticsChange()
    {
        using var pipeline = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(128, 128);
        Upload(sourceTexture, Square(128, 128, 48, 48, 32, 32));
        var parameters = Parameters();

        Assert.True(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in parameters));
        Assert.False(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in parameters));

        var gainChanged = parameters with { Gain = 2f };
        Assert.False(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in gainChanged));

        var opticsChanged = parameters with { Psf = parameters.Psf with { Defocus = 0.5f } };
        Assert.True(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in opticsChanged));

        Upload(sourceTexture, Square(128, 128, 32, 32, 32, 32));
        Assert.True(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in opticsChanged));
    }
}
