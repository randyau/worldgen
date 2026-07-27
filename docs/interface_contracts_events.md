<!-- contract-snapshot-hash: 8804eb586a103681 -->
# Interface Contracts — Events & Enumerations
**Parent:** `interface_contracts.md` | **Version:** 0.9 | **Status:** M3 complete (M4 Phase 1 emissary system included)

Covers: Events table schema, EventEntities table, CausalEdges table, EventsReadable view, typed payloads, EventType ranges, SimEvent, IHistoryGraphReadOnly, key enumerations, ID wrappers.

---

## Database Schema Overview

Three tables and one view make up the event log in `world.db`. All are created by `DatabaseSchema.cs` (`WorldEngine.Sim/Persistence/DatabaseSchema.cs`).

```
Events          — one row per simulation event; denormalized hot-query columns
EventEntities   — many-to-many: events ↔ entity IDs with Role semantics
CausalEdges     — directed edges encoding causal predecessor/successor links
EventsReadable  — view over Events with enum integers decoded to text
```

---

## Events Table

Full DDL:

```sql
CREATE TABLE IF NOT EXISTS Events (
    Id               INTEGER PRIMARY KEY,
    Type             INTEGER NOT NULL,   -- EventType enum value
    TypeName         TEXT    NOT NULL,   -- EventType name, e.g. "CharacterDied"
    Domain           TEXT    NOT NULL,   -- Category bucket (see Domain Values below)
    Year             INTEGER NOT NULL,
    Season           INTEGER NOT NULL,   -- 0=Spring 1=Summer 2=Autumn 3=Winter
    Tick             INTEGER NOT NULL,
    LocationX        INTEGER,            -- Tile X; NULL if no location
    LocationY        INTEGER,            -- Tile Y; NULL if no location
    TierInvolvement  INTEGER NOT NULL,   -- EventTier enum: 0=Background … 3=Headline
    VerbClass        INTEGER NOT NULL,   -- VerbClass enum: 0=Creation … 6=Interaction
    PopulationImpact INTEGER NOT NULL,   -- PopulationImpact enum: 0=None … 4=Catastrophic
    IsFirstOfKind    INTEGER NOT NULL,   -- 0/1 boolean
    IsGodMode        INTEGER NOT NULL,   -- 0/1 boolean
    ActorId          INTEGER,            -- Primary actor's EntityId; NULL if not applicable
    ActorName        TEXT,               -- Primary actor's display name (denormalized)
    CivId            INTEGER,            -- Associated civilization ID; NULL if not applicable
    SettlementName   TEXT,               -- Associated settlement name (denormalized)
    PayloadJson      TEXT    NOT NULL    -- Typed payload serialized as JSON
);
```

### Denormalized Columns

`ActorId`, `ActorName`, `CivId`, and `SettlementName` are redundant with `PayloadJson` but stored flat for fast filtering without `json_extract`. These four columns are indexed (see Indexes below).

**Before denormalization** — filtering all war events for a civilization required:

```sql
-- Slow: json_extract over every row in the type range
SELECT * FROM Events
WHERE Type BETWEEN 3101 AND 3199
  AND json_extract(PayloadJson, '$.DeclarerCivId') = 42;
```

**After denormalization** — uses the `idx_events_civid` partial index:

```sql
-- Fast: index seek on denormalized column
SELECT * FROM Events
WHERE Type BETWEEN 3101 AND 3199
  AND CivId = 42;
```

### TypeName and Domain Columns

`TypeName` is the string name of the `EventType` enum value (e.g. `"CharacterDied"`, `"WarDeclared"`). It is written at insert time and never changes. Useful for ad-hoc queries without needing the enum reference.

`Domain` is a high-level category bucket. Values:

