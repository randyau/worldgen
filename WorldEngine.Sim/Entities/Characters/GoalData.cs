using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>GoalType enum and GoalData record: type, target, priority, staleness, and resolution tracking.</summary>
public enum GoalType
{
    // Survival
    Survive,        // unmet urgent need
    Security,       // raise Safety need
    Acquire,        // secure a resource (food, water, material)
    Flee,           // leave current region (disaster or resource crisis)
    Endure,         // just survive — trauma/crisis response

    // Civic ambition
    Dominance,      // defeat a rival
    Alliance,       // form an ally
    Unify,          // absorb rival civ into own (Phase 2.3+)

    // Social / emotional
    Bond,           // seek and maintain companionship with a specific person
    Protect,        // keep a trusted entity alive
    Avenge,         // punish whoever killed a trusted entity
    Grieve,         // trusted person died — withdrawal and Wellbeing drain

    // Flourishing
    Create,         // make art, craft, or knowledge (Ingenuity-driven)

    // M3+ city-state
    FoundCity,         // ruler-delegated: travel to frontier and found a new city
    BuildImprovement,  // build a Farm/Mine/etc. on a territory tile

    // M4 Phase 3 — religion
    FoundReligion,     // high-spiritual character founds a religious movement

    // Beast interaction
    SlayBeast,         // hunt and kill a specific legendary beast

    // M5 Artifacts
    CovetArtifact,     // desire a high-quality artifact not currently owned by the character
}

public enum GoalObject
{
    None,
    Person,
    Settlement,
    Food,
    Water,
    Material,
    Region,
    Rival,
    Artwork,
    Artifact,
}

public sealed class GoalData
{
    public GoalType   Type           { get; init; }
    public GoalObject Object         { get; init; }
    public EntityId?  TargetEntityId { get; init; }
    public TileCoord? TargetTile     { get; init; }
    public float      Priority       { get; set; }   // 0.0–1.0, recomputed each tick
    public float      Progress       { get; set; }   // 0.0–1.0
    public bool       IsComplete     { get; set; }
    public int        StaleSince     { get; set; }   // tick when last advanced
    public float      Intensity      { get; set; }   // emotional weight 0–1; drives Wellbeing impact
    public int        FormedTick     { get; set; }
    /// <summary>
    /// For Acquire goals: the specific resource type string ("food", "iron", etc.).
    /// Null for non-resource goals. Lowercase, matches ResourceLedger keys.
    /// </summary>
    public string?    ResourceTag    { get; set; }

    /// <summary>
    /// For CovetArtifact goals: the <see cref="ArtifactId"/> of the desired artifact.
    /// Zero-valued for all other goal types.
    /// </summary>
    public ArtifactId CovetedArtifactId { get; set; }
}
