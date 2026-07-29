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

    /// <summary>
    /// Width, in local tiles, of the band next to each world-tile edge over which elevation blends
    /// from this tile's own byte value toward the shared BorderManifest edge sample. Must be less
    /// than LocalTilesPerWorldTileEdge / 2 (bands from opposite edges must not overlap).
    /// </summary>
    public int EdgeBlendBandTiles { get; set; } = 100;

    /// <summary>FastNoiseLite frequency for local elevation detail noise, sampled in absolute local-tile coordinates.</summary>
    public float NoiseFrequency { get; set; } = 0.05f;

    /// <summary>Fractal octaves for local elevation detail noise.</summary>
    public int NoiseOctaves { get; set; } = 3;

    /// <summary>Max +/- byte contribution the detail noise adds on top of the blended macro elevation.</summary>
    public float NoiseAmplitude { get; set; } = 6f;

    /// <summary>Byte elevation decrement applied to cells carved into a river channel.</summary>
    public int RiverChannelDepth { get; set; } = 20;

    /// <summary>
    /// Channel width, in local tiles, at a river's interior source/mouth anchor (the tile-center
    /// endpoint used when a tile has only one boundary crossing) — the manifest gives no width to
    /// read for that endpoint since it isn't a tile-edge crossing.
    /// </summary>
    public float RiverSourceWidthTiles { get; set; } = 15f;

    /// <summary>
    /// Chunk-radius around the local-view camera that stays generated (Minecraft-style lazy
    /// loading, per docs/phases/m11_local_scale_generation.md's "chunked, lazy generation"
    /// decision); chunks further away than this are discarded, not persisted.
    /// </summary>
    public int ViewDistanceChunks { get; set; } = 3;
}
