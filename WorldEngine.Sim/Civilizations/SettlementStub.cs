using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// Lightweight settlement placeholder for Phase 2.2.
/// Replaced by full Settlement system in Phase 2.4.
/// </summary>
public sealed record SettlementStub(
    EntityId FounderId,
    CivId    CivId,
    TileCoord Tile,
    int      FoundedYear,
    int      Population,   // stub: starts at 50, Phase 2.4 makes this dynamic
    int      Health);      // 0–100; raids reduce it; 0 = destroyed
