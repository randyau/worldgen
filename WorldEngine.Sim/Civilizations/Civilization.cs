using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

public sealed class Civilization
{
    public CivId      Id          { get; }
    public string     Name        { get; }
    public EntityId   FounderId   { get; }
    /// <summary>Current ruling character. Starts as FounderId; updated by succession when the ruler dies.</summary>
    public EntityId   RulerId     { get; set; }
    public TileCoord  CapitalTile { get; set; }
    public int        FoundedYear { get; }
    public bool       IsCollapsed { get; set; }
    public int        CollapseYear { get; set; }

    // Living member characters
    public HashSet<EntityId> Members { get; } = [];

    /// <summary>Year the most recent settlement was founded. Guards against paired-founding.</summary>
    public int LastSettlementFoundedYear { get; set; } = -999;

    /// <summary>Count of currently-live local settlements (within ColonyMinDistance of an existing settlement at founding). Maintained by CivTracker.</summary>
    public int SettlementCount { get; set; } = 0;

    /// <summary>Count of currently-live colony settlements (founded beyond ColonyMinDistance from all same-civ settlements). Maintained by CivTracker.</summary>
    public int ColonyCount { get; set; } = 0;

    /// <summary>Sum of all settlement populations for this civ. Refreshed by PopulationDynamicsPhase each tick — read-only outside that phase.</summary>
    public int TotalPopulation { get; set; } = 0;

    /// <summary>
    /// Year the succession crisis ends. int.MinValue = founder still alive, no crisis pending.
    /// Set when the founder dies; distant settlements decay faster until this year passes.
    /// </summary>
    public int SuccessionCrisisEndYear { get; set; } = int.MinValue;

    /// <summary>
    /// Accumulated territorial tension toward each other civ.
    /// Increases annually when their settlements are within WarProximityRadius tiles;
    /// decays when they're not. Crossing TensionWarThreshold triggers war if the ruler
    /// is aggressive enough. Cleared on peace. Maintained by CivTracker.RunBorderTension.
    /// </summary>
    public Dictionary<CivId, float> BorderTension { get; } = new();

    /// <summary>
    /// Active wars: maps the enemy CivId to the year war was declared.
    /// War is a civ-level state — individual character relationships carry personal
    /// rivalries only; actual military conflict is tracked here.
    /// </summary>
    public Dictionary<CivId, int> WarsAgainst { get; } = [];

    public bool IsAtWarWith(CivId other) => WarsAgainst.ContainsKey(other);

    /// <summary>
    /// Peace treaties: maps a former enemy CivId to the year peace was made.
    /// DeclareWar checks this to enforce a post-war cooldown — neither side can
    /// restart the war for PeaceCooldownYears after it ended.
    /// </summary>
    public Dictionary<CivId, int> PeaceTreaties { get; } = [];

    /// <summary>
    /// Total wars ever declared against each civ (from this civ's perspective).
    /// Used to scale the peace cooldown — repeated aggressors face growing exhaustion.
    /// </summary>
    public Dictionary<CivId, int> WarHistory { get; } = [];

    /// <summary>
    /// Per-city territory. Key = city tile; Value = all tiles that city owns (including itself).
    /// Maintained by CivTracker on founding, abandonment, conquest, and annual TerritoryPhase.
    /// </summary>
    public Dictionary<TileCoord, HashSet<TileCoord>> CityTerritories { get; } = new();

    /// <summary>
    /// Number of rulers this civ has had (founder = 1). Incremented on each succession.
    /// </summary>
    public int RulerCount { get; set; } = 1;

    // ─── Cultural trait counters (maintained by sim phases; read by EvaluateCulturalTraits) ─────

    /// <summary>Total wars this civ has declared (sum across all enemies). Incremented in StartWarBetween.</summary>
    public int TotalWarsInitiated { get; set; } = 0;

    /// <summary>Total succession events this civ has had. Incremented in CharacterBehaviorPhase when succession fires.</summary>
    public int TotalSuccessions { get; set; } = 0;

    /// <summary>Total settlements ever founded by this civ. Incremented in CivTracker.EstablishSettlement.</summary>
    public int TotalSettlementsFounded { get; set; } = 0;

    /// <summary>Number of times TotalPopulation fell below the near-collapse threshold. Checked annually by EvaluateCulturalTraits.</summary>
    public int NearCollapseCount { get; set; } = 0;

    /// <summary>Total ScholarDiscovery events by civ members. Incremented in Tier2BehaviorPhase.</summary>
    public int TotalScholarDiscoveries { get; set; } = 0;

    /// <summary>Permanent cultural traits assigned once thresholds are crossed. Maintained by CivTracker.EvaluateCulturalTraits.</summary>
    public HashSet<string> CulturalTraits { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cultural profile derived from the founding ancestry and active traits (M3.5).
    /// Built at civ founding; updated when new cultural traits are acquired.
    /// Null until the civ is fully initialized.
    /// </summary>
    public CulturalProfile? CulturalProfile { get; set; }

    // ─── War campaign tracking (M4.2) ────────────────────────────────────────

    /// <summary>
    /// Per-war battle win counts against each enemy civ during the current war.
    /// Incremented by RunWarCampaigns; reset and consumed by EndWarBetween for territory transfer.
    /// </summary>
    public Dictionary<CivId, int> WarBattleWins { get; } = new();

    // ─── Civ awareness / emissary system (M4.1) ──────────────────────────────

    /// <summary>
    /// Civs this civ has knowledge of. Keyed by the known civ's id.
    /// Populated by KnowledgePropagationPhase; read by emissary dispatch.
    /// </summary>
    public Dictionary<CivId, CivContact> KnownCivs { get; } = new();

    /// <summary>
    /// Emissaries currently in transit dispatched BY this civ, keyed by target CivId.
    /// Stored here for per-civ cap checks; canonical list is WorldState.PendingEmissaries.
    /// </summary>
    public Dictionary<CivId, int> ActiveEmissaryCountByTarget { get; } = new();

    public bool InPeaceCooldownWith(CivId other, int currentYear, int cooldownYears, int warExhaustionPerWar = 0)
    {
        if (!PeaceTreaties.TryGetValue(other, out int peaceYear)) return false;
        int wars = WarHistory.GetValueOrDefault(other, 0);
        int scaledCooldown = cooldownYears + wars * warExhaustionPerWar;
        return currentYear - peaceYear < scaledCooldown;
    }

    public Civilization(CivId id, string name, EntityId founderId, TileCoord capitalTile, int foundedYear)
    {
        Id          = id;
        Name        = name;
        FounderId   = founderId;
        RulerId     = founderId;
        CapitalTile = capitalTile;
        FoundedYear = foundedYear;
    }
}