| Domain          | EventType range(s)      |
|-----------------|-------------------------|
| `Environmental` | 1001–1099               |
| `Beast`         | 2001–2099               |
| `Character`     | 3001–3099, 3101–3199    |
| `Civilization`  | 3201–3299               |
| `Tier2`         | 3301–3399               |
| `Population`    | 3401–3499               |
| `Religion`      | 4001–4099               |
| `Seafaring`     | 5100–5199               |
| `Artifact`      | 6001–6099               |
| `GodMode`       | 9001–9099               |

### Indexes

```sql
CREATE INDEX idx_events_year     ON Events(Year);
CREATE INDEX idx_events_type     ON Events(Type);
CREATE INDEX idx_events_tier     ON Events(TierInvolvement);
CREATE INDEX idx_events_location ON Events(LocationX, LocationY) WHERE LocationX IS NOT NULL;
CREATE INDEX idx_events_civid    ON Events(CivId)    WHERE CivId IS NOT NULL;
CREATE INDEX idx_events_actorid  ON Events(ActorId)  WHERE ActorId IS NOT NULL;
```

---

## EventEntities Table

Maps events to all related entity IDs with a `Role` label. Written by Phase 7 when `PendingEvent.EntityIds` is non-null.

```sql
CREATE TABLE IF NOT EXISTS EventEntities (
    EventId    INTEGER NOT NULL REFERENCES Events(Id),
    EntityId   INTEGER NOT NULL,
    Role       TEXT    NOT NULL DEFAULT 'Primary',
    PRIMARY KEY (EventId, EntityId)
);
CREATE INDEX idx_event_entities_entity ON EventEntities(EntityId);
```

### Role Field Semantics

| Role        | Meaning                                                        |
|-------------|----------------------------------------------------------------|
| `Primary`   | The main actor who caused or performed the event               |
| `Secondary` | A participant who was affected or involved (target, witness)   |
| `Witness`   | An observer with narrative significance but no causal role     |

**Important:** `EventEntities` must be deleted before `Events` in any `Truncate()` call due to the FK constraint.

### Querying by Entity

```sql
-- All events involving a character (any role)
SELECT e.* FROM Events e
JOIN EventEntities ee ON ee.EventId = e.Id
WHERE ee.EntityId = @characterId;

-- Only events where the character was the primary actor
SELECT e.* FROM Events e
JOIN EventEntities ee ON ee.EventId = e.Id
WHERE ee.EntityId = @characterId AND ee.Role = 'Primary';

-- Shared history between two characters
SELECT e.* FROM Events e
JOIN EventEntities a ON a.EventId = e.Id AND a.EntityId = @charA
JOIN EventEntities b ON b.EventId = e.Id AND b.EntityId = @charB;
```

---

## CausalEdges Table

Directed graph edges encoding causal relationships between events.

```sql
CREATE TABLE IF NOT EXISTS CausalEdges (
    PredecessorId INTEGER NOT NULL REFERENCES Events(Id),
    SuccessorId   INTEGER NOT NULL REFERENCES Events(Id),
    PRIMARY KEY (PredecessorId, SuccessorId)
);
```

Queried via `IHistoryGraphReadOnly.GetCausalPredecessors` / `GetCausalSuccessors` / `GetCausalChain`.

---

## EventsReadable View

Decodes integer enum columns to human-readable text. Useful for direct DB inspection and ad-hoc queries.

