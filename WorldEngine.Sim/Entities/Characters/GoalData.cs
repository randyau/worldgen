using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

public enum GoalType
{
    // Survival
    Survive,        // unmet urgent need
    Security,       // raise Safety need
    Acquire,        // secure a resource (food, water, material)
    Flee,           // leave current region (disaster or resource crisis)
    Endure,         // just survive — trauma/crisis response

    // Civic ambition
    Expansion,      // establish a settlement
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
}
