namespace WorldEngine.Sim.Core;

/// <summary>All enums: BiomeType, Season, SimPhase, EntityKind, EventType, EventTier, VerbClass, etc.</summary>
/// <summary>
/// Unified taxonomy for everything a character can create (M9 G-1). Routine output is a transient
/// event; exceptional output persists as an <see cref="WorldEngine.Sim.Entities.Artifacts.Artifact"/>
/// whose category is derived from the specific good (see CreatedGoodTaxonomy), not the creator's role.
/// </summary>
public enum CreatedGoodType
{
    // Artisan goods (Tier2 Artisan)
    Textiles, Pottery, Metalwork, Woodcraft, Leatherwork, Stonework,
    // Art (Tier1 Create goal)
    Monument, Epic, Song, Tapestry, Sculpture, Painting,
    // Discoveries (Tier2 Scholar)
    Agriculture, Medicine, Astronomy, Mathematics, Engineering, Philosophy, Navigation, Metallurgy,
}

public enum Season { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }

public enum SimSpeed { Paused, Slow, Normal, Fast, Ultrafast }

public enum OverlayType { Biome, Elevation, Temperature, Moisture, Resources, MagicIntensity, Territory }

public enum SimPhase
{
    Environmental      = 1,
    ResourceProduction = 2,
    PopulationDynamics = 3,
    EntityBehavior     = 4,
    CharacterDecisions = 5,
    ConflictResolution = 6,
    EventGeneration    = 7
}

public enum EntityKind
{
    Tier1Character, Tier2Character, Settlement, Army, TradeCaravan,
    RefugeeGroup, DiseaseOutbreak, ReligiousMovement, MonsterGroup,
    NomadGroup, LegendaryBeast
}

public enum EventTier
{
    Background = 0,
    Character  = 1,
    Regional   = 2,
    Headline   = 3
}

public enum VerbClass
{
    Creation = 0, Destruction = 1, Transformation = 2,
    Transfer = 3, Conflict = 4, Maintenance = 5, Interaction = 6
}

public enum PopulationImpact
{
    None = 0, Minor = 1, Moderate = 2, Major = 3, Catastrophic = 4
}

public enum BiomeType
{
    Ocean, CoastalWater, Beach, Tundra, BorealForest, TemperateForest,
    TropicalRainforest, Grassland, Savanna, Desert, Swamp,
    HighMountain, Mountain, Hills, Plains, Volcanic
}

public enum DisasterType
{
    Wildfire      = 0,
    Flood         = 1,
    VolcanicAsh   = 2,
    SeismicDamage = 3,
    // V2: Plague, Blight, ArmyPresence
}

public enum CulturalTrait
{
    Militaristic,    // high war frequency
    Expansionist,    // high settlement founding rate
    Mercantile,      // high merchant trade volume
    Scholarly,       // high scholar discovery rate
    Reclusive,       // low inter-civ contact
    UnstableThrone,  // high succession rate
    WarWeary,        // repeated war exhaustion cooldowns triggered
    Resilient,       // survived multiple near-collapses
}

public enum EventType
{
    // Environmental (1000–1099) — locked, never renumber
    VolcanicEruption    = 1001,
    EarthquakeOccurred  = 1002,
    WildfireOccurred    = 1003,
    FloodOccurred       = 1004,
    DroughtBegan        = 1005,
    DroughtEnded        = 1006,
    SeaLevelChanged     = 1007,
    BiomeChanged        = 1008,
    ClimateShifted      = 1009,
    ResourceRecovered   = 1010,
    // Beast events (2001–2099) — M2.1
    BeastSpawned        = 2001,
    BeastAwakened       = 2002,
    BeastDied           = 2003,
    BeastSlain          = 2004,
    BeastReproduced     = 2005,
    BeastEncountered    = 2006,
    BeastAttackedChar   = 2007,  // beast attacked a Tier 1 character