```sql
CREATE VIEW IF NOT EXISTS EventsReadable AS
SELECT
    e.Id,
    e.TypeName,
    e.Domain,
    e.Year,
    CASE e.Season
        WHEN 0 THEN 'Spring' WHEN 1 THEN 'Summer'
        WHEN 2 THEN 'Autumn' WHEN 3 THEN 'Winter'
    END AS SeasonName,
    CASE e.TierInvolvement
        WHEN 0 THEN 'Background' WHEN 1 THEN 'Character'
        WHEN 2 THEN 'Regional'   WHEN 3 THEN 'Headline'
    END AS Tier,
    CASE e.VerbClass
        WHEN 0 THEN 'Creation'       WHEN 1 THEN 'Destruction'
        WHEN 2 THEN 'Transformation' WHEN 3 THEN 'Transfer'
        WHEN 4 THEN 'Conflict'       WHEN 5 THEN 'Maintenance'
        WHEN 6 THEN 'Interaction'
    END AS Verb,
    CASE e.PopulationImpact
        WHEN 0 THEN 'None'     WHEN 1 THEN 'Minor'
        WHEN 2 THEN 'Moderate' WHEN 3 THEN 'Major'
        WHEN 4 THEN 'Catastrophic'
    END AS PopImpact,
    e.ActorId,
    e.ActorName,
    e.CivId,
    e.SettlementName,
    e.LocationX,
    e.LocationY,
    e.IsFirstOfKind,
    e.IsGodMode,
    e.PayloadJson
FROM Events e;
```

Example: `SELECT TypeName, Year, SeasonName, Tier, ActorName FROM EventsReadable WHERE Domain = 'Character' ORDER BY Year LIMIT 20;`

---

## Typed Payload Reference

`PayloadJson` is always the JSON serialization of a specific sealed record type from `WorldEngine.Sim/Events/Payloads.cs`. The correct type is determined by `EventType`. All records are `internal sealed record`.

### Character Domain

| EventType              | Payload record               | Key fields                                                                 |
|------------------------|------------------------------|----------------------------------------------------------------------------|
| `CharacterBorn`        | `CharacterBornPayload`       | `CharacterId`, `CharacterName`, `Epithet?`, `Ambition`, `Aggression`, `Role?`, `Source?`, `AncestryId?` |
| `CharacterDied`        | `CharacterDeathPayload`      | `CharacterId`, `CharacterName`, `Cause`, `AgeSeason`, `AncestryId?`, `MaxAgeSeason`       |
| `CharacterFlourishing` | `CharacterWellbeingPayload`  | `CharacterId`, `CharacterName`, `Wellbeing`                                |
| `CharacterSpiraling`   | `CharacterWellbeingPayload`  | `CharacterId`, `CharacterName`, `Wellbeing`                                |
| `CharacterGrieved`     | `CharacterGriefPayload`      | `CharacterId`, `CharacterName`, `DeceasedId`, `DeceasedName`, `Intensity`, `Wellbeing`, `HasAvenge` |
| `ArtworkCreated`       | `ArtworkCreatedPayload`      | `CharacterId`, `CharacterName`, `ArtType`, `Wellbeing`                     |
| `GoalFormed`           | `GoalEventPayload`           | `CharacterId`, `CharacterName`, `GoalType`, `GoalObject`, `TargetId?`, `Intensity`, `Outcome="formed"` |
| `GoalResolved`         | `GoalEventPayload`           | same fields; `Outcome="completed"` or `"abandoned"`                        |
| `CharacterCrystallized`| `CharacterCrystallizedPayload` | `OldCharacterId`, `OldName`, `NewCharacterId`, `NewName`                  |

### Alliance / Rivalry / War Domain

| EventType         | Payload record             | Key fields                                                                  |
|-------------------|----------------------------|-----------------------------------------------------------------------------|
| `AllianceFormed`  | `AllianceFormedPayload`    | `DeclarerId`, `DeclarerName`, `TargetId`, `TargetName`, `DeclarerCivId`, `TargetCivId` |
| `AllianceBroken`  | `AllianceBrokenPayload`    | `CharacterAId`, `CharacterAName`, `CharacterBId`, `CharacterBName`, `Reason` |
| `RivalryFormed`   | `RivalryFormedPayload`     | `CharacterId`, `CharacterName`, `TargetId`, `TargetName`                    |
| `WarDeclared`     | `WarDeclaredPayload`       | `DeclarerId`, `DeclarerName`, `DeclarerCivId`, `DeclarerCivName`, `TargetCivId`, `TargetCivName`, `Cause`, `CauseDescription`, `WarNumber`, `DeclarerTraits?` |
| `WarEnded`        | `WarEndedPayload`          | `CivAId`, `CivAName`, `CivBId`, `CivBName`, `Outcome`, `WarNumber`          |
| `Negotiated`      | `NegotiatedPayload`        | `CharacterId`, `CharacterName`, `TargetId`, `TrustGain`                     |
| `BattleOccurred`  | `BattlePayload`            | `RaiderId`, `RaiderName`, `Damage`, `SettlementHealth`, `RaidOutcome`, `RaiderWounded`, `RaiderHealthPct` |

