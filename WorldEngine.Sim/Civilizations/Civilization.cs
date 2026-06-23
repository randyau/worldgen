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

    public Civilization(CivId id, string name, EntityId founderId, TileCoord capitalTile, int foundedYear)
    {
        Id          = id;
        Name        = name;
        FounderId   = founderId;
        CapitalTile = capitalTile;
        FoundedYear = foundedYear;
    }
}
