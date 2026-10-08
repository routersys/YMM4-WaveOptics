namespace SpectralConvolution.Tests;

public sealed class TileShaderMathTests
{
    [Theory]
    [InlineData(64, 3, 100, 100)]
    [InlineData(128, 5, 300, 200)]
    [InlineData(512, 40, 1920, 1080)]
    public void TheTileOriginIsTheValidOriginMinusTheRadius(int size, int radius, int width, int height)
    {
        var plan = TilePlan.Create(size, radius, 7, 11, width, height);

        for (var tile = 0; tile < plan.TileCount; tile++)
        {
            var origin = TileShaderMath.TileOrigin(tile, plan.TilesX, plan.ValidSize, plan.Radius);

            Assert.Equal(plan.OriginX(tile) - plan.RegionX - plan.Radius, origin.X);
            Assert.Equal(plan.OriginY(tile) - plan.RegionY - plan.Radius, origin.Y);
        }
    }

    [Theory]
    [InlineData(64, 0)]
    [InlineData(128, 3)]
    [InlineData(512, 9)]
    public void TheTileIndexAddsTheGroupOffsetToTheBatchStart(int size, int batchStart)
    {
        var plan = TilePlan.Create(size, 1, 0, 0, 300, 300);

        for (var tile = 0; tile < plan.TileCount; tile++)
            Assert.Equal(batchStart + tile, TileShaderMath.TileIndex(batchStart, tile * plan.TileElements, plan.Log2Size));
    }
}
