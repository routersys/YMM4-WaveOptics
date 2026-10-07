using ComputeWeave;

namespace SpectralConvolution.Tests;

public sealed class ByteColorTests
{
    [Fact]
    public void TheByteRangeIsZeroToTwoHundredFiftyFive()
    {
        Assert.Equal(256, ByteColor.Levels);
        Assert.Equal(255, ByteColor.Maximum);
        Assert.Equal(255u, ByteColor.Mask);
        Assert.Equal(255f, ByteColor.Scale);
        Assert.Equal(255d, ByteColor.ScaleDouble);
    }

    [Theory]
    [InlineData(0f, 0u)]
    [InlineData(1f, 255u)]
    [InlineData(0.5f, 128u)]
    [InlineData(0.498f, 127u)]
    [InlineData(0.002f, 1u)]
    [InlineData(0.001f, 0u)]
    public void AValueBecomesTheNearestLevel(float value, uint expected)
    {
        Assert.Equal(expected, ByteColor.ToLevel(value));
    }

    [Fact]
    public void ChannelsArePackedFromTheLowestByteFirst()
    {
        var packed = ByteColor.Pack(new Float4(1f / 255f, 2f / 255f, 3f / 255f, 4f / 255f));

        Assert.Equal(0x04030201u, packed);
        for (var index = 0; index < 4; index++)
            Assert.Equal((uint)index + 1u, ByteColor.Channel(packed, index));
    }

    [Fact]
    public void PackingMatchesTheDirectCorrelationLayout()
    {
        var packed = ByteColor.Pack(new Float4(10f / 255f, 20f / 255f, 30f / 255f, 40f / 255f));

        Assert.Equal(DirectCorrelation.Pack(10, 20, 30, 40), packed);
    }
}
