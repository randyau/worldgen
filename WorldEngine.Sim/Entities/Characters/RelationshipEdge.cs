using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

[Flags]
public enum RelationshipFlags
{
    None = 0, IsAlly = 1, IsRival = 2,
    IsFamily = 8, IsMarried = 16
}

/// <summary>
/// Directed relationship edge: how From perceives To.
/// Stored as canonical pair (smaller Id first) in RelationshipGraph.
/// War is NOT tracked here — it is a civ-level state on Civilization.WarsAgainst.
/// This edge only tracks personal relationships: trust, alliances, rivalries.
/// </summary>
public sealed record RelationshipEdge(
    EntityId From,
    EntityId To,
    float Trust,    // -1.0 to 1.0 (negative = hostility)
    float Fear,     //  0.0 to 1.0
    float Debt,     // -1.0 to 1.0 (negative = they owe me)
    RelationshipFlags Flags)
{
    public bool IsAlly  => Flags.HasFlag(RelationshipFlags.IsAlly);
    public bool IsRival => Flags.HasFlag(RelationshipFlags.IsRival);
}