    // M2+ character lifecycle (3000-range)
    CharacterBorn           = 3001,
    CharacterDied           = 3002,
    CharacterMarried        = 3003,
    CharacterExiled         = 3004,
    CharacterGrieved        = 3005,  // trusted companion died; character enters grief
    CharacterFlourishing    = 3006,  // Wellbeing crossed +0.7; character is thriving
    CharacterSpiraling      = 3007,  // Wellbeing crossed -0.7; crisis state

    // M2+ character actions (3100-range)
    AllianceFormed          = 3101,
    AllianceBroken          = 3102,
    WarDeclared             = 3103,
    WarEnded                = 3104,
    BattleOccurred          = 3105,
    RivalryFormed           = 3106,
    Negotiated              = 3107,
    ArtworkCreated          = 3108,  // character created something (art, craft, discovery)
    GoalFormed              = 3109,  // notable goal formed (Bond, Avenge, Create)
    GoalResolved            = 3110,  // notable goal achieved or abandoned
    DebtIncurred            = 3111,  // M13 13.2: one character materially aided another, creating an obligation
    DebtForgiven            = 3112,  // M13 13.2: a creditor released a debtor from their obligation
    RivalryPlacated         = 3113,  // M13 13.1: a character appeased a feared rival instead of confronting them
    CharacterDefected       = 3114,  // M13 13.4: a distressed character abandoned their civ for a trusted foreign confidant's
    RivalsReconciled        = 3115,  // M13 13.5: a placated rivalry cooled enough (low Fear, positive Trust) to end outright
    RivalryEscalatedToFeud  = 3116,  // M13 13.5: a rivalry re-declared while already active deepened into a Feud
    CharacterEstranged      = 3117,  // M13 13.5: a married couple's Trust decayed far enough to end the marriage
    OathBroken              = 3118,  // M13 13.5: a debtor warred/raided their own creditor's civ instead of honoring the debt

    // M2+ civilization/settlement (3200-range)
    CivilizationFounded     = 3201,
    CivilizationCollapsed   = 3202,
    SettlementFounded       = 3203,
    SettlementDestroyed     = 3204,
    SuccessionOccurred      = 3205,
    SettlementStraining     = 3206,  // settlement is under food or water shortage
    SettlementConquered     = 3207,  // raiding civ annexed the settlement; survives under new CivId
    TerritoryExpanded       = 3208,
    TerritoryLost           = 3209,
    ImprovementBuilt        = 3210,
    CivTraitAcquired        = 3211,   // civ crossed a threshold and earned a cultural trait
    CivSplintered           = 3212,   // settlement(s) seceded and formed a new civilization (S2 splinter mechanic)

    // M2+ population events (3400-range)
    SettlementGrew          = 3401,
    SettlementShrank        = 3402,
    SettlementAbandoned     = 3403,
    DiseaseOutbreak         = 3404,  // settlement struck by disease; population drains while infected
    DiseaseRecovered        = 3405,  // settlement cleared of infection
    WildlifeRaid            = 3406,  // beast pack attacks settlement; direct population loss
    SuccessionCrisis        = 3407,  // founding ruler died; distant settlements enter instability

    // M2+ Tier 2 character events (3300-range)
    AppointedToRole         = 3301,
    DismissedFromRole       = 3302,
    MerchantTradeCompleted  = 3303,
    ScholarDiscovery        = 3304,
    PhysicianHealed         = 3305,
    CharacterCrystallized   = 3306,
    ArtisanCrafted          = 3307,  // artisan completed a notable piece; exceptional=true in payload marks a masterwork

