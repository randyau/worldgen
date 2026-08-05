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
    // M14 14.4: internal set (not just get) so Tier2BehaviorPhase.PromoteToTier1 can assign the
    // promoted Tier1 a distinct EntityId from the dead Tier2's own — see that method's doc comment
    // for why reusing the same numeric id is unsafe (EntityRegistry's shared dictionary aliasing).
    // Kept internal set, not a constructor-only value, precisely so the *rest* of Spawn's
    // deterministic entitySeq-derived rolls (name, personality, ancestry) can stay unchanged —
    // reassigning only the identity after construction, not the whole derivation seed, avoids
    // rippling into every other RNG-derived outcome downstream of a promotion (confirmed via the
    // M13 relationship-event balance test: changing entitySeq itself shifted CharacterGrieved/
    // RivalryFormed/DebtIncurred far outside their calibrated bands, a real but unintended
    // consequence of a different fix approach).
    public EntityId  Id       { get; internal set; }
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
    // M14 14.5 — economic ledger UI plumbing: 0 for non-character entities (beasts have no Wealth).
    protected virtual  float  SnapshotWealth      => 0f;

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
        Wellbeing:      SnapshotWellbeing,
        Wealth:         SnapshotWealth);
}
