namespace WorldEngine.Sim.Tiles.LocalScale;

/// <summary>
/// Minimal per-cell local-scale terrain data — flavor terrain only, no civ/economy fields
/// (compare <see cref="TileData"/>, the much larger world-scale equivalent).
/// </summary>
public struct LocalTileData
{
    public byte Elevation;      // 0-255, scaled; blended from parent TileData + border manifests (11.3)
    public byte BiomeType;      // cast to Core.BiomeType enum; inherited from parent tile until 11.3 adds sub-tile detail
    public byte DecorationType; // 0=none; local flavor decoration (rocks, scrub, etc.), populated 11.3+
    public byte Flags;          // reserved bit flags (e.g. river-crossing overlay, 11.4); no bits assigned yet
}
