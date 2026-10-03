namespace SpectralConvolution.Tests;

public sealed class TileInputTests
{
    [Fact]
    public void TheNormsCoverTheWholeTileIncludingItsApron()
    {
        var source = new byte[10 * 10 * 4];
        source[0] = 3;
        source[1] = 4;
        source[2] = 12;
        source[3] = 5;
        var plan = TilePlan.Create(64, 2, 2, 2, 100, 100);

        var (redGreen, blueAlpha) = TileInput.Norms(source, 0, 0, 10, 10, plan, 0);

        Assert.Equal(Math.Sqrt(12 * 12 + 4 * 4) / 255d, redGreen);
        Assert.Equal(Math.Sqrt(3 * 3 + 5 * 5) / 255d, blueAlpha);
    }

    [Fact]
    public void PixelsOutsideTheTileAreIgnored()
    {
        var source = ConvolutionScene.Uniform(200, 10, 255);
        var plan = TilePlan.Create(64, 2, 0, 0, 200, 10);

        var (redGreen, _) = TileInput.Norms(source, 0, 0, 200, 10, plan, 1);

        var columns = Math.Min(64, 200 - (plan.OriginX(1) - 2));
        Assert.Equal(Math.Sqrt(2d * columns * 10), redGreen, 12);
    }

    [Fact]
    public void AShortSourceIsRejected()
    {
        var plan = TilePlan.Create(64, 2, 0, 0, 10, 10);

        Assert.Throws<ArgumentException>(() => TileInput.Norms(new byte[10], 0, 0, 2, 2, plan, 0));
    }
}
