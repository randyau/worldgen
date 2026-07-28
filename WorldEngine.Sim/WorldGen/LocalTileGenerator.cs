using WorldEngine.Sim.Config;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.Tiles.LocalScale;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Placeholder (flat/uniform) local-chunk generator — unblocks chunk-loading/UI work ahead of
/// 11.3's real noise-based terrain amplification. Every cell in the chunk copies the parent world
/// tile's own Elevation/BiomeType verbatim; no sub-tile variation, no border-manifest blending yet.
/// </summary>
public static class LocalTileGenerator
{
    public static LocalChunk GenerateFlat(ChunkCoord coord, TileData parentTile, LocalGenConfig config)
    {
        var chunk = new LocalChunk(coord, config.ChunkSizeTiles);
        var flat = new LocalTileData
        {
            Elevation = parentTile.Elevation,
            BiomeType = parentTile.BiomeType,
            DecorationType = 0,
            Flags = 0,
        };

        for (byte y = 0; y < config.ChunkSizeTiles; y++)
            for (byte x = 0; x < config.ChunkSizeTiles; x++)
                chunk.SetTile(new LocalTileCoord(x, y), flat);

        return chunk;
    }
}
