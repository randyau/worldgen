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
    Transfer = 3, Conflict = 4, Maintenance = 5
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
    // Environmental
    WildfireOccurred, FloodOccurred, VolcanicEruption, Earthquake, DroughtBegan, DroughtEnded,
    SeaLevelChanged, BiomeShifted, ResourceRecovered,
    // World-level (stubs for future phases)
    CharacterBorn, CharacterDied,
    CivilizationFounded, CivilizationCollapsed,
    ArtifactCreated, ArtifactDestroyed,
    ReligionFounded, ReligionExtinct,
    WarDeclared, WarEnded,
    GodModeDisasterTriggered, GodModeEntitySpawned,
    GodModeCharacterCreated, GodModeArtifactPlaced, GodModeCivilizationForced,
}
