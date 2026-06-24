using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities;

/// <summary>
/// Abstract base for all named, tracked simulation entities.
/// Holds the fields shared across Tier1Character, Tier2Character, and LegendaryBeast —
/// Id, Location, lifecycle state, and health/aging — so subclasses own only their
/// tier-specific behaviour.
/// </summary>
public abstract class SimEntity : IEntity
{
    public EntityId  Id       { get; }
    public abstract EntityKind Kind { get; }
    public TileCoord Location    { get; internal set; }
    public bool      IsAlive     { get; internal set; } = true;
    public int       Health      { get; internal set; }
    public int       MaxHealth   { get; }
    public int       AgeSeason   { get; internal set; }
    public int       MaxAgeSeason { get; }

    protected SimEntity(EntityId id, TileCoord location, int maxHealth, int maxAgeSeason)
    {
        Id           = id;
        Location     = location;
        MaxHealth    = maxHealth;
        MaxAgeSeason = maxAgeSeason;
        Health       = maxHealth;
    }

    public abstract IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase);

    // Subclasses provide these three projections so the base can build EntitySnapshot
    // without knowing tier-specific fields.
    protected abstract string SnapshotName       { get; }
    protected abstract string SnapshotSpeciesId  { get; }
    protected abstract bool   SnapshotIsLegendary { get; }
    protected abstract float  SnapshotFoodFraction { get; }
    protected virtual  string SnapshotAncestryId  => string.Empty;
    protected virtual  float  SnapshotWellbeing   => 0f;
    protected virtual  string? SnapshotCivName    => null;

    public EntitySnapshot ToSnapshot() => new(
        Id:             Id,
        Kind:           Kind,
        Name:           SnapshotName,
        SpeciesId:      SnapshotSpeciesId,
        IsLegendary:    SnapshotIsLegendary,
        Location:       Location,
        HealthFraction: MaxHealth > 0 ? (float)Health / MaxHealth : 0f,
        FoodFraction:   SnapshotFoodFraction,
        AgeSeason:      AgeSeason,
        IsAlive:        IsAlive,
        CivName:        SnapshotCivName,
        AncestryId:     SnapshotAncestryId,
        Wellbeing:      SnapshotWellbeing);
}
