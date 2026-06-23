namespace WorldEngine.Sim.Core;

public enum Season { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }

public enum SimSpeed { Paused, Slow, Normal, Fast, Ultrafast }

public enum OverlayType { Biome, Elevation, Temperature, Moisture, Resources, MagicIntensity }

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

    // M2+ character actions (3100-range)
    AllianceFormed          = 3101,
    AllianceBroken          = 3102,
    WarDeclared             = 3103,
    WarEnded                = 3104,
    BattleOccurred          = 3105,
    RivalryFormed           = 3106,
    Negotiated              = 3107,

    // M2+ civilization/settlement (3200-range)
    CivilizationFounded     = 3201,
    CivilizationCollapsed   = 3202,
    SettlementFounded       = 3203,
    SettlementDestroyed     = 3204,
    SuccessionOccurred      = 3205,

    // M2+ population events (3400-range)
    SettlementGrew          = 3401,
    SettlementShrank        = 3402,
    SettlementAbandoned     = 3403,

    // M2+ Tier 2 character events (3300-range)
    AppointedToRole         = 3301,
    DismissedFromRole       = 3302,
    MerchantTradeCompleted  = 3303,
    ScholarDiscovery        = 3304,
    PhysicianHealed         = 3305,
    CharacterCrystallized   = 3306,

    // M3+ artifacts / religion (6000+/4000+ ranges reserved)
    ArtifactCreated         = 6001,
    ArtifactDestroyed       = 6002,
    ReligionFounded         = 4003,
    ReligionExtinct         = 4004,
    GodModeDisasterTriggered    = 9001,
    GodModeEntitySpawned        = 9002,
    GodModeCharacterCreated     = 9003,
    GodModeArtifactPlaced       = 9004,
    GodModeCivilizationForced   = 9005,
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
        EventType.CharacterMarried        => VerbClass.Transfer,
        EventType.CharacterExiled         => VerbClass.Transformation,
        EventType.AllianceFormed          => VerbClass.Transfer,
        EventType.AllianceBroken          => VerbClass.Destruction,
        EventType.WarDeclared             => VerbClass.Conflict,
        EventType.WarEnded                => VerbClass.Maintenance,
        EventType.BattleOccurred          => VerbClass.Conflict,
        EventType.RivalryFormed           => VerbClass.Conflict,
        EventType.Negotiated              => VerbClass.Maintenance,
        EventType.CivilizationFounded     => VerbClass.Creation,
        EventType.CivilizationCollapsed   => VerbClass.Destruction,
        EventType.SettlementFounded       => VerbClass.Creation,
        EventType.SettlementDestroyed     => VerbClass.Destruction,
        EventType.SuccessionOccurred      => VerbClass.Transfer,
        EventType.SettlementGrew          => VerbClass.Creation,
        EventType.SettlementShrank        => VerbClass.Destruction,
        EventType.SettlementAbandoned     => VerbClass.Destruction,
        EventType.AppointedToRole         => VerbClass.Transfer,
        EventType.DismissedFromRole       => VerbClass.Transfer,
        EventType.MerchantTradeCompleted  => VerbClass.Transfer,
        EventType.ScholarDiscovery        => VerbClass.Creation,
        EventType.PhysicianHealed         => VerbClass.Maintenance,
        EventType.CharacterCrystallized   => VerbClass.Transformation,
        EventType.ArtifactCreated         => VerbClass.Creation,
        EventType.ArtifactDestroyed       => VerbClass.Destruction,
        EventType.ReligionFounded         => VerbClass.Creation,
        EventType.ReligionExtinct         => VerbClass.Destruction,
        EventType.GodModeDisasterTriggered    => VerbClass.Destruction,
        EventType.GodModeEntitySpawned        => VerbClass.Creation,
        EventType.GodModeCharacterCreated     => VerbClass.Creation,
        EventType.GodModeArtifactPlaced       => VerbClass.Creation,
        EventType.GodModeCivilizationForced   => VerbClass.Transformation,
        // Beast events
        EventType.BeastSpawned     => VerbClass.Creation,
        EventType.BeastAwakened    => VerbClass.Creation,
        EventType.BeastDied        => VerbClass.Transformation, // Destruction floor=Regional; most beast deaths are old-age, not narrative
        EventType.BeastSlain       => VerbClass.Destruction,
        EventType.BeastReproduced  => VerbClass.Creation,
        EventType.BeastEncountered  => VerbClass.Interaction,
        EventType.BeastAttackedChar => VerbClass.Interaction,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No VerbClass mapping")
    };
}
