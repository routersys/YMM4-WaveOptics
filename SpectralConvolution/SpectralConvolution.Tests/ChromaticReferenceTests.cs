namespace SpectralConvolution.Tests;

public sealed class ChromaticReferenceTests
{
    static double[] Kernel(int radius, double scale)
    {
        var size = radius * 2 + 1;
        var values = new double[size * size];
        for (var index = 0; index < values.Length; index++)
            values[index] = (index + 1d) * scale;
        var sum = values.Sum();
        for (var index = 0; index < values.Length; index++)
            values[index] /= sum;
        values[0] += scale;
        return values;
    }

    static byte[] Image(int width, int height, int x, int y, byte blue, byte green, byte red, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        var offset = (y * width + x) * 4;
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = alpha;
        return pixels;
    }

    [Fact]
    public void EachChannelReadsItsOwnKernelAndTheAlphaReadsTheGreenKernel()
    {
        const int radius = 1;
        var red = Kernel(radius, 1d);
        var green = Kernel(radius, 2d);
        var blue = Kernel(radius, 5d);
        var image = Image(3, 3, 1, 1, 30, 20, 10, 40);
        var result = new double[4];

        DirectCorrelation.Evaluate(image, 3, 3, red, green, blue, radius, 1, 1, result);

        Assert.Equal(10 / 255d * red[4], result[0], 15);
        Assert.Equal(20 / 255d * green[4], result[1], 15);
        Assert.Equal(30 / 255d * blue[4], result[2], 15);
        Assert.Equal(40 / 255d * green[4], result[3], 15);
    }

    [Fact]
    public void IdenticalKernelsGiveTheSingleKernelResult()
    {
        const int radius = 2;
        var kernel = Kernel(radius, 3d);
        var random = new Random(4);
        var image = new byte[16 * 16 * 4];
        random.NextBytes(image);
        var single = new double[4];
        var chromatic = new double[4];

        DirectCorrelation.Evaluate(image, 16, 16, kernel, radius, 7, 9, single);
        DirectCorrelation.Evaluate(image, 16, 16, kernel, kernel, kernel, radius, 7, 9, chromatic);

        Assert.Equal(single, chromatic);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANeighborhoodIsReadWithTheKernelOfEachChannel(bool linear)
    {
        const int radius = 1;
        var red = Kernel(radius, 1d);
        var green = Kernel(radius, 2d);
        var blue = Kernel(radius, 5d);
        var random = new Random(8);
        var neighborhood = new uint[9];
        for (var index = 0; index < neighborhood.Length; index++)
            neighborhood[index] = DirectCorrelation.Pack((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        var light = new LightOptions(linear, false, 0.9f, 4f);
        var result = new double[4];

        DirectCorrelation.Evaluate(neighborhood, red, green, blue, in light, result);

        var expected = new double[4];
        for (var index = 0; index < neighborhood.Length; index++)
        {
            var packed = neighborhood[index];
            var (r, g, b, a) = linear
                ? LightTransform.FromBytes((byte)(packed & 255u), (byte)(packed >> 8 & 255u), (byte)(packed >> 16 & 255u), (byte)(packed >> 24), in light)
                : ((packed & 255u) / 255f, (packed >> 8 & 255u) / 255f, (packed >> 16 & 255u) / 255f, (packed >> 24) / 255f);
            expected[0] += r * red[index];
            expected[1] += g * green[index];
            expected[2] += b * blue[index];
            expected[3] += a * green[index];
        }

        Assert.Equal(expected[0], result[0], 6);
        Assert.Equal(expected[1], result[1], 6);
        Assert.Equal(expected[2], result[2], 6);
        Assert.Equal(expected[3], result[3], 6);
    }

    [Fact]
    public void KernelsOfDifferentLengthsAreRejected()
    {
        var short1 = Kernel(1, 1d);
        var long1 = Kernel(2, 1d);

        Assert.Throws<ArgumentException>(() => DirectCorrelation.Evaluate(new byte[36], 3, 3, short1, short1, long1, 1, 1, 1, new double[4]));
        Assert.Throws<ArgumentException>(() => DirectCorrelation.Evaluate(new uint[9], short1, long1, short1, default(LightOptions), new double[4]));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(512)]
    public void TheChromaticBoundCoversTheSplittingAndMergingOfChannels(int size)
    {
        foreach (var unit in new[] { ConvolutionBound.GpuOperationError, ConvolutionBound.CpuOperationError })
        {
            var plain = ConvolutionBound.Relative(size, unit);
            var chromatic = ConvolutionBound.ChromaticRelative(size, unit);

            Assert.True(chromatic > plain);
            Assert.True(chromatic < plain + 8d * unit);
            Assert.True(ConvolutionBound.ChromaticFlushedOperations(size) > ConvolutionBound.FlushedOperations(size));
        }
    }

    [Fact]
    public void TheChromaticAbsoluteBoundIsTheRelativeBoundOfTheNormPlusTheFlushedOperations()
    {
        const double norm = 123.5;
        var unit = ConvolutionBound.GpuOperationError;

        var expected = ConvolutionBound.ChromaticRelative(128, unit) * norm + ConvolutionBound.ChromaticFlushedOperations(128) * ConvolutionBound.SmallestNormal;

        Assert.Equal(expected, ConvolutionBound.ChromaticAbsolute(128, unit, norm));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConvolutionBound.ChromaticAbsolute(128, unit, -1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConvolutionBound.ChromaticAbsolute(128, unit, double.NaN));
    }
}
