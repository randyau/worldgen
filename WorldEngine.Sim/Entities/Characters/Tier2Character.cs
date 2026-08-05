using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// A named Tier 2 character — specialist or authority figure below hero/ruler status.
/// Uses simplified 4-need model and fixed role behaviors instead of utility scoring.
/// </summary>
public sealed class Tier2Character : SimEntity
{
    public override EntityKind Kind => EntityKind.Tier2Character;

    public PersonalityVector6 Personality { get; }
    public LivelihoodData     Livelihood  { get; internal set; }
    public NeedsVector4       Needs       { get; internal set; }

    public string Name { get; }

    // Notable work pacing — set when a notable event fires; gates re-emission via cooldown
    public int  LastNotableWorkTick { get; internal set; } = -1000;
    // Masterwork flag — set when exceptional work fires; only one per lifetime (// V2: ARTIFACT)
    public bool HasMasterwork       { get; internal set; } = false;

    // M13.8.2: recent Tier1-driven relationship exposure (Bond/Rivalry/Placate/GrantAid/
    // ForgiveDebt targeting this character), decaying each tick. Feeds TryCrystallize's gate
    // alongside Needs.Status — see docs/phases/m13_8_tier2_relationship_exposure.md.
    public float Notability { get; internal set; } = 0f;

    // M14 14.0 — personal wealth accumulator (mirrors Notability's shape: internally-set,
    // externally readable). Denominated against EconomyConfig.BaseValuePerUnit; see
    // docs/phases/m14_economy_independent_wealth.md decision 4. Unlike Notability, this field has
    // real DTO/mapper persistence coverage from day one (WorldStateDto.cs/WorldStateMapper.cs).
    public float Wealth { get; internal set; } = 0f;

    public Tier2Character(
        EntityId id,
        TileCoord location,
        string name,
        PersonalityVector6 personality,
        LivelihoodData livelihood,
        int maxHealth,
        int maxAgeSeason)
        : base(id, location, maxHealth, maxAgeSeason)
    {
        Name        = name;
        Personality = personality;
        Livelihood  = livelihood;
        Needs       = NeedsVector4.Default;
    }

    // M13.8.2: bump Notability when targeted by a Tier1-driven relationship action
    // (Bond/Rivalry/Placate/GrantAid/ForgiveDebt) — called from CivTracker/GoalManager resolution.
    public void GainNotability(float amount) => Notability = Math.Min(1f, Notability + amount);

    // M14 14.0 — floored at 0 so a spend/spoilage step can never drive Wealth negative.
    public void AddWealth(float amount) => Wealth = Math.Max(0f, Wealth + amount);

    protected override string SnapshotName         => Name;
    protected override string SnapshotSpeciesId    => string.Empty;
    protected override bool   SnapshotIsLegendary  => false;
    protected override float  SnapshotFoodFraction => Needs.Food;
    protected override float  SnapshotWealth       => Wealth;

    // IEntity.EmitCommands — behavior handled by Tier2BehaviorPhase, not here
    public override IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase)
        => Enumerable.Empty<ICommand>();
}
