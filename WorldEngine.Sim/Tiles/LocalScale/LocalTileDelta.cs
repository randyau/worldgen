namespace WorldEngine.Sim.Tiles.LocalScale;

/// <summary>
/// One persisted, sparse override of a single local tile — the only local-scale state that
/// survives a chunk being discarded and later regenerated (see
/// docs/phases/m11_local_scale_generation.md "regenerate base terrain on demand; persist only
/// modifications"). Keyed by (Chunk, Local); writing a second delta for the same cell replaces
/// the first rather than layering.
/// </summary>
public sealed record LocalTileDelta(
    ChunkCoord Chunk,
    LocalTileCoord Local,
    LocalChangeType ChangeType,
    string PayloadJson);
