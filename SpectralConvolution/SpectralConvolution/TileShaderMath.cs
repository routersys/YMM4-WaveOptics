using ComputeWeave;

namespace SpectralConvolution;

internal static class TileShaderMath
{
    const uint ExponentMask = 0x7F800000u;

    public static bool IsNonFinite(float value) => (Hlsl.AsUInt(value) & ExponentMask) == ExponentMask;

    public static bool IsNonFinite(Float4 value)
        => IsNonFinite(value.X) || IsNonFinite(value.Y) || IsNonFinite(value.Z) || IsNonFinite(value.W);

    public static int TileIndex(int batchStart, int groupBase, int log2Size) => batchStart + (groupBase >> (log2Size * 2));

    public static Int2 TileOrigin(int tileIndex, int tilesX, int validSize, int radius)
    {
        var tileRow = tileIndex / tilesX;
        return new Int2((tileIndex - tileRow * tilesX) * validSize - radius, tileRow * validSize - radius);
    }
}
