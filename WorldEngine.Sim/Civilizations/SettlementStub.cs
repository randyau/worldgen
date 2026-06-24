using WorldEngine.Sim.Core;
using System.Collections.Generic;

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
    string    Name                 = "Unknown",
    float     PopulationF          = 0f,   // fractional accumulator for growth
    int       LastCrystalThresh    = 0,    // population threshold already crystallized
    float     FoodPressureRatio    = 1f,   // convenience accessor; mirrors ResourceLedger["food"]
    float     WaterPressureRatio   = 1f,
    int       LastStrainEventTick  = 0,    // tick of last SettlementStraining event (rate-limit)
    IReadOnlyDictionary<string, float>? ResourceLedger = null, // extensible supply values per resource type
    float     FertilityMultiplier  = 1f,   // per-settlement founding-time variance; permanent
    int       ConqueredYear        = 0,    // year this settlement was last conquered (0 = never)
    int       ConqueredFromCivId   = 0,   // CivId of previous owner at time of conquest (0 = never)
    IReadOnlyDictionary<string, float>? ResourceStores = null)  // persistent resource reserves keyed by resource name
                                           // vital (food, water): measured in seasons of supply; draws during deficit
                                           // wealth (gold, minerals, timber): raw accumulated units; no demand draw
{
    public float GetStore(string resource) =>
        ResourceStores?.TryGetValue(resource, out float v) == true ? v : 0f;

    /// <summary>
    /// Radius (in tiles) of this settlement's hinterland.
    /// Scales with population; capped at 5. Shared by ResourcePressurePhase and UtilityScorer.
    /// </summary>
    public int ReachRadius() => Math.Clamp(2 + Population / 2000, 2, 5);
}
