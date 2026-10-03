namespace SpectralConvolution.Tests;

public sealed class DirectCorrelationTests
{
    static double[] Kernel(int radius)
    {
        var size = radius * 2 + 1;
        var values = new double[size * size];
        for (var index = 0; index < values.Length; index++)
            values[index] = index + 1d;
        var sum = values.Sum();
        for (var index = 0; index < values.Length; index++)
            values[index] /= sum;
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
    public void PackingPutsRedInTheLowestByte()
    {
        Assert.Equal(0x44332211u, DirectCorrelation.Pack(0x11, 0x22, 0x33, 0x44));
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(9, 11)]
    [InlineData(12, 8)]
    public void AnImpulseReadsTheKernelAtTheOffsetFromThePoint(int x, int y)
    {
        const int radius = 2;
        var kernel = Kernel(radius);
        var image = Image(20, 20, 10, 10, 0, 0, 255, 0);
        var result = new double[4];

        DirectCorrelation.Evaluate(image, 20, 20, kernel, radius, x, y, result);

        var offsetX = 10 - x;
        var offsetY = 10 - y;
        Assert.Equal(kernel[(offsetY + radius) * 5 + offsetX + radius], result[0], 15);
        Assert.Equal(0d, result[1]);
        Assert.Equal(0d, result[2]);
        Assert.Equal(0d, result[3]);
    }

    [Fact]
    public void ChannelsKeepTheirOrder()
    {
        const int radius = 1;
        var kernel = Kernel(radius);
        var image = Image(3, 3, 1, 1, 30, 20, 10, 40);
        var result = new double[4];

        DirectCorrelation.Evaluate(image, 3, 3, kernel, radius, 1, 1, result);

        var center = kernel[4];
        Assert.Equal(10 / 255d * center, result[0], 15);
        Assert.Equal(20 / 255d * center, result[1], 15);
        Assert.Equal(30 / 255d * center, result[2], 15);
        Assert.Equal(40 / 255d * center, result[3], 15);
    }

    [Fact]
    public void PixelsOutsideTheImageCountAsZero()
    {
        const int radius = 2;
        var kernel = Kernel(radius);
        var image = new byte[4 * 4 * 4];
        Array.Fill(image, (byte)255);
        var result = new double[4];

        DirectCorrelation.Evaluate(image, 4, 4, kernel, radius, 0, 0, result);

        var expected = 0d;
        for (var offsetY = 0; offsetY <= radius; offsetY++)
        {
            for (var offsetX = 0; offsetX <= radius; offsetX++)
                expected += kernel[(offsetY + radius) * 5 + offsetX + radius];
        }
        Assert.Equal(expected, result[3], 14);
    }

    [Fact]
    public void AGatheredNeighborhoodGivesTheSameValueAsTheImage()
    {
        const int radius = 3;
        const int width = 23;
        const int height = 17;
        var kernel = Kernel(radius);
        var random = new Random(7);
        var image = new byte[width * height * 4];
        random.NextBytes(image);
        var neighborhood = new uint[kernel.Length];
        const int x = 2;
        const int y = 15;
        for (var offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                var sourceX = x + offsetX;
                var sourceY = y + offsetY;
                if (sourceX < 0 || sourceX >= width || sourceY < 0 || sourceY >= height)
                    continue;
                var offset = (sourceY * width + sourceX) * 4;
                neighborhood[(offsetY + radius) * 7 + offsetX + radius] = DirectCorrelation.Pack(image[offset + 2], image[offset + 1], image[offset], image[offset + 3]);
            }
        }
        var fromImage = new double[4];
        var fromNeighborhood = new double[4];

        DirectCorrelation.Evaluate(image, width, height, kernel, radius, x, y, fromImage);
        DirectCorrelation.Evaluate(neighborhood, kernel, fromNeighborhood);

        Assert.Equal(fromImage, fromNeighborhood);
    }

    [Fact]
    public void MismatchedLengthsAreRejected()
    {
        var kernel = Kernel(1);

        Assert.Throws<ArgumentException>(() => DirectCorrelation.Evaluate(new uint[8], kernel, new double[4]));
        Assert.Throws<ArgumentException>(() => DirectCorrelation.Evaluate(new uint[9], kernel, new double[3]));
        Assert.Throws<ArgumentException>(() => DirectCorrelation.Evaluate(new byte[16], 2, 2, kernel, 2, 0, 0, new double[4]));
        Assert.Throws<ArgumentException>(() => DirectCorrelation.Evaluate(new byte[15], 2, 2, kernel, 1, 0, 0, new double[4]));
    }

    [Fact]
    public void TheReferenceErrorIsTiny()
    {
        var bound = DirectCorrelation.ErrorBound(127 * 127);

        Assert.True(bound > 0d);
        Assert.True(bound < 1e-11);
        Assert.Throws<ArgumentOutOfRangeException>(() => DirectCorrelation.ErrorBound(0));
    }
}
