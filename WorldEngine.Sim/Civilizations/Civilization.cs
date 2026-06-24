using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Civilizations;

public sealed class Civilization
{
    public CivId      Id          { get; }
    public string     Name        { get; }
    public EntityId   FounderId   { get; }
    public TileCoord  CapitalTile { get; set; }
    public int        FoundedYear { get; }
    public bool       IsCollapsed { get; set; }
    public int        CollapseYear { get; set; }

    // Living member characters
    public HashSet<EntityId> Members { get; } = [];

    /// <summary>Year the most recent settlement was founded. Guards against paired-founding.</summary>
    public int LastSettlementFoundedYear { get; set; } = -999;

    /// <summary>
    /// Active wars: maps the enemy CivId to the year war was declared.
    /// War is a civ-level state — individual character relationships carry personal
    /// rivalries only; actual military conflict is tracked here.
    /// </summary>
    public Dictionary<CivId, int> WarsAgainst { get; } = [];

    public bool IsAtWarWith(CivId other) => WarsAgainst.ContainsKey(other);

    public Civilization(CivId id, string name, EntityId founderId, TileCoord capitalTile, int foundedYear)
    {
        Id          = id;
        Name        = name;
        FounderId   = founderId;
        CapitalTile = capitalTile;
        FoundedYear = foundedYear;
    }
}
