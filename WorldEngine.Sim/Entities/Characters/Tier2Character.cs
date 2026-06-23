using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// A named Tier 2 character — specialist or authority figure below hero/ruler status.
/// Uses simplified 4-need model and fixed role behaviors instead of utility scoring.
/// </summary>
public sealed class Tier2Character : IEntity
{
    public EntityId    Id       { get; }
    public EntityKind  Kind     => EntityKind.Tier2Character;
    public TileCoord   Location { get; internal set; }
    public bool        IsAlive  { get; internal set; } = true;

    public PersonalityVector6 Personality { get; }
    public LivelihoodData     Livelihood  { get; internal set; }
    public NeedsVector4       Needs       { get; internal set; }

    public string Name    { get; }
    public int    Health  { get; internal set; }
    public int    MaxHealth { get; }
    public int    AgeSeason  { get; internal set; }
    public int    MaxAgeSeason { get; }

    public Tier2Character(
        EntityId id,
        TileCoord location,
        string name,
        PersonalityVector6 personality,
        LivelihoodData livelihood,
        int maxHealth,
        int maxAgeSeason)
    {
        Id           = id;
        Location     = location;
        Name         = name;
        Personality  = personality;
        Livelihood   = livelihood;
        MaxHealth    = maxHealth;
        MaxAgeSeason = maxAgeSeason;
        Health       = maxHealth;
        Needs        = NeedsVector4.Default;
    }

    public EntitySnapshot ToSnapshot() => new(
        Id:            Id,
        Kind:          Kind,
        Name:          Name,
        SpeciesId:     string.Empty,
        IsLegendary:   false,
        Location:      Location,
        HealthFraction: MaxHealth > 0 ? (float)Health / MaxHealth : 0f,
        FoodFraction:  Needs.Food,
        AgeSeason:     AgeSeason,
        IsAlive:       IsAlive);

    // IEntity.EmitCommands — behavior handled by Tier2BehaviorPhase, not here
    public IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase)
        => Enumerable.Empty<ICommand>();
}
