using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>Describes a Tier 2 character's role, affiliation, and economic position.</summary>
public sealed record LivelihoodData(
    Tier2Role  Role,
    EntityId?  EmployerId,      // Tier 1 character this person serves, if any
    TileCoord  SettlementTile,  // home settlement
    float      IncomeLevel      // 0.0–1.0
);