### Civilization / Settlement Domain

| EventType                | Payload record                 | Key fields                                                                  |
|--------------------------|--------------------------------|-----------------------------------------------------------------------------|
| `CivilizationFounded`    | `CivFoundedPayload`            | `CivId`, `CivName`, `FounderId`, `FounderName`                              |
| `CivilizationCollapsed`  | `CivCollapsedPayload`          | `CivId`, `Reason?`                                                          |
| `SettlementFounded`      | `SettlementFoundedPayload`     | `FounderId`, `FounderName`, `CivId`, `CivName`, `StartingPopulation`        |
| `SettlementDestroyed`    | `SettlementDestroyedPayload`   | `FounderId`, `DestroyerId`, `DestroyerName`, `TimesSettled`                 |
| `SettlementConquered`    | `SettlementConqueredPayload`   | `ConquererId`, `ConquererName`, `ConquerorCivId`, `PreviousCivId`, `SurvivingPop` |
| `SettlementAbandoned`    | `SettlementAbandonedPayload`   | `FounderId`, `FoundedYear`, `TimesSettled`, `Population`                    |
| `SettlementStraining`    | `SettlementStrainPayload`      | `Resource`, `Ratio`, `Impact`                                               |
| `SuccessionOccurred`     | `SuccessionPayload`            | `PredecessorId`, `PredecessorName`, `PredecessorOrdinal`, `SuccessorId`, `SuccessorName`, `SuccessorOrdinal`, `CivTraits?` |
| `SuccessionCrisis`       | `SuccessionCrisisPayload`      | `CivId`, `CivName`, `CrisisEndYear`                                         |
| `DiseaseOutbreak`        | `DiseaseOutbreakPayload`       | `Population`, `DensityFactor`, `ContactFactor`, `FamineFactor`, `InWar`, `InFamine` |
| `DiseaseRecovered`       | `DiseaseRecoveredPayload`      | `Population`, `DurationYears`                                               |
| `WildlifeRaid`           | `WildlifeRaidPayload`          | `PopulationBefore`, `PopulationLost`, `DefenderId`, `DefenderName?`         |

### Tier 2 Specialist Domain

| EventType               | Payload record               | Key fields                                                                  |
|-------------------------|------------------------------|-----------------------------------------------------------------------------|
| `AppointedToRole`       | `SpecialistAppointedPayload` | `CharacterId`, `CharacterName`, `Role`, `Population`, `Threshold`           |
| `MerchantTradeCompleted`| `MerchantTradePayload`       | `CharacterId`, `CharacterName`, `TradedResource`, `DestX`, `DestY`          |
| `ScholarDiscovery`      | `ScholarDiscoveryPayload`    | `CharacterId`, `CharacterName`, `DiscoveryType`, `BonusKey`, `BonusAmount`  |
| `PhysicianHealed`       | `PhysicianHealedPayload`     | `CharacterId`, `CharacterName`, `PatientId`, `PatientName`, `Healed`, `Critical` |
| `ArtisanCrafted`        | `ArtisanCraftedPayload`      | `CharacterId`, `CharacterName`, `GoodType`                                  |
| `DismissedFromRole`     | `SpecialistDismissedPayload` | `CharacterId`, `CharacterName`, `Role`, `Reason`                            |

### Beast Domain

