using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// A named Tier 1 character — hero, warlord, or ruler.
/// Makes utility-scored decisions each season via EmitCommands.
/// </summary>
public sealed class Tier1Character : IEntity
{
    public EntityId Id    { get; }
    public EntityKind Kind => EntityKind.Tier1Character;
    public TileCoord Location { get; internal set; }
    public bool IsAlive { get; internal set; } = true;

    // Stable traits
    public PersonalityVector Personality { get; }
    public AptitudeVector    Aptitude    { get; }

    // Dynamic
    public SkillVector Skills   { get; internal set; }
    public NeedsVector Needs    { get; internal set; }
    public IdentityData Identity { get; internal set; }
    public List<GoalData> Goals { get; } = [];

    // Health / aging
    public int Health    { get; internal set; }
    public int MaxHealth { get; }
    public int AgeSeason { get; internal set; }
    public int MaxAgeSeason { get; }

    // Wanderlust — ticks on the same tile; drives travel utility bonus
    public int TicksInCurrentTile { get; internal set; }

    public Tier1Character(
        EntityId id,
        TileCoord location,
        PersonalityVector personality,
        AptitudeVector aptitude,
        SkillVector skills,
        IdentityData identity,
        int maxHealth,
        int maxAgeSeason)
    {
        Id           = id;
        Location     = location;
        Personality  = personality;
        Aptitude     = aptitude;
        Skills       = skills;
        Needs        = NeedsVector.Default;
        Identity     = identity;
        MaxHealth    = maxHealth;
        Health       = maxHealth;
        MaxAgeSeason = maxAgeSeason;
    }

    public IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase)
    {
        if (!IsAlive || phase != SimPhase.CharacterDecisions) yield break;
        // Full utility scoring wired in Story 2.2.4 (CharacterBehaviorPhase calls this)
        yield break;
    }

    public EntitySnapshot ToSnapshot() => new(
        Id:            Id,
        Kind:          Kind,
        Name:          $"{Identity.Name} {Identity.Epithet}",
        SpeciesId:     string.Empty,
        IsLegendary:   false,
        Location:      Location,
        HealthFraction: MaxHealth > 0 ? (float)Health / MaxHealth : 0f,
        FoodFraction:  Needs.Food,
        AgeSeason:     AgeSeason,
        IsAlive:       IsAlive);

    public CharacterSnapshot ToCharacterSnapshot() => new(
        Id:            Id,
        Kind:          Kind,
        Name:          Identity.Name,
        Epithet:       Identity.Epithet,
        Location:      Location,
        CivId:         Identity.CivId,
        IsAlive:       IsAlive,
        Ambition:      Personality.Ambition,
        Aggression:    Personality.Aggression,
        Loyalty:       Personality.Loyalty,
        Safety:        Needs.Safety,
        Status:        Needs.Status,
        Purpose:       Needs.Purpose,
        Combat:        Skills.Combat,
        Leadership:    Skills.Leadership,
        Diplomacy:     Skills.Diplomacy,
        AgeSeason:     AgeSeason,
        HealthFraction: MaxHealth > 0 ? (float)Health / MaxHealth : 0f);
}
