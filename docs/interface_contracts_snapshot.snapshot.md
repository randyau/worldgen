<!-- AUTO-GENERATED — do not edit. Run: python3 scripts/gen-interface-contracts.py -->

# Interface Contracts Snapshot — snapshot

## WorldSnapshot
**File:** `WorldEngine.Sim/World/WorldSnapshot.cs:66`  
**Kind:** `sealed record`

```csharp
public sealed record WorldSnapshot(
    // Time
    int CurrentYear,
    Season CurrentSeason,
    SimSpeed CurrentSpeed,
    bool IsPaused,
    long TicksPerSecond,

    // Map — flat array indexed by (y * WorldTileWidth + x); X wraps, Y clamps
    TileDisplayData[] AllTiles,
    OverlayType ActiveOverlay,
    int WorldTileWidth,
    int WorldTileHeight,

    // Event log
    IReadOnlyList<SimEvent> RecentEvents,

    // Tile inspector (null if no tile selected)
    TileInspectorData? InspectedTile,

    // Entities — flat lookup by EntityId; used by inspector and map renderer
    IReadOnlyDictionary<EntityId, EntitySnapshot> EntitySnapshots,

    // Settlements — keyed by tile coord; used by inspector and map renderer
    IReadOnlyDictionary<TileCoord, SettlementSnapshot> Settlements,

    // Ruins — keyed by tile coord; displayed in inspector and map renderer
    IReadOnlyDictionary<TileCoord, RuinRecord> Ruins,

    // Territory and improvements (M3 Phase 3.0)
    // TerritoryMap: tile → (owning city tile, civ id). Absent = unclaimed.
    IReadOnlyDictionary<TileCoord, TerritorySnapshot> TerritoryMap,
    // ImprovementMap: tile → improvement snapshot. Absent = no improvement.
    IReadOnlyDictionary<TileCoord, ImprovementSnapshot> ImprovementMap,

    // World-level drift parameters for UI status display
    float GlobalTemperatureAnomaly,
    float GlobalPrecipitationMultiplier,
    float StormCorridorNormalizedLat,

    // Character watch panel (M3 Phase 3.4) — null when no character is being watched
    CharacterWatchSnapshot? WatchedCharacter = null,

    // Save state (M3 Phase 3.6) — used by UI to show "Saving..." overlay
    bool IsSaving     = false,
    long LastSaveTick = -1
);
```

## SettlementStub
**File:** `WorldEngine.Sim/Civilizations/SettlementStub.cs:9`  
**Kind:** `sealed record`

```csharp
public sealed record SettlementStub(
    EntityId  FounderId,
    CivId     CivId,
    TileCoord Tile,
    int       FoundedYear,
    int       Population,              // integer head count
    int       Health,                  // 0–100; raids reduce it; 0 = destroyed
    string    Name                 = "Unknown",
    float     PopulationF          = 0f,   // fractional accumulator for growth
    int       LastCrystalThresh    = 0,    // population threshold already crystallized
    float     FoodPressureRatio    = 1f,   // convenience accessor; mirrors ResourceLedger["food"]
    float     WaterPressureRatio   = 1f,
    int       LastStrainEventTick  = 0,    // tick of last SettlementStraining event (rate-limit)
    IReadOnlyDictionary<string, float>? ResourceLedger = null, // extensible supply values per resource type
    float     FertilityMultiplier  = 1f,   // per-settlement founding-time variance; permanent
    int       ConqueredYear        = 0,    // year this settlement was last conquered (0 = never)
    int       ConqueredFromCivId   = 0,   // CivId of previous owner at time of conquest (0 = never)
    IReadOnlyDictionary<string, float>? ResourceStores = null,  // persistent resource reserves keyed by resource name
    int CarryingCapacity = 50_000,                              // food-ledger-derived population ceiling; computed by ResourcePressurePhase each tick (EMA-smoothed)
    float SmoothedCapacity = 50_000f,                          // EMA of raw food-derived capacity; used by logistic suppression to damp oscillation
    bool IsColony = false,                                       // true when founded beyond ColonyMinDistance from all same-civ settlements
    bool IsInfected = false,                                    // currently suffering a disease outbreak
    int  InfectedSinceYear = 0,                                 // year the current infection started
    float Unrest = 0f)                                          // S2: 0=content, 1=fully rebellious; drives secession
```

## SettlementSnapshot
**File:** `WorldEngine.Sim/World/WorldSnapshot.cs:27`  
**Kind:** `sealed record`

```csharp
public sealed record SettlementSnapshot(
    TileCoord Coord,
    string    Name,
    string    CivName,
    int       Population,
    int       Health,
    int       FoundedYear,
    IReadOnlyDictionary<string, float>? ResourceLedger = null,
    int       ConqueredYear      = 0,
    int       ConqueredFromCivId = 0,
    IReadOnlyDictionary<string, float>? ResourceStores = null);
```

## Civilization
**File:** `WorldEngine.Sim/Civilizations/Civilization.cs:9`  
**Kind:** `class`