| EventType              | Payload record             | Key fields                                                                  |
|------------------------|----------------------------|-----------------------------------------------------------------------------|
| `BeastSpawned`         | `BeastSpawnedPayload`      | `BeastId`, `BeastName`, `SpeciesId`, `IsLegendary`                          |
| `BeastDied`/`BeastSlain`| `BeastDeathPayload`       | `BeastId`, `BeastName`, `SpeciesId`, `IsLegendary`, `AgeSeason`, `Cause`, `KillerId`, `KillerName?` |
| `BeastReproduced`      | `BeastReproducedPayload`   | `ParentId`, `ParentName`, `OffspringId`, `OffspringName`, `SpeciesId`       |
| `BeastEncountered`     | `BeastEncounterPayload`    | `AttackerId`, `AttackerName`, `TargetId`, `TargetName`                      |
| `BeastAttackedChar`    | `BeastCharEncounterPayload`| `CharacterId`, `CharacterName`, `BeastId`, `BeastName`, `Damage`, `CounterDamage`, `CharHealthAfter`, `BeastHealthAfter` |

### Environmental Domain

| EventType          | Payload record           | Key fields                                          |
|--------------------|--------------------------|-----------------------------------------------------|
| Most disasters     | `DisasterPayload`        | `Intensity`                                         |
| `BiomeChanged`     | `BiomeChangedPayload`    | `From`, `To`, `GlobalTemperatureAnomaly`            |
| `SeaLevelChanged`  | `SeaLevelChangedPayload` | `PreviousLevel`, `NewLevel`, `Delta`                |
| *(no data needed)* | `EmptyPayload`           | `{}` — used when no payload fields are meaningful   |

### Territory / Improvement Domain

| EventType           | Payload record              | Key fields                                                              |
|---------------------|-----------------------------|-------------------------------------------------------------------------|
| `TerritoryExpanded` | `TerritoryExpandedPayload`  | `CivId`, `CivName`, `CityTileX`, `CityTileY`, `TileCount`, `TotalOwned` |
| `TerritoryLost`     | `TerritoryLostPayload`      | `CivId`, `CivName`, `CityTileX`, `CityTileY`, `TilesReleased`, `TotalOwned`, `Reason` |
| `ImprovementBuilt`  | `ImprovementBuiltPayload`   | `BuilderId`, `BuilderName`, `CivId`, `TileX`, `TileY`, `ImprovementType` |
| `CivTraitAcquired`  | `CivTraitAcquiredPayload`   | `CivId`, `CivName`, `Trait`, `Reason`                                   |
| `CivSplintered`     | `CivSplinteredPayload`      | `ParentCivId`, `ParentCivName`, `NewCivId`, `NewCivName`, `NewRulerId`, `NewRulerName`, `SettlementsSeceded`, `PopulationTransferred`, `LeaderUnrest`, `TileX`, `TileY` |

### Emissary Domain (M4 Phase 1)

| EventType                    | Payload record                      | Key fields                                                                |
|------------------------------|-------------------------------------|---------------------------------------------------------------------------|
| `EmissaryDispatched`         | `EmissaryDispatchedPayload`         | `FromCivId`, `FromCivName`, `ToCivId`, `ToCivName`, `Purpose`, `ArrivalYear`, `SurvivalChance` |
| `EmissaryLost`               | `EmissaryLostPayload`               | `FromCivId`, `FromCivName`, `ToCivId`, `ToCivName`, `Purpose`             |
| `ReligiousEmissaryArrived`   | `ReligiousEmissaryArrivedPayload`   | `FromCivId`, `FromCivName`, `ToCivId`, `ToCivName`, `CharactersAffected`  |
| `CivIntelGathered`           | `CivIntelGatheredPayload`           | `FromCivId`, `FromCivName`, `ToCivId`, `ToCivName`, `NewConfidence`       |

### Religion Domain

