namespace SpectralConvolution.Tests;

public sealed class ConvolutionBoundTests
{
    [Theory]
    [InlineData(64, 0.296, 0.166)]
    [InlineData(128, 0.688, 0.385)]
    [InlineData(256, 1.566, 0.877)]
    [InlineData(512, 3.514, 1.966)]
    public void TheWorstCaseTileStaysWithinTheDerivedNumberOfLevels(int size, double gpuLevels, double cpuLevels)
    {
        var norm = Math.Sqrt(2d * size * size);

        Assert.Equal(gpuLevels, ConvolutionBound.Relative(size, ConvolutionBound.GpuOperationError) * norm * 255d, 3);
        Assert.Equal(cpuLevels, ConvolutionBound.Relative(size, ConvolutionBound.CpuOperationError) * norm * 255d, 3);
    }

    [Fact]
    public void TheBoundGrowsWithTheTileAndTheOperationError()
    {
        int[] sizes = [64, 128, 256, 512];
        for (var index = 1; index < sizes.Length; index++)
        {
            Assert.True(ConvolutionBound.Relative(sizes[index], ConvolutionBound.GpuOperationError)
                > ConvolutionBound.Relative(sizes[index - 1], ConvolutionBound.GpuOperationError));
            Assert.True(ConvolutionBound.FlushedOperations(sizes[index]) > ConvolutionBound.FlushedOperations(sizes[index - 1]));
        }

        foreach (var size in sizes)
            Assert.True(ConvolutionBound.Relative(size, ConvolutionBound.GpuOperationError) > ConvolutionBound.Relative(size, ConvolutionBound.CpuOperationError));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(512)]
    public void TheSpectrumErrorIsDominatedBySingleRounding(int size)
    {
        var error = ConvolutionBound.SpectrumError(size);

        Assert.True(error > ConvolutionBound.SingleRounding);
        Assert.True(error < ConvolutionBound.SingleRounding + 1e-10);
    }

    [Fact]
    public void TheTwiddleErrorCoversRoundingToSinglePrecision()
    {
        Assert.True(ConvolutionBound.SingleTwiddleError > ConvolutionBound.SingleRounding);
        Assert.True(ConvolutionBound.DoubleTwiddleError > ConvolutionBound.TwiddleAngleError);
        Assert.True(ConvolutionBound.DoubleTwiddleError < 1e-14);
    }

    [Fact]
    public void GammaMatchesItsDefinition()
    {
        Assert.Equal(0d, ConvolutionBound.Gamma(0, ConvolutionBound.GpuOperationError));
        var unit = ConvolutionBound.GpuOperationError;
        Assert.Equal(3d * unit / (1d - 3d * unit), ConvolutionBound.Gamma(3, unit));
    }

    [Theory]
    [InlineData(-1, 0.1)]
    [InlineData(10, 0.1)]
    [InlineData(1, -0.1)]
    [InlineData(1, double.NaN)]
    public void GammaRejectsMeaninglessArguments(int operations, double unit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConvolutionBound.Gamma(operations, unit));
    }

    [Fact]
    public void FlushingAddsFarLessThanOneLevel()
    {
        Assert.True(ConvolutionBound.FlushedOperations(512) * ConvolutionBound.SmallestNormal < 1e-30);
        Assert.True(ConvolutionBound.Absolute(512, ConvolutionBound.GpuOperationError, 0d) > 0d);
    }

    [Fact]
    public void TheAbsoluteBoundScalesWithTheNorm()
    {
        var size = 128;
        var flush = ConvolutionBound.FlushedOperations(size) * ConvolutionBound.SmallestNormal;
        var relative = ConvolutionBound.Relative(size, ConvolutionBound.GpuOperationError);

        Assert.Equal(relative * 3d + flush, ConvolutionBound.Absolute(size, ConvolutionBound.GpuOperationError, 3d));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnInvalidNormIsRejected(double norm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConvolutionBound.Absolute(128, ConvolutionBound.GpuOperationError, norm));
    }
}
