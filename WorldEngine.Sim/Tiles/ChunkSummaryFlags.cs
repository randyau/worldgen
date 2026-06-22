namespace WorldEngine.Sim.Tiles;

[Flags]
public enum ChunkSummaryFlags : byte
{
    None              = 0,
    HasVolcanicTile   = 1 << 0,
    HasFaultLineTile  = 1 << 1,
    HasForestTile     = 1 << 2,
    HasRiverTile      = 1 << 3,
    HasActiveDisaster = 1 << 4,
    // bits 5–7: reserved
}
