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
    IReadOnlyDictionary<string, float>? ResourceStores = null,  // persistent resource reserves keyed by resource name
    int CarryingCapacity = 50_000,                              // food-ledger-derived population ceiling; computed by ResourcePressurePhase each tick (EMA-smoothed)
    float SmoothedCapacity = 50_000f,                          // EMA of raw food-derived capacity; used by logistic suppression to damp oscillation
    bool IsColony = false,                                       // true when founded beyond ColonyMinDistance from all same-civ settlements
    bool IsInfected = false,                                    // currently suffering a disease outbreak
    int  InfectedSinceYear = 0,                                 // year the current infection started
    float Unrest = 0f)                                          // S2: 0=content, 1=fully rebellious; drives secession
                                           // vital (food, water): measured in seasons of supply; draws during deficit
                                           // wealth (gold, minerals, timber): raw accumulated units; no demand draw
{
    public float GetStore(string resource) =>
        ResourceStores?.TryGetValue(resource, out float v) == true ? v : 0f;
}
