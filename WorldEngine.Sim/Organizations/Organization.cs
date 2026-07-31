using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Organizations;

/// <summary>
/// Shared organization layer backing Civilization (M12) and, from M13-M15, Guild/Religion/Family.
/// Holds membership, the leader seat, and org-to-org relationship state (alliance/war/tension)
/// as an independent fact rather than one derived from the leader's personal RelationshipEdge —
/// see roadmap M12 design decision 1. Civ-specific mechanics that aren't about
/// membership/leadership/relationships (territory, CulturalProfile, war resolution itself) stay
/// on Civilization; only the generalizable state lives here.
/// </summary>
public sealed class Organization
{
    public OrganizationId Id       { get; }
    public OrganizationKind Kind   { get; }
    public string Name             { get; set; }
    public EntityId LeaderId       { get; set; }
    public int FoundedYear         { get; }

    public Dictionary<EntityId, Membership> Members { get; } = new();

    /// <summary>Year the leader seat became vacant. int.MinValue = seat filled, no succession pending. Generalized in 12.3.</summary>
    public int SuccessionCrisisEndYear { get; set; } = int.MinValue;

    /// <summary>Active wars: maps the enemy OrganizationId to the year war was declared.</summary>
    public Dictionary<OrganizationId, int> WarsAgainst { get; } = new();

    /// <summary>Accumulated tension toward each other Organization, mirroring Civilization.BorderTension.</summary>
    public Dictionary<OrganizationId, float> BorderTension { get; } = new();

    /// <summary>Peace treaties: maps a former enemy OrganizationId to the year peace was made.</summary>
    public Dictionary<OrganizationId, int> PeaceTreaties { get; } = new();

    /// <summary>Standing alliances, tracked as an independent fact rather than derived from leader trust.</summary>
    public HashSet<OrganizationId> Allies { get; } = new();

    public Organization(OrganizationId id, OrganizationKind kind, string name, EntityId leaderId, int foundedYear)
    {
        Id = id;
        Kind = kind;
        Name = name;
        LeaderId = leaderId;
        FoundedYear = foundedYear;
    }

    public bool IsAtWarWith(OrganizationId other) => WarsAgainst.ContainsKey(other);
    public bool IsAllyOf(OrganizationId other) => Allies.Contains(other);
}
