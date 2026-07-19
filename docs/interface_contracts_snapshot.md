<!-- contract-snapshot-hash: 79990b7c923aa96a -->
# Interface Contracts — Snapshot & World Structures
**Parent:** `interface_contracts.md` | **Version:** 0.9 | **Status:** M3 complete

Covers: TileDisplayData, EntitySnapshot, IdentityData, AncestryConfig, AncestryRegistry, TileInspectorData, WorldSnapshot, SettlementStub, SettlementSnapshot, RuinRecord, TerritorySnapshot, ImprovementSnapshot, CharacterWatchSnapshot, Civilization, ID wrappers.

---

## TileDisplayData

Per-tile rendering data in `WorldSnapshot.AllTiles`. Contains effective (current) values, not genesis base values. Flat array indexed by `(y * WorldTileWidth + x)`.

**v0.6:** Added `HasRuin` — computed from `WorldState.Ruins.ContainsKey(coord)`.

```csharp
public sealed record TileDisplayData(
    BiomeType Biome,
    byte Elevation,
    byte EffectiveTemperature,  // BaseTemp + seasonal delta + GlobalTemperatureAnomaly
    byte CurrentMoisture,       // dynamic moisture, updated each tick
    byte MagicIntensity,
    byte Fertility,
    TileStaticFlags StaticFlags,
    TileDynFlags DynFlags,
    bool HasActiveDisaster,      // computed: ActiveTileDisasters.ContainsKey(coord)
    bool HasRuin,                // computed: Ruins.ContainsKey(coord)
    EntityId[] EntitiesPresent   // empty array if none — never null
);
```

---

## EntitySnapshot

Flat, immutable summary of one entity for `WorldSnapshot`. Produced by `IEntity.ToSnapshot()`.

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

---

## IdentityData

Immutable record on `Tier1Character`. All fields set at spawn; change only via record `with`.

```csharp
public sealed record IdentityData(
    string     Name,
    string     Epithet,
    string     AncestryId,    // key into AncestryRegistry / ancestries.toml
    EntityId?  MotherId,
    EntityId?  FatherId,
    CivId      CivId,         // CivId(0) if no civ; check .IsValid before use
    int        BirthYear,
    int        BirthSeason,
    int        NameOrdinal  = 0,   // 0 = first bearer of this name; 1 = II, 2 = III, etc.
    int        RulerOrdinal = 0);  // Nth ruler of their civ (0 = founder / not yet a ruler)
```

---

## AncestryConfig

Per-ancestry data loaded from `config/ancestries.toml`. Accessed via `SimConfig.AncestryRegistry`.

```csharp
public sealed class AncestryConfig
{
    public string Id          { get; set; }  // "human", "elf", "dwarf", "dark_elf", "orc", "halfling"
    public string DisplayName { get; set; }

    public int MinLifespanSeasons { get; set; }  // inclusive lower bound
    public int MaxLifespanSeasons { get; set; }  // exclusive upper bound

    // Personality biases (+0.2 = mean shifts from 0.5 → 0.7; individual stddev ≈ 0.2 ≥ max bias)
    public float BiasAmbition, BiasGreed, BiasAggression, BiasCompassion, BiasCuriosity,
                 BiasCreativity, BiasRationality, BiasWonder, BiasLoyalty, BiasSociability,
                 BiasHonesty, BiasStability;

    // Aptitude biases — same additive pattern, clamped to [0.1, 0.9]
    public float BiasDiligence, BiasFocus, BiasPerfectionism, BiasComposure, BiasAcuity, BiasIngenuity;

    // Biome-weighted spawn probability — keys are snake_case BiomeType names
    public Dictionary<string, float> SpawnWeights   { get; set; }
    // One-time trust modifier on first interaction with this ancestry
    public Dictionary<string, float> FirstMeetingTrust { get; set; }
    // Cultural distance (0–1) driving passive per-tick trust drain
    public Dictionary<string, float> CulturalDistance  { get; set; }

    public string[] FirstNames { get; set; }  // ancestry-specific name pool
    public string[] Epithets   { get; set; }
}
```

**Trust drain formula (per tick, cross-civ chars sharing a tile):**
```
trust -= CulturalDistance[otherAncestryId] × CulturalDistanceDrainRate
trust -= |stabilityA - stabilityB| × PersonalityMismatchDrainRate
```

First-meeting modifier applied once (when `RelationshipGraph.Get(a,b) == null` before `GetOrCreate`):
```
trust += (FirstMeetingTrust[otherAncestryId] + other.FirstMeetingTrust[myAncestryId]) / 2
```

---

## AncestryRegistry

Loaded by `AncestryLoader.LoadOrDefault()`, stored on `SimConfig.AncestryRegistry`.

```csharp
public sealed class AncestryRegistry
{
    public AncestryConfig? Get(string id);
    public AncestryConfig GetOrHuman(string id);    // fallback to human default
    public IReadOnlyCollection<AncestryConfig> All { get; }

    // Biome-weighted ancestry sampling — used by CharacterFactory.Spawn()
    public string SampleAncestry(BiomeType biome, int worldSeed, long seq, int salt);

    public float GetFirstMeetingTrust(string idA, string idB);
    public float GetCulturalDistance(string idA, string idB);  // symmetric fallback

    public static readonly AncestryRegistry Empty;
}
```

