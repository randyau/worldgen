using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

[Flags]
public enum RelationshipFlags
{
    None = 0, IsAlly = 1, IsRival = 2, IsAtWar = 4,
    IsFamily = 8, IsMarried = 16
}

/// <summary>
/// Directed relationship edge: how From perceives To.
/// Stored as canonical pair (smaller Id first) in RelationshipGraph.
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
    public bool IsAtWar => Flags.HasFlag(RelationshipFlags.IsAtWar);
}
