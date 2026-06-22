namespace WorldEngine.Sim.Tiles;

[Flags]
public enum TileStaticFlags : ushort
{
    None            = 0,
    IsVolcanic      = 1 << 0,
    IsFaultLine     = 1 << 1,
    HasDeposit      = 1 << 2,
    HasRareResource = 1 << 3,
    IsCoastal       = 1 << 4,
    HasRiver        = 1 << 5,
    IsLake          = 1 << 6,
    IsPOICandidate  = 1 << 7,
    IsStormCorridor = 1 << 8,
    // bits 9–15: reserved for M2+
}
