<!-- AUTO-GENERATED — do not edit. Run: python3 scripts/gen-interface-contracts.py -->

# Interface Contracts Snapshot — events

## SimEvent
**File:** `WorldEngine.Sim/World/SimEvent.cs:9`  
**Kind:** `class`

```csharp
public sealed record SimEvent
{
    public required EventId Id { get; init; }
    public required EventType Type { get; init; }
    public required string TypeName { get; init; }
    public required string Domain { get; init; }
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
    public long ActorId { get; init; }
    public string? ActorName { get; init; }
    public long CivId { get; init; }
    public string? SettlementName { get; init; }
    public required string PayloadJson { get; init; }
    public float SignificanceScore { get; init; } = 0f;
    public string? GeneratedProse { get; init; }  // V2: LLM generation
}
```

## IHistoryGraphReadOnly
**File:** `WorldEngine.Sim/World/IHistoryGraphReadOnly.cs:8`  
**Kind:** `interface`

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

## IHistoryQuery
**File:** `WorldEngine.Sim/World/IHistoryQuery.cs:10`  
**Kind:** `interface`

```csharp
public interface IHistoryQuery
{
    CivSummary? GetCivSummary(CivId civId);

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

## EventType
**File:** `WorldEngine.Sim/Core/Enumerations.cs:90`  
**Kind:** `enum`

```csharp
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
}
```

<!-- content-hash: 601498e0c0a767ba -->
