using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Tiles.LocalScale;

/// <summary>
/// Pure conversions between (ChunkCoord, LocalTileCoord) and an "absolute" local coordinate — the
/// cell's position counted in local tiles from world origin (0,0), ignoring world-tile and chunk
/// boundaries entirely. Absolute coordinates are what 11.3's noise sampling keys off of, so the
/// same seed produces continuous terrain across a chunk or world-tile edge instead of a per-tile
/// noise domain that restarts (and seams) at every boundary.
/// </summary>
public static class LocalCoordMath
{
    public static (long X, long Y) ToAbsolute(
        ChunkCoord chunk, LocalTileCoord local, int localTilesPerWorldTileEdge, int chunkSizeTiles)
    {
        long tileOriginX = (long)chunk.WorldTile.X * localTilesPerWorldTileEdge;
        long tileOriginY = (long)chunk.WorldTile.Y * localTilesPerWorldTileEdge;
        long chunkOriginX = tileOriginX + (long)chunk.ChunkX * chunkSizeTiles;
        long chunkOriginY = tileOriginY + (long)chunk.ChunkY * chunkSizeTiles;
        return (chunkOriginX + local.X, chunkOriginY + local.Y);
    }

    /// <summary>
    /// Inverse of <see cref="ToAbsolute"/>. absX wraps cylindrically at the world's total local
    /// width (worldTileWidth * localTilesPerWorldTileEdge), matching TileCoord.Wrap's X-only
    /// wraparound; absY is not wrapped (the world doesn't wrap vertically).
    /// </summary>
    public static (ChunkCoord Chunk, LocalTileCoord Local) FromAbsolute(
        long absX, long absY, int localTilesPerWorldTileEdge, int chunkSizeTiles, int worldTileWidth)
    {
        long worldLocalWidth = (long)worldTileWidth * localTilesPerWorldTileEdge;
        absX = ((absX % worldLocalWidth) + worldLocalWidth) % worldLocalWidth;

        long worldTileX = FloorDiv(absX, localTilesPerWorldTileEdge);
        long worldTileY = FloorDiv(absY, localTilesPerWorldTileEdge);
        long withinTileX = absX - worldTileX * localTilesPerWorldTileEdge;
        long withinTileY = absY - worldTileY * localTilesPerWorldTileEdge;

        var tile = new TileCoord((int)worldTileX, (int)worldTileY);
        int chunkX = (int)(withinTileX / chunkSizeTiles);
        int chunkY = (int)(withinTileY / chunkSizeTiles);
        var localX = (byte)(withinTileX % chunkSizeTiles);
        var localY = (byte)(withinTileY % chunkSizeTiles);

        return (new ChunkCoord(tile, chunkX, chunkY), new LocalTileCoord(localX, localY));
    }

    private static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