| EventType        | Payload record           | Key fields                                                           |
|------------------|--------------------------|----------------------------------------------------------------------|
| `ReligionFounded` | `ReligionFoundedPayload` | `FounderId`, `FounderName`, `Year`, `TileX`, `TileY`                |

### Seafaring Domain (M11)

| EventType             | Payload record      | Key fields                                                  |
|------------------------|---------------------|--------------------------------------------------------------|
| `SeaVoyageEmbarked`   | `SeaVoyagePayload`  | `CharacterId`, `CharacterName`, `CivId`, `TileX`, `TileY`    |
| `SeaVoyageCompleted`  | `SeaVoyagePayload`  | `CharacterId`, `CharacterName`, `CivId`, `TileX`, `TileY`    |

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
    BeastReproduced=2005 (not recorded — lifecycle only), BeastEncountered=2006, BeastAttackedChar=2007

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
    SettlementStraining=3206, SettlementConquered=3207,
    TerritoryExpanded=3208, TerritoryLost=3209,
    ImprovementBuilt=3210, CivTraitAcquired=3211, CivSplintered=3212

Tier2 specialist: 3301–3399
    AppointedToRole=3301, DismissedFromRole=3302, MerchantTradeCompleted=3303,
    ScholarDiscovery=3304, PhysicianHealed=3305, CharacterCrystallized=3306,
    ArtisanCrafted=3307

Population: 3401–3499
    SettlementGrew=3401, SettlementShrank=3402, SettlementAbandoned=3403,
    DiseaseOutbreak=3404, DiseaseRecovered=3405, WildlifeRaid=3406, SuccessionCrisis=3407

Religion:   4001–4099   (ReligionFounded=4003, ReligionExtinct=4004)
Emissary:   5001–5099   (EmissaryDispatched=5001, EmissaryLost=5002,
                         ReligiousEmissaryArrived=5003, CivIntelGathered=5004)
Seafaring:  5100–5199   (SeaVoyageEmbarked=5101, SeaVoyageCompleted=5102;
                         SeaVoyageLost=5103 reserved for a future weather/sea-monster failure hook)
Artifacts:  6001–6099   (ArtifactCreated=6001, ArtifactDestroyed=6002)
God Mode:   9001–9099   (GodModeDisasterTriggered=9001, GodModeEntitySpawned=9002,
                         GodModeCharacterCreated=9003, GodModeArtifactPlaced=9004,
                         GodModeCivilizationForced=9005)
