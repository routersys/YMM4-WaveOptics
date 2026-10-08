using ComputeWeave;

namespace SpectralConvolution.Tests;

public sealed class CpuFftTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void TheVectorButterfliesMatchTheScalarOnesBitForBit(int log2)
    {
        var size = 1 << log2;
        var twiddles = new Float2[Math.Max(size / 2, 1)];
        for (var index = 0; index < twiddles.Length; index++)
        {
            var angle = -2d * Math.PI * index / size;
            twiddles[index] = new Float2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        var random = new Random(log2 * 101);
        foreach (var direction in new[] { 1f, -1f })
        {
            var expected = new float[size * 4];
            for (var index = 0; index < expected.Length; index++)
            {
                expected[index] = random.Next(7) switch
                {
                    0 => 0f,
                    1 => -0f,
                    2 => (float)(random.NextDouble() * 1e-30),
                    3 => (float)(random.NextDouble() * 1e6 - 5e5),
                    _ => (float)(random.NextDouble() * 2 - 1),
                };
            }

            var actual = (float[])expected.Clone();

            var narrow = (float[])expected.Clone();

            CpuFft.ButterfliesScalar(expected, log2, twiddles, direction);
            CpuFft.Butterflies(actual, log2, twiddles, direction);
            CpuFft.Butterflies(narrow, log2, twiddles, direction, allowWide: false);

            Assert.Equal(expected.Select(BitConverter.SingleToInt32Bits), actual.Select(BitConverter.SingleToInt32Bits));
            Assert.Equal(expected.Select(BitConverter.SingleToInt32Bits), narrow.Select(BitConverter.SingleToInt32Bits));
        }
    }

    [Fact]
    public void TheButterfliesRefuseALineThatIsTooShort()
    {
        var twiddles = new Float2[4];

        Assert.Throws<ArgumentException>(() => CpuFft.Butterflies(new float[8 * 4 - 1], 3, twiddles, 1f));
        Assert.Throws<ArgumentException>(() => CpuFft.Butterflies(new float[8 * 4], 3, twiddles.AsSpan(0, 3), 1f));
    }

    [Theory]
    [InlineData(1, new[] { 0, 1 })]
    [InlineData(2, new[] { 0, 2, 1, 3 })]
    [InlineData(3, new[] { 0, 4, 2, 6, 1, 5, 3, 7 })]
    public void TheReversalListsTheIndicesInBitReversedOrder(int log2, int[] expected)
    {
        var order = new int[1 << log2];

        CpuFft.FillReversal(order, log2);

        Assert.Equal(expected, order);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(9)]
    public void ReversingTwiceReturnsTheSameIndex(int log2)
    {
        var order = new int[1 << log2];

        CpuFft.FillReversal(order, log2);

        for (var index = 0; index < order.Length; index++)
            Assert.Equal(index, order[order[index]]);
    }
}