---

## TileInspectorData

Full tile detail for the UI inspector panel. Created by the sim thread when `SetInspectedTile` command is received.

```csharp
public sealed record TileInspectorData(
    TileCoord Coord,
    TileData RawTile,                              // full 14-byte struct (base/genesis values)
    SeasonalProfile SeasonalProfile,               // all four season deltas
    float EffectiveTemperature,                    // float precision for display
    float CurrentMoistureF,                        // float precision for display
    IReadOnlyList<ResourceDeposit> Deposits,       // from ResourceRegistry
    IReadOnlyList<ActiveDisaster> Disasters,       // from ActiveTileDisasters
    bool IsInActiveDrought,                        // computed from ActiveDroughts list
    EventId? DroughtOriginEventId                  // set if IsInActiveDrought
);
```

---

## Strongly-Typed ID Wrappers

All entity/civ/event IDs use readonly record structs, never raw ints or longs.

```csharp
public readonly record struct EntityId(long Value)
{
    public static EntityId New() => new(IdGenerator.Next());  // thread-safe global counter
    public static void EnsureCounterExceeds(long minValue);  // call after loading saves
}

public readonly record struct CivId(int Value)
{
    public static readonly CivId None = new(0);
    public bool IsValid => Value > 0;   // unset CivId is CivId(0) / CivId.None, not null
}

public readonly record struct EventId(long Value);

public readonly record struct ModifierId(Guid Value)
{
    public static ModifierId New() => new(Guid.NewGuid());
}

public readonly record struct ArtifactId(long Value)
{
    public static ArtifactId New() => new(IdGenerator.Next());
}
```

Always check `CivId.IsValid` before using a CivId from character identity data — the unset value is `CivId.None` (Value=0).

---

## WorldSnapshot

Immutable projection of world state for the UI. Created after each tick.

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

    // Save state — used by UI to show "Saving..." overlay
    bool IsSaving     = false,
    long LastSaveTick = -1
);
```

---

## SettlementStub

Live sim-thread settlement state. Lives in `WorldState.Settlements`; updated each tick by `ResourcePressurePhase` and `PopulationDynamicsPhase`. Always replaced via record `with` — never mutated directly.

```csharp
public sealed record SettlementStub(
    EntityId  FounderId,
    CivId     CivId,
    TileCoord Tile,
    int       FoundedYear,
    int       Population,              // integer head count
    int       Health,                  // 0–100; raids reduce it; 0 = destroyed
    string    Name                 = "Unknown",
    float     PopulationF          = 0f,         // fractional accumulator for growth
    int       LastCrystalThresh    = 0,          // population threshold already crystallized
    float     FoodPressureRatio    = 1f,         // convenience accessor; mirrors ResourceLedger["food"]
    float     WaterPressureRatio   = 1f,
    int       LastStrainEventTick  = 0,          // tick of last SettlementStraining event (rate-limit)
    IReadOnlyDictionary<string, float>? ResourceLedger  = null,
    float     FertilityMultiplier  = 1f,         // per-settlement founding-time variance; permanent
    int       ConqueredYear        = 0,          // year this settlement was last conquered (0 = never)
    int       ConqueredFromCivId   = 0,          // CivId of previous owner at time of conquest (0 = never)
    IReadOnlyDictionary<string, float>? ResourceStores  = null,
    int       CarryingCapacity     = 50_000,     // food-ledger-derived population ceiling; recomputed each tick (EMA-smoothed)
    float     SmoothedCapacity     = 50_000f,    // EMA of raw food-derived capacity; damps logistic oscillation
    bool      IsColony             = false,      // true when founded beyond ColonyMinDistance from all same-civ settlements
    bool      IsInfected           = false,      // currently suffering a disease outbreak
    int       InfectedSinceYear    = 0,          // year the current infection started
    float     Unrest               = 0f);        // 0=content, 1=fully rebellious; drives secession
```

**ResourceLedger keys:** `"food"`, `"water"`, `"timber"`, lowercase deposit type names  
**Food/water:** supply/demand ratio (1.0=met, >1=surplus, <1=shortage); minerals: absolute units  
**ReachRadius():** `Math.Clamp(2 + Population / 2000, 2, 5)` — shared by `ResourcePressurePhase` and `UtilityScorer`

---

## SettlementSnapshot

UI-facing companion to `SettlementStub`. Lives in `WorldSnapshot.Settlements`.

```csharp
public sealed record SettlementSnapshot(
    TileCoord Coord,
    string    Name,
    string    CivName,
    int       Population,
    int       Health,              // 0–100
    int       FoundedYear,
    IReadOnlyDictionary<string, float>? ResourceLedger   = null,
    int       ConqueredYear      = 0,
    int       ConqueredFromCivId = 0,
    IReadOnlyDictionary<string, float>? ResourceStores   = null);
