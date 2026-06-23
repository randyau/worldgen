using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities;

/// <summary>
/// Immutable UI-facing summary of one entity. Read by the UI thread from WorldSnapshot.
/// Heavy entity data stays on the sim thread inside EntityRegistry.
/// </summary>
public sealed record EntitySnapshot(
    EntityId Id,
    EntityKind Kind,
    string Name,
    string SpeciesId,        // matches beasts.toml id field for beasts; empty for characters
    bool IsLegendary,
    TileCoord Location,
    float HealthFraction,    // 0.0–1.0
    float FoodFraction,      // 0.0–1.0; -1 if entity has no Food need
    int AgeSeason,           // age in seasons
    bool IsAlive,
    string? CivName    = null,  // non-null for characters that belong to a civilization
    string AncestryId  = "",    // ancestry id from ancestries.toml; empty for non-character entities
    float  Wellbeing   = 0f    // -1 spiraling … +1 flourishing; 0 for non-character entities
);