```

---

## SimEvent (C# record)

```csharp
/// <summary>
/// An event in the simulation history log. Immutable once written.
/// PayloadJson always holds the JSON of a specific typed payload record
/// from WorldEngine.Sim.Events (see Typed Payload Reference above).
/// </summary>
public sealed record SimEvent
{
    public required EventId Id { get; init; }
    public required EventType Type { get; init; }
    public required string TypeName { get; init; }       // Enum name, e.g. "CharacterDied"
    public required string Domain { get; init; }         // Category, e.g. "Character"
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
    public long ActorId { get; init; }                   // Denormalized primary actor (0 if none)
    public string? ActorName { get; init; }              // Denormalized primary actor name
    public long CivId { get; init; }                     // Denormalized civ association (0 if none)
    public string? SettlementName { get; init; }         // Denormalized settlement name
    public required string PayloadJson { get; init; }
    /// <summary>
    /// Float significance score (0.0–1.0). Populated by SignificanceRescoringPass after simulation.
    /// Enables precise ranking of events within a tier (e.g. "top 10 events of a character's life").
    /// </summary>
    public float SignificanceScore { get; init; } = 0f;
    public string? GeneratedProse { get; init; }         // V2: LLM generation
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
}
```

---

## IHistoryQuery

Pre-indexed query API backed by SQLite summary tables built by `SummaryBuilder`. Obtained via `EventStore.GetHistoryQuery()`. Use for UI queries and story generation — avoids full-table scans.

```csharp
public interface IHistoryQuery
{
    CivSummary?     GetCivSummary(CivId civId);
    CharacterSummary? GetCharacterSummary(EntityId charId);
    IReadOnlyList<CharacterSummary> GetRulersOfCiv(CivId civId);
    CharacterSummary? GetRulerAtYear(CivId civId, int year);
    IReadOnlyList<SimEvent> GetCivHistory(CivId civId, int startYear, int endYear);
    IReadOnlyList<SimEvent> GetCharacterHistory(EntityId charId);
    IReadOnlyList<SimEvent> GetSignificantEvents(int startYear, int endYear, EventTier minTier);
    IReadOnlyList<ConflictRecord> GetConflictHistory(CivId civA, CivId civB);
    IReadOnlyList<CharacterSummary> FindCharactersByName(string name);
    IReadOnlyList<CivSummary> GetAllCivSummaries();
    IReadOnlyList<(long CauseEventId, SimEvent CauseEvent, string EdgeType)> GetCausalChain(long effectEventId, int maxDepth = 3);
    Dictionary<int, int> GetEventCountByDecade(int startYear, int endYear);
    IReadOnlyList<SimEvent> GetTileHistory(TileCoord coord, int maxEvents = 10);
}
```

Pre-aggregated summary types live in `WorldEngine.Sim/World/HistoryTypes.cs`:

- **`CharacterSummary`** — `CharacterId`, `Name`, `Epithet?`, `NameOrdinal`, `AncestryId?`, `CivId`, `CivName?`, `RulerOrdinal`, `BirthYear`, `DeathYear`, `DeathCause?`, `AgeSeasons`, `WarsInitiated`, `SettlementsFounded`, `ArtworksCreated`, `SignificantEventIds`
- **`CivSummary`** — `CivId`, `Name`, `FoundedYear`, `CollapseYear`, `IsCollapsed`, `PeakSettlements`, `TotalRulers`, `TotalWarsInitiated`, `TotalWarsSuffered`, `TotalYearsAtWar`, `DominantAncestry?`, `CulturalTraits`, `FirstRulerName?`, `LastRulerName?`
- **`ConflictRecord`** — `WarDeclarationEventId`, `CivAId`, `CivBId`, `DeclaredYear`, `EndedYear`, `Outcome`, `BattleCount`, `WarNumber`

---

## Key Enumerations

```csharp
public enum Season { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }
public enum SimSpeed { Paused, Slow, Normal, Fast, Ultrafast }
public enum OverlayType { Biome, Elevation, Temperature, Moisture, Resources, MagicIntensity, Territory }

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

## War Outcome and Cause Constants

String constants used in `WarEndedPayload.Outcome` and `WarDeclaredPayload.Cause`. String form keeps the history log human-readable and backward-compatible.

```csharp
public static class WarOutcome
{
    public const string Truce       = "truce";
    public const string Conquest    = "conquest";
    public const string Surrender   = "surrender";
    public const string Destruction = "destruction";
}

public static class WarCause
{
    public const string CharacterEncounter = "character_encounter";
    public const string BorderTension      = "border_tension";
    public const string SuccessionCrisis   = "succession_crisis";  // target civ in power vacuum
    public const string WeakNeighbor       = "weak_neighbor";      // target has disease/famine
    public const string ResourceShortage   = "resource_shortage";  // aggressor is starving
}
```

---

## Strongly-Typed ID Wrappers

See `interface_contracts_snapshot.md` for the full ID wrapper definitions. Summary:

```csharp
public readonly record struct EntityId(long Value);   // EntityId.New() / EnsureCounterExceeds()
public readonly record struct EventId(long Value);
public readonly record struct CivId(int Value);       // CivId.None = CivId(0); IsValid => Value > 0
public readonly record struct ModifierId(Guid Value); // ModifierId.New()
public readonly record struct ArtifactId(long Value); // ArtifactId.New()
```
