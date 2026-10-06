using System.Runtime.InteropServices;
using ComputeWeave;
using WaveOptics.Effects;
using WaveOptics.Rendering;

namespace WaveOptics.Tests;

[Collection("Direct3D12")]
public sealed class WaveOpticsSourceHashTests
{
    static readonly WaveOpticsPipeline.Parameters Defaults = new(1f, new WaveOpticsPipeline.PsfParameters(
        WaveOpticsQuality.Draft, 3, 550f, 8f, 4f, WaveOpticsApertureShape.Circular, 6, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f));

    static int[] RandomPixels(int width, int height, int seed, double litFraction)
    {
        var random = new Random(seed);
        var pixels = new int[width * height];
        for (var index = 0; index < pixels.Length; index++)
        {
            if (random.NextDouble() < litFraction)
                pixels[index] = random.Next(int.MinValue, int.MaxValue) | 1;
        }

        return pixels;
    }

    [Theory]
    [InlineData(37, 21, 0, 0, 0.3)]
    [InlineData(64, 48, 16, 12, 0.05)]
    [InlineData(19, 33, 4, 8, 1.0)]
    public void TheCpuHashMatchesTheGpuHash(int width, int height, int offsetX, int offsetY, double litFraction)
    {
        using var pipeline = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(pipeline);
        var pixels = RandomPixels(width, height, width * height, litFraction);
        using var texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        texture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(pixels.AsSpan()));
        var parameters = Defaults;

        pipeline.Simulate(texture, width + offsetX * 2, height + offsetY * 2, offsetX, offsetY, width, height, in parameters);

        var cpu = WaveOpticsSourceHash.Compute(MemoryMarshal.AsBytes(pixels.AsSpan()), offsetX, offsetY, width, height);
        Assert.Equal(pipeline.SourceHash, cpu);
    }

    [Fact]
    public void ATransparentSourceHasNothingToShow()
    {
        var hash = WaveOpticsSourceHash.Compute(new byte[16 * 16 * 4], 4, 4, 16, 16);

        Assert.Equal(0, hash.LitCount);
        Assert.False(hash.TryGetVisibleBounds(24, 24, 3, out _));
        Assert.Equal(WaveOpticsSourceHash.Empty, hash);
    }

    [Fact]
    public void TheBoundsAreReportedInCanvasCoordinates()
    {
        var pixels = new int[10 * 8];
        pixels[2 * 10 + 3] = -1;
        pixels[5 * 10 + 7] = 1 << 24;

        var hash = WaveOpticsSourceHash.Compute(MemoryMarshal.AsBytes(pixels.AsSpan()), 20, 30, 10, 8);

        Assert.Equal((2, 23, 32, 27, 35), (hash.LitCount, hash.MinimumX, hash.MinimumY, hash.MaximumX, hash.MaximumY));
    }

    [Fact]
    public void AShortSourceIsRejected()
    {
        Assert.Throws<ArgumentException>(() => WaveOpticsSourceHash.Compute(new byte[15], 0, 0, 2, 2));
    }

    [Theory]
    [InlineData(1, 1, 0.0)]
    [InlineData(1, 5, 1.0)]
    [InlineData(7, 3, 0.5)]
    [InlineData(8, 4, 0.01)]
    [InlineData(9, 4, 0.3)]
    [InlineData(15, 6, 1.0)]
    [InlineData(16, 6, 0.0)]
    [InlineData(17, 3, 0.02)]
    [InlineData(63, 9, 0.6)]
    [InlineData(64, 9, 1.0)]
    [InlineData(1000, 7, 0.001)]
    [InlineData(1000, 7, 0.9)]
    public void TheVectorHashMatchesTheScalarOneBitForBit(int width, int height, double litFraction)
    {
        var pixels = RandomPixels(width, height, width * 31 + height, litFraction);
        var bytes = MemoryMarshal.AsBytes(pixels.AsSpan());

        var expected = WaveOpticsSourceHash.ComputeScalar(bytes, 12, 7, width, height);
        var actual = WaveOpticsSourceHash.Compute(bytes, 12, 7, width, height);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(32)]
    public void ASingleLitPixelIsFoundWhereverItSits(int width)
    {
        const int Height = 3;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixels = new int[width * Height];
                pixels[y * width + x] = 0x01020304;
                var bytes = MemoryMarshal.AsBytes(pixels.AsSpan());

                var expected = WaveOpticsSourceHash.ComputeScalar(bytes, 5, 9, width, Height);
                var actual = WaveOpticsSourceHash.Compute(bytes, 5, 9, width, Height);

                Assert.Equal(expected, actual);
                Assert.Equal((1, 5 + x, 9 + y, 5 + x, 9 + y), (actual.LitCount, actual.MinimumX, actual.MinimumY, actual.MaximumX, actual.MaximumY));
            }
        }
    }
}
