# Interface Contracts — Events & Enumerations
**Parent:** `interface_contracts.md` | **Version:** 0.7 | **Status:** M2 complete

Covers: EventEntities table, EventType ranges, SimEvent, IHistoryGraphReadOnly, key enumerations, ID wrappers.

---

## EventEntities Table (M2+)

Cross-reference table in `world.db` linking events to entity IDs. Written by Phase 7 when `PendingEvent.EntityIds` is non-null.

```sql
CREATE TABLE IF NOT EXISTS EventEntities (
    EventId  INTEGER NOT NULL REFERENCES Events(Id),
    EntityId INTEGER NOT NULL,
    PRIMARY KEY (EventId, EntityId)
);
CREATE INDEX IF NOT EXISTS idx_evententities_entity ON EventEntities(EntityId);
```

Query all events for a character: `SELECT * FROM Events WHERE Id IN (SELECT EventId FROM EventEntities WHERE EntityId = @id)`.

**Important:** `EventEntities` must be deleted before `Events` in any `Truncate()` call (FK constraint).

---

## EventType Ranges

Values from `WorldEngine.Sim/Core/Enumerations.cs`.

```
Environmental:   1001–1099
    VolcanicEruption=1001, EarthquakeOccurred=1002, WildfireOccurred=1003, FloodOccurred=1004,
    DroughtBegan=1005, DroughtEnded=1006, SeaLevelChanged=1007, BiomeChanged=1008,
    ClimateShifted=1009, ResourceRecovered=1010

Beast:           2001–2099
    BeastSpawned=2001, BeastAwakened=2002, BeastDied=2003, BeastSlain=2004,
    BeastReproduced=2005, BeastEncountered=2006, BeastAttackedChar=2007

Character lifecycle: 3001–3099
    CharacterBorn=3001, CharacterDied=3002, CharacterMarried=3003, CharacterExiled=3004,
    CharacterGrieved=3005, CharacterFlourishing=3006, CharacterSpiraling=3007

Character/civ actions: 3101–3199
    AllianceFormed=3101, AllianceBroken=3102, WarDeclared=3103, WarEnded=3104,
    BattleOccurred=3105, RivalryFormed=3106, Negotiated=3107,
    ArtworkCreated=3108, GoalFormed=3109, GoalResolved=3110

Civilization/settlement: 3201–3299
    CivilizationFounded=3201, CivilizationCollapsed=3202, SettlementFounded=3203,
    SettlementDestroyed=3204, SuccessionOccurred=3205,
    SettlementStraining=3206, SettlementConquered=3207

Tier2 specialist: 3301–3399
    AppointedToRole=3301, DismissedFromRole=3302, MerchantTradeCompleted=3303,
    ScholarDiscovery=3304, PhysicianHealed=3305, CharacterCrystallized=3306

Population: 3401–3499
    SettlementGrew=3401, SettlementShrank=3402, SettlementAbandoned=3403

Religion:   4001–4099   (reserved; ReligionFounded=4003, ReligionExtinct=4004)
Artifacts:  6001–6099   (ArtifactCreated=6001, ArtifactDestroyed=6002)
God Mode:   9001–9099   (GodModeDisasterTriggered=9001, GodModeEntitySpawned=9002,
                         GodModeCharacterCreated=9003, GodModeArtifactPlaced=9004,
                         GodModeCivilizationForced=9005)
```

**Key events added in M2:** `CharacterGrieved/Flourishing/Spiraling`, `AllianceBroken`, `WarEnded`, `BattleOccurred`, `RivalryFormed`, `Negotiated`, `ArtworkCreated`, `GoalFormed`, `GoalResolved`, `SettlementStraining`, `SettlementConquered`

---

## SimEvent

```csharp
/// <summary>
/// An event in the simulation history log. Immutable once written.
/// </summary>
public sealed record SimEvent
{
    public required EventId Id { get; init; }
    public required EventType Type { get; init; }
    public required int Year { get; init; }
    public required Season Season { get; init; }
    public required long Tick { get; init; }
    public TileCoord? Location { get; init; }
    public IReadOnlyList<EntityId> PrimaryEntities { get; init; } = Array.Empty<EntityId>();
    public IReadOnlyList<EntityId> SecondaryEntities { get; init; } = Array.Empty<EntityId>();
    public required EventTier TierInvolvement { get; init; }
    public required VerbClass VerbClass { get; init; }
    public required PopulationImpact PopulationImpact { get; init; }
    public required bool IsFirstOfKind { get; init; }
    public required bool IsGodMode { get; init; }
    public required string PayloadJson { get; init; }
    public string? GeneratedProse { get; init; }  // V2: LLM generation
}
```

---

## IHistoryGraphReadOnly

```csharp
public interface IHistoryGraphReadOnly
{
    SimEvent? GetEvent(EventId id);
    IEnumerable<SimEvent> GetEventsByYear(int year);
    IEnumerable<SimEvent> GetEventsByYearRange(int fromYear, int toYear);
    IEnumerable<SimEvent> GetHeadlineEvents(int fromYear, int toYear);
    IEnumerable<SimEvent> GetEventsByLocation(TileCoord coord, int radiusWorldTiles = 0);
    IEnumerable<SimEvent> GetCausalPredecessors(EventId eventId);
    IEnumerable<SimEvent> GetCausalSuccessors(EventId eventId);
    IEnumerable<SimEvent> GetCausalChain(EventId eventId, int maxDepth = 10);
    IEnumerable<SimEvent> GetEventsByType(EventType type, int fromYear = 0, int toYear = int.MaxValue);
    IEnumerable<SimEvent> GetEventsByTier(EventTier tier, int fromYear = 0, int toYear = int.MaxValue);
    IEnumerable<SimEvent> GetEventsByVerbClass(VerbClass verbClass, int fromYear = 0, int toYear = int.MaxValue);
    IEnumerable<SimEvent> GetFirstOfKindEvents(int fromYear = 0, int toYear = int.MaxValue);
    // M2+: GetEventsByEntity, GetSharedHistory
}
```

---

## Key Enumerations

```csharp
public enum Season { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }
public enum SimSpeed { Paused, Slow, Normal, Fast, Ultrafast }
public enum OverlayType { Biome, Elevation, Temperature, Moisture, Resources, MagicIntensity }

public enum SimPhase
{
    Environmental     = 1,
    ResourceProduction = 2,
    PopulationDynamics = 3,
    EntityBehavior    = 4,
    CharacterDecisions = 5,
    ConflictResolution = 6,
    EventGeneration   = 7
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
```

---

## Strongly-Typed ID Wrappers

```csharp
public readonly record struct EntityId(long Value)
{
    public static EntityId New() => new(IdGenerator.Next());
}
public readonly record struct EventId(long Value);
public readonly record struct CivId(int Value);
public readonly record struct ModifierId(Guid Value)
{
    public static ModifierId New() => new(Guid.NewGuid());
}
public readonly record struct ArtifactId(long Value)
{
    public static ArtifactId New() => new(IdGenerator.Next());
}
```
