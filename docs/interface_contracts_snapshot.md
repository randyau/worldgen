# Interface Contracts — Snapshot & World Structures
**Parent:** `interface_contracts.md` | **Version:** 0.7 | **Status:** M2 complete

Covers: TileDisplayData, EntitySnapshot, IdentityData, AncestryConfig, AncestryRegistry, TileInspectorData, WorldSnapshot, SettlementStub, SettlementSnapshot, RuinRecord, Civilization, ID wrappers.

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
    string? CivName    = null,  // M2+: set for Tier1Character with a valid CivId
    string AncestryId  = ""     // M2+: ancestry id from ancestries.toml; empty for non-character entities
);
```

---

## IdentityData

Immutable record on `Tier1Character`. All fields set at spawn; change only via record `with`.

```csharp
public sealed record IdentityData(
    string     Name,
    string     Epithet,
    string     AncestryId,   // key into AncestryRegistry / ancestries.toml
    EntityId?  MotherId,
    EntityId?  FatherId,
    CivId      CivId,        // CivId.None if no civ; check .IsValid before use
    int        BirthYear,
    int        BirthSeason);
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
    public static EntityId New() => new(Interlocked.Increment(ref _counter));
    private static long _counter;
    public bool IsValid => Value > 0;
}

public readonly record struct CivId(int Value)
{
    public bool IsValid => Value > 0;   // unset CivId is CivId(0), not null
}

public readonly record struct EventId(long Value)
{
    public bool IsValid => Value > 0;
}
```

`CivId.IsValid` was added in M2. Always check `.IsValid` before using a CivId from character identity data.

---

## WorldSnapshot

Immutable projection of world state for the UI. Created after each tick. **v0.6:** Added `Ruins`, `EntitySnapshots`, expanded `SettlementSnapshot`.

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

    // Entities — flat lookup by EntityId
    IReadOnlyDictionary<EntityId, EntitySnapshot> EntitySnapshots,

    // Settlements — keyed by tile coord
    IReadOnlyDictionary<TileCoord, SettlementSnapshot> Settlements,

    // Ruins — keyed by tile coord
    IReadOnlyDictionary<TileCoord, RuinRecord> Ruins,

    // World-level drift parameters for UI status display
    float GlobalTemperatureAnomaly,
    float GlobalPrecipitationMultiplier,
    float StormCorridorNormalizedLat
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
    int       Population,             // integer head count
    int       Health,                 // 0–100; raids reduce it; 0 = destroyed
    string    Name                = "Unknown",
    float     PopulationF         = 0f,         // fractional accumulator for growth
    int       LastCrystalThresh   = 0,          // highest population threshold already crystallized
    float     FoodPressureRatio   = 1f,
    float     WaterPressureRatio  = 1f,
    int       LastStrainEventTick = 0,
    IReadOnlyDictionary<string, float>? ResourceLedger   = null,
    float     FertilityMultiplier = 1f,         // founding-time variance; permanent
    int       ConqueredYear       = 0,          // 0 = never conquered
    int       ConqueredFromCivId  = 0,
    IReadOnlyDictionary<string, float>? ResourceStores   = null,
    int       CarryingCapacity    = 50_000);    // biome-based ceiling; recomputed each tick
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

## Civilization

Mutable class; only `CivTracker` mutates it. Read via `IWorldStateReadOnly.GetCivilization(civId)`.

```csharp
public sealed class Civilization
{
    public CivId     Id              { get; }
    public string    Name            { get; }
    public EntityId  FounderId       { get; }
    public TileCoord CapitalTile     { get; }
    public int       FoundedYear     { get; }
    public HashSet<EntityId> Members { get; }

    public Dictionary<CivId, int> WarsAgainst  { get; }   // enemyCivId → year declared
    public Dictionary<CivId, int> PeaceTreaties { get; }  // enemyCivId → year peace made

    public int  LastSettlementFoundedYear { get; set; }
    public bool IsCollapsed { get; set; }

    public bool IsAtWarWith(CivId other) => WarsAgainst.ContainsKey(other);
    public bool InPeaceCooldownWith(CivId other, int currentYear, int cooldownYears);
}
```

**War/peace lifecycle:**
1. Character emits `DeclareWar` command
2. `CivTracker.ResolveDeclareWar` records in `WarsAgainst` on both sides
3. `CivTracker.RunAnnualDiplomacy` (Spring tick) expires wars via truce / surrender / destruction
4. `EndWarBetween` removes from `WarsAgainst`, writes `PeaceTreaties[enemy] = currentYear`
5. `InPeaceCooldownWith` blocks re-declaration for `PeaceCooldownYears` after a treaty
