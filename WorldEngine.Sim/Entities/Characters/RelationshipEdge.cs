using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>RelationshipFlags (ally/rival/etc.) and RelationshipEdge record tracking character-to-character bonds.</summary>
[Flags]
public enum RelationshipFlags
{
    None = 0, IsAlly = 1, IsRival = 2,
    IsFamily = 8, IsMarried = 16,
    // M13 13.5: a rivalry re-declared while already active escalates into a Feud — deeper
    // Trust/Fear penalty, cleared only by Reconciliation alongside IsRival.
    IsFeud = 32
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
    float Debt,     // -1.0 to 1.0, relative to From: positive = From owes To, negative = To owes From
    RelationshipFlags Flags)
{
    public bool IsAlly    => Flags.HasFlag(RelationshipFlags.IsAlly);
    public bool IsRival   => Flags.HasFlag(RelationshipFlags.IsRival);
    public bool IsFamily  => Flags.HasFlag(RelationshipFlags.IsFamily);
    public bool IsMarried => Flags.HasFlag(RelationshipFlags.IsMarried);
    public bool IsFeud    => Flags.HasFlag(RelationshipFlags.IsFeud);

    // M13 13.2 — Debt as an obligation mechanic. Null when the edge carries no obligation.
    public EntityId? DebtorId   => Debt > 0f ? From : Debt < 0f ? To   : null;
    public EntityId? CreditorId => Debt > 0f ? To   : Debt < 0f ? From : null;
}
