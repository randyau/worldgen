using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Immutable UI-facing summary of a Tier 1 character.
/// Carried in WorldSnapshot; only key fields for display purposes.
/// </summary>
public sealed record CharacterSnapshot(
    EntityId Id,
    EntityKind Kind,
    string Name,
    string Epithet,
    string AncestryId,
    TileCoord Location,
    CivId CivId,
    bool IsAlive,
    // Key personality for display
    float Ambition, float Aggression, float Loyalty,
    // Key needs for display
    float Safety, float Status, float Purpose,
    // Key skills for display
    float Combat, float Leadership, float Diplomacy,
    int AgeSeason,
    float HealthFraction,
    float Wellbeing);
