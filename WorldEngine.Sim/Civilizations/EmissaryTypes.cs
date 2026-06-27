using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// How one civilization learned of another — ranked by fidelity (higher = better known).
/// </summary>
public enum CivContactSource
{
    Rumor            = 0,   // heard via proximity or chaining; lowest fidelity
    WandererMet      = 1,   // a wandering character had a cross-civ encounter
    EmissaryExchange = 2,   // a dispatched emissary returned with direct knowledge
    War              = 3,   // at war with this civ; highest confidence
}

/// <summary>
/// What one civ knows about another — how they learned of it, where the capital is,
/// and how confident the knowledge is. Confidence decays without refresh.
/// </summary>
public sealed record CivContact(
    CivId KnownCivId,
    int   YearFirstContact,
    int   YearLastContact,
    CivContactSource BestSource,   // highest-fidelity source seen so far
    TileCoord CapitalTile,         // exact tile — updated on EmissaryExchange or War contact
    float Confidence               // 0.0 = rumor almost forgotten, 1.0 = well-known
);

/// <summary>
/// The purpose of a dispatched emissary mission.
/// </summary>
public enum EmissaryPurpose { Trade, Diplomacy, Spy, Religious }

/// <summary>
/// An emissary in transit between two civs. Stored in WorldState.PendingEmissaries.
/// Resolved when ArrivalYear == world.CurrentYear.
/// </summary>
public sealed record PendingEmissary(
    CivId FromCiv,
    CivId ToCiv,
    EmissaryPurpose Purpose,
    int   DepartedYear,
    int   ArrivalYear,      // DepartedYear + ceil(distance / emissary_travel_speed_tiles_per_year)
    float SurvivalChance    // pre-computed at dispatch; clamp(1 - dist * death_per_tile, min_survival, 1.0)
);