    // M14 economy events (3500-range) — a fresh range rather than packing more values into the
    // 3300s (highest used there: ArtisanCrafted = 3307), per the M14 deep-review finding
    // (docs/phases/m14_economy_independent_wealth.md). TradePaid (14.1) is the first: a real
    // Wealth transfer from a destination settlement's precious-commodity reserves to a merchant
    // and their home settlement, distinct from the pre-existing MerchantTradeCompleted (which is
    // a silent/notable status-gain marker, not a resource transfer).
    TradePaid               = 3500,
    // M14 14.2 — persistent trade routes / caravan transit (docs/phases/m14_economy_independent_wealth.md).
    // TradeRouteFormed also fires (with Reopened=true in its payload) when a Severed route
    // automatically reopens — see decision in Tier2BehaviorPhase.ReopenRoute rather than a
    // separate event type for that case.
    TradeRouteFormed        = 3501,  // a settlement pair graduated to (or reopened as) a persistent TradeRoute
    TradeRouteSevered       = 3502,  // TradeRoute closed: war between endpoints' civs, a lost endpoint, or sustained caravan losses
    CaravanRaided           = 3503,  // in-transit caravan lost — Cause in payload distinguishes war/disaster/piracy
    // M14 14.3 — goal fulfillment via trade: a CovetArtifact goal-holder bought the coveted
    // artifact from its living Character/Settlement owner (an alternative to the claim-if-Lost
    // path already covered by ArtifactTransferred with Reason="claim").
    ArtifactPurchased       = 3504,
    // M14 14.4 — Guild organizations, treasuries, and civ-level economic ruin
    // (docs/phases/m14_economy_independent_wealth.md decision 9/10, phase-sequence "14.4" entry).
    // A first-time-populated Guild organization forming (or a merchant joining an existing one
    // fires no separate event — see Tier2BehaviorPhase.FormOrJoinGuild's doc comment).
    GuildFormed             = 3505,
    // Decision 9's two treasury commands: any member depositing personal Wealth into their
    // Organization's Treasury (no authority check), and the Leader-only converse.
    TreasuryContribution    = 3506,
    TreasuryWithdrawal      = 3507,
    // Decision 10: a Civilization-kind Organization's Treasury crosses from >=0 into negative.
    // Edge-triggered (fires once per crossing, not every tick it stays negative) — see
    // Organization.TreasuryInsolvencyFlagged and CivTracker.CheckTreasuryInsolvency.
    TreasuryInsolvent       = 3508,
    // War reparations (deep-review finding folded into 14.4): a one-time Wealth transfer from the
    // losing civ's Treasury to the winner's on war resolution (CivTracker.EndWarBetween) — allowed
    // to drive the loser's Treasury negative, which is exactly the TreasuryInsolvent trigger above.
    WarReparationsPaid      = 3509,
    // A Guild's Leader seat changes hands via SuccessionResolver.SelectSuccessor (unmodified —
    // see the M12 audit note this milestone is bound by). Distinct from SuccessionOccurred (civ
    // ruler succession) because SummaryBuilder/CausalEdgeBuilder parse that event's payload
    // assuming civ-specific fields (CivId, RulerOrdinal) that don't apply to a Guild.
    GuildLeadershipTransferred = 3510,

    // M3+ artifacts / religion (6000+/4000+ ranges reserved)
    ArtifactCreated         = 6001,
    ArtifactDestroyed       = 6002,
    ArtifactTransferred     = 6003,  // ownership changed: inheritance, conquest, or claim
    ReligionFounded         = 4003,
    ReligionExtinct         = 4004,
    GodModeDisasterTriggered    = 9001,
    GodModeEntitySpawned        = 9002,
    GodModeCharacterCreated     = 9003,
    GodModeArtifactPlaced       = 9004,
    GodModeCivilizationForced   = 9005,
    GodModeCharacterNudged      = 9006,

    // M4 Phase 1 — Diplomatic emissary events (5000-range)
    EmissaryDispatched          = 5001,  // civ sent an emissary to a known civ
    EmissaryLost                = 5002,  // emissary did not survive the journey
    ReligiousEmissaryArrived    = 5003,  // successful religious mission; awe seeds planted
    CivIntelGathered            = 5004,  // spy emissary returned with intelligence

    // M11 — sea voyage events (5100-range)
    SeaVoyageEmbarked           = 5101,  // character departed a Port on a sea voyage
    SeaVoyageCompleted          = 5102,  // character reached the far shore
    // V2: SeaVoyageLost = 5103 — weather/sea-monster failure hook, not built yet
}

