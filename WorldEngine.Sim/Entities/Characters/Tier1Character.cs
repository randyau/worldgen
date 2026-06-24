using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// A named Tier 1 character — hero, warlord, or ruler.
/// Makes utility-scored decisions each season via EmitCommands.
/// </summary>
public sealed class Tier1Character : SimEntity
{
    public override EntityKind Kind => EntityKind.Tier1Character;

    // Stable traits
    public PersonalityVector Personality { get; }
    public AptitudeVector    Aptitude    { get; }

    // Dynamic
    public SkillVector   Skills   { get; internal set; }
    public NeedsVector   Needs    { get; internal set; }
    public IdentityData  Identity { get; internal set; }
    public List<GoalData> Goals   { get; } = [];

    // Disease state — set by CharacterBehaviorPhase on exposure to infected settlements
    public bool IsInfected       { get; internal set; }
    public int  InfectedSinceYear { get; internal set; }

    // Emotional state — continuous wellbeing score: -1 (spiraling) … +1 (flourishing)
    public float Wellbeing { get; internal set; }

    // Wanderlust — ticks on the same tile; drives travel utility bonus
    public int TicksInCurrentTile { get; internal set; }

    // Tick when the most recent Create goal completed (used to gate re-formation)
    public int LastCreateCompletedTick { get; internal set; } = -1;

    public Tier1Character(
        EntityId id,
        TileCoord location,
        PersonalityVector personality,
        AptitudeVector aptitude,
        SkillVector skills,
        IdentityData identity,
        int maxHealth,
        int maxAgeSeason)
        : base(id, location, maxHealth, maxAgeSeason)
    {
        Personality = personality;
        Aptitude    = aptitude;
        Skills      = skills;
        Needs       = NeedsVector.Default;
        Identity    = identity;
    }

    public override IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase)
    {
        if (!IsAlive || phase != SimPhase.CharacterDecisions) yield break;
        yield break;
    }

    protected override string  SnapshotName         => $"{Identity.Name} {Identity.Epithet}";
    protected override string  SnapshotSpeciesId    => string.Empty;
    protected override bool    SnapshotIsLegendary  => false;
    protected override float   SnapshotFoodFraction => Needs.Food;
    protected override string  SnapshotAncestryId   => Identity.AncestryId;
    protected override float   SnapshotWellbeing    => Wellbeing;

    public CharacterSnapshot ToCharacterSnapshot() => new(
        Id:             Id,
        Kind:           Kind,
        Name:           Identity.Name,
        Epithet:        Identity.Epithet,
        AncestryId:     Identity.AncestryId,
        Location:       Location,
        CivId:          Identity.CivId,
        IsAlive:        IsAlive,
        Ambition:       Personality.Ambition,
        Aggression:     Personality.Aggression,
        Loyalty:        Personality.Loyalty,
        Safety:         Needs.Safety,
        Status:         Needs.Status,
        Purpose:        Needs.Purpose,
        Combat:         Skills.Combat,
        Leadership:     Skills.Leadership,
        Diplomacy:      Skills.Diplomacy,
        AgeSeason:      AgeSeason,
        HealthFraction: MaxHealth > 0 ? (float)Health / MaxHealth : 0f,
        Wellbeing:      Wellbeing);
}
