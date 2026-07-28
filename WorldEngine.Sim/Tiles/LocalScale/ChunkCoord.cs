using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Tiles.LocalScale;

/// <summary>A chunk's position within its parent world tile.</summary>
public readonly record struct ChunkCoord(TileCoord WorldTile, int ChunkX, int ChunkY)
{
    /// <summary>
    /// Rolls ChunkX/ChunkY back into [0, chunksPerEdge) when out of range, carrying the overflow
    /// into the neighboring world tile — cylinder-wrapped in X (matching TileCoord.Wrap), clamped
    /// in Y (world tiles don't wrap vertically, matching every other TileCoord accessor). Used when
    /// deriving a neighbor chunk's coordinate (e.g. chunk loading, border blending) from arithmetic
    /// that may cross a world-tile edge.
    /// </summary>
    public ChunkCoord Normalize(int chunksPerEdge, int worldTileWidth, int worldTileHeight)
    {
        int tileDx = FloorDiv(ChunkX, chunksPerEdge);
        int cx = ChunkX - tileDx * chunksPerEdge;
        int tileDy = FloorDiv(ChunkY, chunksPerEdge);
        int cy = ChunkY - tileDy * chunksPerEdge;

        var tile = WorldTile;
        if (tileDx != 0)
            tile = tile with { X = (((tile.X + tileDx) % worldTileWidth) + worldTileWidth) % worldTileWidth };
        if (tileDy != 0)
            tile = tile with { Y = Math.Clamp(tile.Y + tileDy, 0, worldTileHeight - 1) };

        return new ChunkCoord(tile, cx, cy);
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
