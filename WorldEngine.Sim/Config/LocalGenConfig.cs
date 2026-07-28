namespace WorldEngine.Sim.Config;

/// <summary>Local-scale (10m-resolution) generation parameters: chunk size and world-tile subdivision (M11).</summary>
public class LocalGenConfig
{
    /// <summary>Local tiles per chunk edge; a chunk is ChunkSizeTiles × ChunkSizeTiles cells.</summary>
    public int ChunkSizeTiles { get; set; } = 40;

    /// <summary>
    /// Local tiles per world-tile edge at 10m resolution (10km / 10m = 1000). Must be evenly
    /// divisible by ChunkSizeTiles so chunk coordinates roll over cleanly at world-tile
    /// boundaries — see LocalCoordMath.
    /// </summary>
    public int LocalTilesPerWorldTileEdge { get; set; } = 1000;

    /// <summary>Chunks per world-tile edge, derived from the two values above.</summary>
    public int ChunksPerWorldTileEdge => LocalTilesPerWorldTileEdge / ChunkSizeTiles;
}