```csharp
public sealed class Civilization
{
    public CivId      Id          { get; }
    public string     Name        { get; }
    public EntityId   FounderId   { get; }
    public EntityId   RulerId     { get; set; }
    public TileCoord  CapitalTile { get; set; }
    public int        FoundedYear { get; }
    public bool       IsCollapsed { get; set; }
    public int        CollapseYear { get; set; }
    public HashSet<EntityId> Members { get; } = [];
    public int LastSettlementFoundedYear { get; set; } = -999;
    public int SettlementCount { get; set; } = 0;
    public int ColonyCount { get; set; } = 0;
    public int TotalPopulation { get; set; } = 0;
    public int SuccessionCrisisEndYear { get; set; } = int.MinValue;
    public Dictionary<CivId, float> BorderTension { get; } = new();
    public Dictionary<CivId, int> WarsAgainst { get; } = [];
    public Dictionary<CivId, int> PeaceTreaties { get; } = [];
    public Dictionary<CivId, int> WarHistory { get; } = [];
    public Dictionary<TileCoord, HashSet<TileCoord>> CityTerritories { get; } = new();
    public int RulerCount { get; set; } = 1;
    public int TotalWarsInitiated { get; set; } = 0;
    public int TotalSuccessions { get; set; } = 0;
    public int TotalSettlementsFounded { get; set; } = 0;
    public int NearCollapseCount { get; set; } = 0;
    public int TotalScholarDiscoveries { get; set; } = 0;
    public HashSet<string> CulturalTraits { get; } = new(StringComparer.OrdinalIgnoreCase);
    public CulturalProfile? CulturalProfile { get; set; }
    public Dictionary<CivId, int> WarBattleWins { get; } = new();
    public Dictionary<CivId, CivContact> KnownCivs { get; } = new();
    public Dictionary<CivId, int> ActiveEmissaryCountByTarget { get; } = new();
}
```

## EntitySnapshot
**File:** `WorldEngine.Sim/Entities/EntitySnapshot.cs:9`  
**Kind:** `sealed record`

```csharp
public sealed record EntitySnapshot(
    EntityId Id,
    EntityKind Kind,
    string Name,
    string SpeciesId,        // matches beasts.toml id field for beasts; empty for characters
    bool IsLegendary,
    TileCoord Location,
    float HealthFraction,    // 0.0–1.0
    float FoodFraction,      // 0.0–1.0; -1 if entity has no Food need
    int AgeSeason,           // age in seasons
    bool IsAlive,
    string? CivName    = null,  // non-null for characters that belong to a civilization
    string AncestryId  = "",    // ancestry id from ancestries.toml; empty for non-character entities
    float  Wellbeing   = 0f    // -1 spiraling … +1 flourishing; 0 for non-character entities
);
```

## AncestryConfig
**File:** `WorldEngine.Sim/Config/AncestryConfig.cs:7`  
**Kind:** `class`

```csharp
public sealed class AncestryConfig
{
    public string   Id          { get; set; } = "human";
    public string   DisplayName { get; set; } = "Human";
    public int MinLifespanSeasons { get; set; } = 60;
    public int MaxLifespanSeasons { get; set; } = 200;
    public float BiasAmbition    { get; set; } = 0f;
    public float BiasGreed       { get; set; } = 0f;
    public float BiasAggression  { get; set; } = 0f;
    public float BiasCompassion  { get; set; } = 0f;
    public float BiasCuriosity   { get; set; } = 0f;
    public float BiasCreativity  { get; set; } = 0f;
    public float BiasRationality { get; set; } = 0f;
    public float BiasWonder      { get; set; } = 0f;
    public float BiasLoyalty     { get; set; } = 0f;
    public float BiasSociability { get; set; } = 0f;
    public float BiasHonesty     { get; set; } = 0f;
    public float BiasStability   { get; set; } = 0f;
    public float BiasDiligence     { get; set; } = 0f;
    public float BiasFocus         { get; set; } = 0f;
    public float BiasPerfectionism  { get; set; } = 0f;
    public float BiasComposure     { get; set; } = 0f;
    public float BiasAcuity        { get; set; } = 0f;
    public float BiasIngenuity     { get; set; } = 0f;
    public Dictionary<string, float> SpawnWeights { get; set; } = new();
    public Dictionary<string, float> FirstMeetingTrust { get; set; } = new();
    public Dictionary<string, float> CulturalDistance { get; set; } = new();
    public string[] FirstNames { get; set; } = [];
    public string[] Epithets   { get; set; } = [];
    public string   ArchitecturalStyle      { get; set; } = "";
    public string   SettlementDescriptor    { get; set; } = "";
    public string[] BiomeAdaptations        { get; set; } = [];
    public string[] ImprovementDescriptors  { get; set; } = [];
    public string[] ArtisticTraditions      { get; set; } = [];
    public string   CivNameSuffix           { get; set; } = "Domain";
    public string[] PhysicalTags { get; set; } = [];
}
```

## TerritorySnapshot
**File:** `WorldEngine.Sim/World/WorldSnapshot.cs:12`  
**Kind:** `sealed record`

```csharp
public sealed record TerritorySnapshot(
    TileCoord CityTile,
    long      CivId);
```

## ImprovementSnapshot
**File:** `WorldEngine.Sim/World/WorldSnapshot.cs:20`  
**Kind:** `sealed record`

```csharp
public sealed record ImprovementSnapshot(
    string    ImprovementType,
    TileCoord CityTile,
    int       BuiltYear,
    long      BuilderId);
```

## CharacterWatchSnapshot
**File:** `WorldEngine.Sim/World/WorldSnapshot.cs:49`  
**Kind:** `sealed record`

```csharp
public sealed record CharacterWatchSnapshot(
    EntityId   Id,
    string     Name,
    string     Epithet,
    string     CivName,
    TileCoord  Location,
    string     BiomeName,
    int        AgeSeasons,
    float      Wellbeing,
    NeedsVector   Needs,
    PersonalityVector Personality,
    IReadOnlyList<GoalWatchEntry> Goals);
```

<!-- content-hash: 4ff2e9f6be149634 -->
