using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

public enum GoalType
{
    Survive,        // unmet urgent need
    Security,       // raise Safety need
    Expansion,      // establish a settlement
    Dominance,      // defeat a rival
    Alliance,       // form an ally
    Unify           // absorb rival civ into own (Phase 2.3+)
}

public sealed class GoalData
{
    public GoalType     Type           { get; init; }
    public EntityId?    TargetEntityId { get; init; }
    public TileCoord?   TargetTile     { get; init; }
    public float        Priority       { get; set; }   // 0.0–1.0, recomputed each tick
    public float        Progress       { get; set; }   // 0.0–1.0
    public bool         IsComplete     { get; set; }
    public int          StaleSince     { get; set; }   // season tick when last advanced
}
