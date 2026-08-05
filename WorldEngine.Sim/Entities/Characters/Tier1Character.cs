using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Tiles.LocalScale;
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

    /// <summary>
    /// M12 12.2: this character's Organization affiliations (civ, and from M13-M15 also
    /// family/guild/religion). Replaces the old IdentityData.CivId scalar field — see
    /// docs/phases/m12_organization_model.md. Mutate only via CivTracker.SetCharacterCiv.
    /// </summary>
    public List<Membership> Memberships { get; } = [];

    /// <summary>
    /// O(1) convenience accessor for this character's Civilization-kind membership, if any.
    /// The overwhelming majority of the sim's per-tick reads only ever care about "what civ is
    /// this character in", so this stays a fast property instead of forcing every call site to
    /// scan Memberships or thread a WorldState reference through (ToCharacterSnapshot below has
    /// none available at all).
    /// </summary>
    public CivId CivId
    {
        get
        {
            foreach (var m in Memberships)
                if (m.CivId.IsValid) return m.CivId;
            return CivId.None;
        }
    }

    // Disease state — set by CharacterBehaviorPhase on exposure to infected settlements
    public bool IsInfected       { get; internal set; }
    public int  InfectedSinceYear { get; internal set; }

    // Emotional state — continuous wellbeing score: -1 (spiraling) … +1 (flourishing)
    public float Wellbeing { get; internal set; }

    // Wanderlust — ticks on the same tile; drives travel utility bonus
    public int TicksInCurrentTile { get; internal set; }

    // Tick when the most recent Create goal completed (used to gate re-formation)
    public int LastCreateCompletedTick { get; internal set; } = -1;

    // Tick when the character last defected to another civ (used to gate re-defection —
    // without this a character stuck in a chronic Wellbeing crisis with an available foreign
    // confidant re-selects Defect every tick, once for each civ swap, indefinitely)
    public int LastDefectionTick { get; internal set; } = -1;

    // Year when the character last created artwork (gates ArtworkCreated events per cooldown period)
    public int LastArtworkYear { get; internal set; } = -999;

    // Year when the character last founded a religion (gates re-founding via cooldown)
    public int LastReligionFoundedYear { get; internal set; } = -999;

    // M14 14.0 — personal wealth accumulator (mirrors Tier2Character.Notability's shape). Portable
    // value denominated against EconomyConfig.BaseValuePerUnit; physically conserved — every
    // transfer debits some other pool (settlement ResourceStores, another character's Wealth, an
    // Organization.Treasury). See docs/phases/m14_economy_independent_wealth.md decision 4.
    public float Wealth { get; internal set; } = 0f;

    /// <summary>Adds (or subtracts) Wealth, floored at 0 so spend/spoilage can never go negative.</summary>
    public void AddWealth(float amount) => Wealth = Math.Max(0f, Wealth + amount);

    // Local-scale position foundation (M11 11.6) — data shape only, never populated by any current
    // sim logic. V2: local-scale character movement/pathfinding.
    public ChunkCoord?     LocalChunk    { get; internal set; }
    public LocalTileCoord? LocalPosition { get; internal set; }

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

    protected override string  SnapshotName         =>
        Identity.Surname.Length > 0
            ? $"{Identity.Name} {Identity.Surname} {Identity.Epithet}"
            : $"{Identity.Name} {Identity.Epithet}";
    protected override string  SnapshotSpeciesId    => string.Empty;
    protected override bool    SnapshotIsLegendary  => false;
    protected override float   SnapshotFoodFraction => Needs.Food;
    protected override string  SnapshotAncestryId   => Identity.AncestryId;
    protected override float   SnapshotWellbeing    => Wellbeing;
    protected override float   SnapshotWealth       => Wealth;

    public CharacterSnapshot ToCharacterSnapshot() => new(
        Id:             Id,
        Kind:           Kind,
        Name:           Identity.Name,
        Surname:        Identity.Surname,
        Epithet:        Identity.Epithet,
        AncestryId:     Identity.AncestryId,
        Location:       Location,
        CivId:          CivId,
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
