namespace WorldEngine.Sim.Tiles.LocalScale;

/// <summary>A cell's position within its chunk, 0..ChunkSizeTiles-1 on each axis.</summary>
public readonly record struct LocalTileCoord(byte X, byte Y);
