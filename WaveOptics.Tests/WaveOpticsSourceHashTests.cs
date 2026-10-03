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
}
