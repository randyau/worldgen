namespace WorldEngine.Sim.Tiles;

[Flags]
public enum TileDynFlags : byte
{
    None              = 0,
    HasActiveDisaster = 1 << 0,
    RecentlyBurned    = 1 << 1, // set when a wildfire expires on a forest tile; cleared after post-fire boost is applied
    // bits 2–7: reserved for M2+
    // Candidates: HasStructure, IsContested, IsUnderSiege, IsOnTradeRoute
}