public static class VerbClassification
{
    public static VerbClass Classify(EventType type) => type switch
    {
        EventType.VolcanicEruption   => VerbClass.Destruction,
        EventType.EarthquakeOccurred => VerbClass.Destruction,
        EventType.WildfireOccurred   => VerbClass.Destruction,
        EventType.FloodOccurred      => VerbClass.Destruction,
        EventType.DroughtBegan       => VerbClass.Destruction,
        EventType.DroughtEnded       => VerbClass.Creation,
        EventType.SeaLevelChanged    => VerbClass.Transformation,
        EventType.BiomeChanged       => VerbClass.Transformation,
        EventType.ClimateShifted     => VerbClass.Transformation,
        EventType.ResourceRecovered  => VerbClass.Maintenance,
        // M2+ stubs — reasonable defaults
        EventType.CharacterBorn           => VerbClass.Creation,
        EventType.CharacterDied           => VerbClass.Transformation, // Destruction floor=Regional floods DB; impact drives tier for notable deaths
        EventType.CharacterGrieved        => VerbClass.Transformation,
        EventType.CharacterFlourishing    => VerbClass.Creation,
        EventType.CharacterSpiraling      => VerbClass.Transformation,
        EventType.CharacterMarried        => VerbClass.Transfer,
        EventType.CharacterExiled         => VerbClass.Transformation,
        EventType.AllianceFormed          => VerbClass.Transfer,
        EventType.AllianceBroken          => VerbClass.Destruction,
        EventType.WarDeclared             => VerbClass.Conflict,
        EventType.WarEnded                => VerbClass.Maintenance,
        EventType.BattleOccurred          => VerbClass.Conflict,
        EventType.RivalryFormed           => VerbClass.Conflict,
        EventType.Negotiated              => VerbClass.Maintenance,
        EventType.ArtworkCreated          => VerbClass.Creation,
        EventType.GoalFormed              => VerbClass.Transformation,
        EventType.GoalResolved            => VerbClass.Transformation,
        EventType.DebtIncurred            => VerbClass.Transfer,
        EventType.DebtForgiven            => VerbClass.Maintenance,
        EventType.RivalryPlacated         => VerbClass.Maintenance,
        EventType.CharacterDefected       => VerbClass.Transformation,
        EventType.RivalsReconciled        => VerbClass.Maintenance,
        EventType.RivalryEscalatedToFeud  => VerbClass.Conflict,
        EventType.CharacterEstranged      => VerbClass.Destruction,
        EventType.OathBroken              => VerbClass.Destruction,
        EventType.CivilizationFounded     => VerbClass.Creation,
        EventType.CivilizationCollapsed   => VerbClass.Destruction,
        EventType.SettlementFounded       => VerbClass.Creation,
        EventType.SettlementDestroyed     => VerbClass.Destruction,
        EventType.SettlementConquered     => VerbClass.Transfer,
        EventType.SuccessionOccurred      => VerbClass.Transfer,
        EventType.SettlementStraining     => VerbClass.Transformation,
        EventType.SettlementGrew          => VerbClass.Creation,
        EventType.SettlementShrank        => VerbClass.Destruction,
        EventType.SettlementAbandoned     => VerbClass.Destruction,
        EventType.DiseaseOutbreak         => VerbClass.Destruction,
        EventType.DiseaseRecovered        => VerbClass.Maintenance,
        EventType.WildlifeRaid            => VerbClass.Destruction,
        EventType.SuccessionCrisis        => VerbClass.Transformation,
        EventType.AppointedToRole         => VerbClass.Transfer,
        EventType.DismissedFromRole       => VerbClass.Transfer,
        EventType.MerchantTradeCompleted  => VerbClass.Transfer,
        EventType.TradePaid               => VerbClass.Transfer,
        EventType.ScholarDiscovery        => VerbClass.Creation,
        EventType.PhysicianHealed         => VerbClass.Maintenance,
        EventType.CharacterCrystallized   => VerbClass.Transformation,
        EventType.ArtisanCrafted          => VerbClass.Creation,
        EventType.ArtifactCreated         => VerbClass.Creation,
        EventType.ArtifactDestroyed       => VerbClass.Destruction,
        EventType.ArtifactTransferred     => VerbClass.Transfer,
        EventType.ReligionFounded         => VerbClass.Creation,
        EventType.ReligionExtinct         => VerbClass.Destruction,
        EventType.GodModeDisasterTriggered    => VerbClass.Destruction,
        EventType.GodModeEntitySpawned        => VerbClass.Creation,
        EventType.GodModeCharacterCreated     => VerbClass.Creation,
        EventType.GodModeArtifactPlaced       => VerbClass.Creation,
        EventType.GodModeCivilizationForced   => VerbClass.Transformation,
        EventType.GodModeCharacterNudged      => VerbClass.Transformation,
        // Beast events
        EventType.BeastSpawned     => VerbClass.Creation,
        EventType.BeastAwakened    => VerbClass.Creation,
        EventType.BeastDied        => VerbClass.Transformation, // Destruction floor=Regional; most beast deaths are old-age, not narrative
        EventType.BeastSlain       => VerbClass.Destruction,
        EventType.BeastReproduced  => VerbClass.Creation,
        EventType.BeastEncountered  => VerbClass.Interaction,
        EventType.BeastAttackedChar  => VerbClass.Interaction,
        EventType.TerritoryExpanded  => VerbClass.Transfer,
        EventType.TerritoryLost      => VerbClass.Destruction,
        EventType.ImprovementBuilt   => VerbClass.Creation,
        EventType.CivTraitAcquired   => VerbClass.Transformation,
        EventType.CivSplintered      => VerbClass.Transformation,
        // M4 Phase 1 emissary events
        EventType.EmissaryDispatched       => VerbClass.Transfer,
        EventType.EmissaryLost             => VerbClass.Destruction,
        EventType.ReligiousEmissaryArrived => VerbClass.Interaction,
        EventType.CivIntelGathered         => VerbClass.Interaction,

        EventType.SeaVoyageEmbarked        => VerbClass.Transformation,
        EventType.SeaVoyageCompleted       => VerbClass.Transformation,

        // M14 14.2 — persistent trade routes / caravan transit
        EventType.TradeRouteFormed         => VerbClass.Creation,
        EventType.TradeRouteSevered        => VerbClass.Destruction,
        EventType.CaravanRaided            => VerbClass.Destruction,
        EventType.ArtifactPurchased        => VerbClass.Transfer,

        // M14 14.4 — Guild organizations, treasuries, civ-level economic ruin, war reparations
        EventType.GuildFormed              => VerbClass.Creation,
        EventType.TreasuryContribution     => VerbClass.Transfer,
        EventType.TreasuryWithdrawal       => VerbClass.Transfer,
        EventType.TreasuryInsolvent        => VerbClass.Destruction,
        EventType.WarReparationsPaid       => VerbClass.Transfer,
        EventType.GuildLeadershipTransferred => VerbClass.Transformation,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No VerbClass mapping")
    };
}

/// <summary>
/// Typed war outcome values used in WarEndedPayload and MetricsCollector (D5).
/// String constants preserve JSON backward compatibility (old rows are unaffected).
/// </summary>
public static class WarOutcome
{
    public const string Truce      = "truce";
    public const string Conquest   = "conquest";
    public const string Surrender  = "surrender";
    public const string Destruction = "destruction";
}

/// <summary>
/// War cause string constants for WarDeclaredPayload (D5 — opportunistic causes added).
/// String form keeps the history log human-readable and backward compatible.
/// </summary>
public static class WarCause
{
    public const string CharacterEncounter = "character_encounter";
    public const string BorderTension      = "border_tension";
    // D5 opportunistic causes:
    public const string SuccessionCrisis   = "succession_crisis";  // target civ in power vacuum
    public const string WeakNeighbor       = "weak_neighbor";      // target has disease/famine
    public const string ResourceShortage   = "resource_shortage";  // aggressor is starving
}
