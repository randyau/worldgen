using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

/// <summary>
/// Records the history of a tile that once held a settlement.
/// Persists when a settlement is destroyed or abandoned; accumulates each time the same tile cycles.
/// </summary>
public sealed record RuinRecord(
    TileCoord Tile,
    string    SettlementName,
    CivId     OriginalCivId,
    int       DestroyedYear,
    string    Cause,        // "destroyed" | "abandoned"
    int       TimesSettled  // how many times this tile has been settled, destroyed, and rebuilt
);
