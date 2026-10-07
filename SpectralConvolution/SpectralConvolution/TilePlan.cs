using System.Numerics;

namespace SpectralConvolution;

internal readonly record struct TilePlan
{
    public const int MinimumSize = 64;
    public const int MaximumSize = 512;
    public const int SmallSize = 128;
    public const int LargeSize = 512;
    public const int SmallSizeRadiusLimit = 23;
    public const int MaximumRadius = (LargeSize - 1) / 2;
    public const int GroupElements = 512;
    public const int ElementBytes = 16;
    public const long BatchBudgetBytes = 16L << 20;

    TilePlan(int size, int radius, int regionX, int regionY, int regionWidth, int regionHeight)
    {
        Size = size;
        Log2Size = BitOperations.Log2((uint)size);
        Radius = radius;
        RegionX = regionX;
        RegionY = regionY;
        RegionWidth = regionWidth;
        RegionHeight = regionHeight;
        TilesX = (regionWidth + ValidSize - 1) / ValidSize;
        TilesY = (regionHeight + ValidSize - 1) / ValidSize;
    }

    public int Size { get; }

    public int Log2Size { get; }

    public int Radius { get; }

    public int RegionX { get; }

    public int RegionY { get; }

    public int RegionWidth { get; }

    public int RegionHeight { get; }

    public int TilesX { get; }

    public int TilesY { get; }

    public int ValidSize => Size - 2 * Radius;

    public int TileCount => TilesX * TilesY;

    public int TileElements => Size * Size;

    public int GroupsPerTile => TileElements / GroupElements;

    public int GroupCount => TileCount * GroupsPerTile;

    public int ColumnUnitsPerGroup => GroupElements / Size;

    public int ChromaticGroupsPerTile => (ChromaticKernelSpectrum.HalfColumnsOf(Size) + ColumnUnitsPerGroup - 1) / ColumnUnitsPerGroup;

    public int BatchTiles => (int)Math.Min(TileCount, Math.Max(1L, BatchBudgetBytes / ((long)ElementBytes * TileElements)));

    public static int SelectSize(int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radius, MaximumRadius);
        return radius <= SmallSizeRadiusLimit ? SmallSize : LargeSize;
    }

    public static TilePlan Create(int radius, int regionX, int regionY, int regionWidth, int regionHeight)
        => Create(SelectSize(radius), radius, regionX, regionY, regionWidth, regionHeight);

    public static TilePlan Create(int size, int radius, int regionX, int regionY, int regionWidth, int regionHeight)
    {
        if (!IsSupportedSize(size))
            throw new ArgumentOutOfRangeException(nameof(size));
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(radius * 2, size);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(regionWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(regionHeight);
        return new TilePlan(size, radius, regionX, regionY, regionWidth, regionHeight);
    }

    public static bool IsSupportedSize(int size)
        => size is >= MinimumSize and <= MaximumSize && BitOperations.IsPow2(size);

    public int OriginX(int tile) => RegionX + tile % TilesX * ValidSize;

    public int OriginY(int tile) => RegionY + tile / TilesX * ValidSize;

    public int TileAt(int x, int y) => (y - RegionY) / ValidSize * TilesX + (x - RegionX) / ValidSize;

    public int ValidWidth(int tile) => Math.Min(ValidSize, RegionX + RegionWidth - OriginX(tile));

    public int ValidHeight(int tile) => Math.Min(ValidSize, RegionY + RegionHeight - OriginY(tile));
}
