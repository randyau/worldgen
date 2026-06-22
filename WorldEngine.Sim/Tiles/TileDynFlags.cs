namespace WorldEngine.Sim.Tiles;

[Flags]
public enum TileDynFlags : byte
{
    None             = 0,
    HasActiveDisaster = 1 << 0,
    // bits 1–7: reserved for M2+
    // Candidates: HasStructure, IsContested, IsUnderSiege, IsOnTradeRoute
}