```

---

## RuinRecord

Persists when a settlement is destroyed or abandoned. `TimesSettled` increments if the tile is resettled and destroyed again.

```csharp
public sealed record RuinRecord(
    TileCoord Tile,
    string    SettlementName,
    CivId     OriginalCivId,
    int       DestroyedYear,
    string    Cause,          // "destroyed" | "abandoned"
    int       TimesSettled    // 1 = first time this tile has been ruined
);
```

---

## TerritorySnapshot

Per-tile territory entry in `WorldSnapshot.TerritoryMap`. Absent = tile is unclaimed.

```csharp
public sealed record TerritorySnapshot(
    TileCoord CityTile,  // which city owns this tile
    long      CivId);    // which civ that city belongs to
```

---

## ImprovementSnapshot

Per-tile improvement entry in `WorldSnapshot.ImprovementMap`. Absent = no improvement on tile.

```csharp
public sealed record ImprovementSnapshot(
    string    ImprovementType,  // e.g. "Farm", "Mine", "LoggingCamp"
    TileCoord CityTile,         // city this improvement belongs to
    int       BuiltYear,
    long      BuilderId);       // EntityId of the character who built it
```

---

## CharacterWatchSnapshot

Live snapshot of a watched character for the character watch panel. Populated by `SnapshotBuilder` when `WatchedCharacterId` is set. Available in `WorldSnapshot.WatchedCharacter` (null when no character is watched).

```csharp
public sealed record CharacterWatchSnapshot(
    EntityId          Id,
    string            Name,
    string            Epithet,
    string            CivName,
    TileCoord         Location,
    string            BiomeName,
    int               AgeSeasons,
    float             Wellbeing,
    NeedsVector       Needs,
    PersonalityVector Personality,
    IReadOnlyList<GoalWatchEntry> Goals);

public sealed record GoalWatchEntry(string Description, float Priority);
```

---

## Civilization

Mutable class; only `CivTracker` mutates it. Read via `IWorldStateReadOnly.GetCivilization(civId)`.

```csharp
public sealed class Civilization
{
    // Identity
    public CivId      Id          { get; }
    public string     Name        { get; }
    public EntityId   FounderId   { get; }
    /// <summary>Current ruling character. Starts as FounderId; updated by succession.</summary>
    public EntityId   RulerId     { get; set; }
    public TileCoord  CapitalTile { get; set; }
    public int        FoundedYear { get; }
    public bool       IsCollapsed { get; set; }
    public int        CollapseYear { get; set; }

    // Members
    public HashSet<EntityId> Members { get; }

    // Settlement accounting
    public int LastSettlementFoundedYear { get; set; }
    public int SettlementCount  { get; set; }  // live local settlements
    public int ColonyCount      { get; set; }  // live colony settlements
    public int TotalPopulation  { get; set; }  // refreshed by PopulationDynamicsPhase

    // Succession
    public int RulerCount           { get; set; }  // total rulers (founder = 1)
    public int SuccessionCrisisEndYear { get; set; }  // int.MinValue = no active crisis

    // Territory
    /// <summary>Key = city tile; Value = all tiles that city owns (including itself).</summary>
    public Dictionary<TileCoord, HashSet<TileCoord>> CityTerritories { get; }

    // Diplomacy
    public Dictionary<CivId, float> BorderTension  { get; }  // accumulates annually near borders
    public Dictionary<CivId, int>   WarsAgainst    { get; }  // enemyCivId → year declared
    public Dictionary<CivId, int>   PeaceTreaties  { get; }  // enemyCivId → year peace made
    public Dictionary<CivId, int>   WarHistory     { get; }  // total wars ever declared per enemy
    public bool IsAtWarWith(CivId other) => WarsAgainst.ContainsKey(other);
    public bool InPeaceCooldownWith(CivId other, int currentYear, int cooldownYears, int warExhaustionPerWar = 0);

    // War campaign tracking
    public Dictionary<CivId, int> WarBattleWins { get; }  // wins this war; consumed by EndWarBetween

    // Cultural traits
    public int  TotalWarsInitiated      { get; set; }
    public int  TotalSuccessions        { get; set; }
    public int  TotalSettlementsFounded { get; set; }
    public int  TotalScholarDiscoveries { get; set; }
    public int  NearCollapseCount       { get; set; }
    public HashSet<string> CulturalTraits { get; }
    public CulturalProfile? CulturalProfile { get; set; }  // null until civ is fully initialized

    // M4.1 awareness / emissary system
    public Dictionary<CivId, CivContact> KnownCivs { get; }
    public Dictionary<CivId, int> ActiveEmissaryCountByTarget { get; }
}
```

**War/peace lifecycle:**
1. Character emits `DeclareWar` command
2. `CivTracker.ResolveDeclareWar` records in `WarsAgainst` on both sides
3. `CivTracker.RunAnnualDiplomacy` (Spring tick) expires wars via truce / surrender / destruction
4. `EndWarBetween` removes from `WarsAgainst`, writes `PeaceTreaties[enemy] = currentYear`
5. `InPeaceCooldownWith` blocks re-declaration; cooldown scales with `WarHistory` count × `warExhaustionPerWar`
