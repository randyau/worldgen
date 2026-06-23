using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// Lightweight settlement record. Population is dynamic from Phase 2.4 onward.
/// </summary>
public sealed record SettlementStub(
    EntityId  FounderId,
    CivId     CivId,
    TileCoord Tile,
    int       FoundedYear,
    int       Population,              // integer head count
    int       Health,                  // 0–100; raids reduce it; 0 = destroyed
    float     PopulationF       = 0f,  // fractional accumulator for growth
    int       LastCrystalThresh = 0);  // population threshold already crystallized
