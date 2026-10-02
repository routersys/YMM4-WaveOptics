namespace SpectralConvolution.Tests;

public sealed class TilePlanTests
{
    [Theory]
    [InlineData(0, 128)]
    [InlineData(1, 128)]
    [InlineData(23, 128)]
    [InlineData(24, 512)]
    [InlineData(63, 512)]
    [InlineData(255, 512)]
    public void TheTileSizeFollowsTheRadius(int radius, int expected)
    {
        Assert.Equal(expected, TilePlan.SelectSize(radius));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void ARadiusOutsideTheLargestTileIsRejected(int radius)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TilePlan.SelectSize(radius));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(96)]
    [InlineData(1024)]
    public void AnUnsupportedTileSizeIsRejected(int size)
    {
        Assert.False(TilePlan.IsSupportedSize(size));
        Assert.Throws<ArgumentOutOfRangeException>(() => TilePlan.Create(size, 1, 0, 0, 10, 10));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    public void EverySupportedTileSizeIsAccepted(int size)
    {
        Assert.True(TilePlan.IsSupportedSize(size));
        var plan = TilePlan.Create(size, 1, 0, 0, 10, 10);
        Assert.Equal(size, 1 << plan.Log2Size);
    }

    [Theory]
    [InlineData(64, 32)]
    [InlineData(128, 64)]
    public void AKernelAsWideAsTheTileIsRejected(int size, int radius)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TilePlan.Create(size, radius, 0, 0, 10, 10));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void AnEmptyRegionIsRejected(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TilePlan.Create(128, 1, 0, 0, width, height));
    }

    [Theory]
    [InlineData(128, 15, 1, 1)]
    [InlineData(128, 15, 98, 98)]
    [InlineData(128, 15, 99, 97)]
    [InlineData(512, 63, 1952, 1112)]
    [InlineData(64, 0, 64, 65)]
    public void TheTilesCoverTheRegionWithoutASpareTile(int size, int radius, int width, int height)
    {
        var plan = TilePlan.Create(size, radius, 5, 7, width, height);

        Assert.Equal(size - 2 * radius, plan.ValidSize);
        Assert.True((plan.TilesX - 1) * plan.ValidSize < width);
        Assert.True(plan.TilesX * plan.ValidSize >= width);
        Assert.True((plan.TilesY - 1) * plan.ValidSize < height);
        Assert.True(plan.TilesY * plan.ValidSize >= height);
    }

    [Fact]
    public void TileOriginsStepByTheValidSize()
    {
        var plan = TilePlan.Create(128, 15, 5, 7, 300, 200);

        Assert.Equal(5, plan.OriginX(0));
        Assert.Equal(7, plan.OriginY(0));
        Assert.Equal(5 + 98, plan.OriginX(1));
        Assert.Equal(7, plan.OriginY(1));
        Assert.Equal(5, plan.OriginX(plan.TilesX));
        Assert.Equal(7 + 98, plan.OriginY(plan.TilesX));
    }

    [Fact]
    public void EveryPixelOfTheRegionBelongsToTheTileWhoseValidAreaHoldsIt()
    {
        var plan = TilePlan.Create(64, 5, 3, 4, 157, 121);
        var covered = new int[plan.RegionWidth * plan.RegionHeight];

        for (var tile = 0; tile < plan.TileCount; tile++)
        {
            for (var y = plan.OriginY(tile); y < plan.OriginY(tile) + plan.ValidHeight(tile); y++)
            {
                for (var x = plan.OriginX(tile); x < plan.OriginX(tile) + plan.ValidWidth(tile); x++)
                {
                    Assert.Equal(tile, plan.TileAt(x, y));
                    covered[(y - plan.RegionY) * plan.RegionWidth + x - plan.RegionX]++;
                }
            }
        }

        Assert.All(covered, count => Assert.Equal(1, count));
    }

    [Theory]
    [InlineData(64, 256)]
    [InlineData(128, 64)]
    [InlineData(256, 16)]
    [InlineData(512, 4)]
    public void TheBatchFitsTheWorkingBudget(int size, int expected)
    {
        var plan = TilePlan.Create(size, 1, 0, 0, 8192, 8192);

        Assert.Equal(expected, plan.BatchTiles);
        Assert.True((long)plan.BatchTiles * plan.TileElements * TilePlan.ElementBytes <= TilePlan.BatchBudgetBytes);
    }

    [Fact]
    public void TheBatchNeverExceedsTheTileCount()
    {
        var plan = TilePlan.Create(128, 1, 0, 0, 10, 10);

        Assert.Equal(1, plan.TileCount);
        Assert.Equal(1, plan.BatchTiles);
    }

    [Theory]
    [InlineData(64, 8)]
    [InlineData(128, 32)]
    [InlineData(512, 512)]
    public void GroupsSplitEachTileIntoEqualParts(int size, int expected)
    {
        var plan = TilePlan.Create(size, 1, 0, 0, 200, 100);

        Assert.Equal(expected, plan.GroupsPerTile);
        Assert.Equal(plan.TileCount * expected, plan.GroupCount);
    }
}
