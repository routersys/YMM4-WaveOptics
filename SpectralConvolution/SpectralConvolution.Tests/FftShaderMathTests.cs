using System.Numerics;

namespace SpectralConvolution.Tests;

public sealed class FftShaderMathTests
{
    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    public void EveryRadix2StageTouchesEachElementOfTwoRowsOnce(int size)
    {
        var log2Size = BitOperations.Log2((uint)size);
        for (var stage = 0; stage < log2Size; stage++)
        {
            var touched = new int[size * 2];
            for (var index = 0; index < size; index++)
            {
                var even = FftShaderMath.Radix2Even(index, log2Size, stage);
                touched[even]++;
                touched[even + FftShaderMath.Pow2(stage)]++;
            }

            Assert.All(touched, count => Assert.Equal(1, count));
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    public void EveryRadix4StageTouchesEachElementOfFourRowsOnce(int size)
    {
        var log2Size = BitOperations.Log2((uint)size);
        for (var stage = 0; stage + 1 < log2Size; stage++)
        {
            var step = FftShaderMath.Pow2(stage);
            var touched = new int[size * FftShaderMath.Radix4Elements];
            for (var index = 0; index < size; index++)
            {
                var first = FftShaderMath.Radix4First(index, log2Size, stage);
                for (var member = 0; member < FftShaderMath.Radix4Elements; member++)
                    touched[first + member * step]++;
            }

            Assert.All(touched, count => Assert.Equal(1, count));
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    public void EveryTwiddleIndexStaysInsideTheTable(int size)
    {
        var log2Size = BitOperations.Log2((uint)size);
        for (var stage = 0; stage < log2Size; stage++)
        {
            for (var index = 0; index < size; index++)
                Assert.InRange(FftShaderMath.TwiddleIndex(FftShaderMath.Position(index, stage), log2Size, stage), 0, size / 2 - 1);
        }

        for (var stage = 0; stage + 1 < log2Size; stage++)
        {
            var step = FftShaderMath.Pow2(stage);
            for (var index = 0; index < size; index++)
            {
                var position = FftShaderMath.Position(index, stage);
                Assert.InRange(FftShaderMath.TwiddleIndex(position, log2Size, stage + 1), 0, size / 2 - 1);
                Assert.InRange(FftShaderMath.TwiddleIndex(position + step, log2Size, stage + 1), 0, size / 2 - 1);
            }
        }
    }

    [Fact]
    public void AGroupHoldsOneRadix4UnitForEveryFourElements()
    {
        Assert.Equal(4, FftShaderMath.Radix4Elements);
        Assert.Equal(TilePlan.GroupThreads / 2, FftShaderMath.StageUnitsPerGroup);
        Assert.Equal(TilePlan.GroupThreads, TilePlan.ChromaticGroupElements / FftShaderMath.Radix4Elements);
    }
}
