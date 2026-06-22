# World Engine — Interface Contracts
**Version:** 0.2  
**Date:** June 2026  
**Status:** Updated after design sessions A–D. All changes from sessions are incorporated.  
**Changes from v0.1:** TileData exact layout locked (14 bytes, Pack=1). TileStaticFlags promoted to ushort. TileDynFlags revised (HasActiveDisaster only). TileDisplayData updated (Fertility added, effective values clarified, HasActiveDisaster computed from registry). WorldSnapshot extended (InspectedTile, drift parameters). TileInspectorData added. ResourceDeposit, ActiveDisaster, ActiveDrought, DisasterType, SeasonalProfile, PendingEvent, ChunkSummaryFlags all added.

**Rule:** Do not add methods to these interfaces without updating this document first. Interface changes are breaking changes.

---

## TileData

The core tile struct. 14 bytes, Pack=1. All integer fields, no floats.

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TileData  // exactly 14 bytes — assert at startup
{
    // Static — set at world gen, never mutated during sim
    public byte Elevation;          // 0-255, scaled
    public byte Fertility;          // 0-255, scaled
    public byte BaseTemperature;    // 0-255, scaled (genesis climate)
    public byte BaseMoisture;       // 0-255, scaled (genesis climate)
    public byte MagicIntensity;     // 0-255, scaled
    public byte BiomeType;          // cast to BiomeType enum
    public byte PlateId;            // 0-255 tectonic plate assignment
    public TileStaticFlags StaticFlags;   // ushort, 16 bits

    // Dynamic — mutated during sim
    public byte CurrentMoisture;    // 0-255, updated each seasonal tick
    public TileDynFlags DynFlags;   // byte, 8 bits
    public byte RoadLevel;          // 0=none; populated in M2+
    public ushort CivControl;       // 0=unclaimed; populated in M2+
}
// Startup assertion: Debug.Assert(Marshal.SizeOf<TileData>() == 14);
```

---

## TileStaticFlags

Set at world gen. Immutable during simulation (exception: `IsCoastal` may flip on sea level change).

```csharp
[Flags]
public enum TileStaticFlags : ushort   // 16 bits — room to grow
{
    None             = 0,
    IsVolcanic       = 1 << 0,  // volcanic zone near subduction boundary
    IsFaultLine      = 1 << 1,  // plate boundary tile
    HasDeposit       = 1 << 2,  // mineral deposit present → ResourceRegistry
    HasRareResource  = 1 << 3,  // rare/magical resource → ResourceRegistry
    IsCoastal        = 1 << 4,  // land tile adjacent to ocean
    HasRiver         = 1 << 5,  // river flows through this tile
    IsLake           = 1 << 6,  // inland lake
    IsPOICandidate   = 1 << 7,  // high-interest confluence (magic/river/resource)
    IsStormCorridor  = 1 << 8,  // within the storm track latitude band at genesis
    // bits 9–15: reserved for M2+
}
```

---

## TileDynFlags

Updated each tick. Disasters are tracked in `ActiveTileDisasters` registry, not as individual flags.

```csharp
[Flags]
public enum TileDynFlags : byte   // 8 bits
{
    None             = 0,
    HasActiveDisaster = 1 << 0,  // presence indicator → ActiveTileDisasters[coord]
    // bits 1–7: reserved for M2+
    // Candidates: HasStructure, IsContested, IsUnderSiege, IsOnTradeRoute
}
```

---

## ChunkSummaryFlags

Per-chunk summary for disaster phase skip optimisation. Set during TileGrid assembly; updated when tile biomes or disaster state change.

```csharp
[Flags]
public enum ChunkSummaryFlags : byte
{
    None              = 0,
    HasVolcanicTile   = 1 << 0,
    HasFaultLineTile  = 1 << 1,
    HasForestTile     = 1 << 2,
    HasRiverTile      = 1 << 3,
    HasActiveDisaster = 1 << 4,
    // bits 5–7: reserved
}
```

---

## SeasonalProfile

Per-tile seasonal climate variation. Stored in `WorldState.SeasonalProfiles[]` parallel to TileGrid. Populated during 1.3.8. Never changes after world gen. 8 bytes per tile.

```csharp
public struct SeasonalProfile  // 8 bytes
{
    public sbyte TempDeltaSpring, TempDeltaSummer, TempDeltaAutumn, TempDeltaWinter;
    public sbyte MoistureDeltaSpring, MoistureDeltaSummer, MoistureDeltaAutumn, MoistureDeltaWinter;
}

// Effective temperature formula (computed at runtime, not stored):
// effectiveTemp = BaseTemperature
//               + SeasonalProfiles[idx].TempDelta[CurrentSeason]
//               + (GlobalTemperatureAnomaly × latitudeScale)
//
// Effective moisture formula:
// effectiveMoisture = (BaseMoisture + SeasonalProfiles[idx].MoistureDelta[CurrentSeason])
//                   × GlobalPrecipitationMultiplier
//                   × (IsStormCorridor ? stormBonus : 1.0f)
//                   × (IsMonsoonTile ? MonsoonIntensityMultiplier : 1.0f)
```

---

## ResourceDeposit

Stored in `WorldState.ResourceRegistry : Dictionary<TileCoord, List<ResourceDeposit>>`. The `HasDeposit` / `HasRareResource` StaticFlags are presence indicators only — always look up the registry for type and quality.

```csharp
/// <summary>
/// A mineral or resource deposit at a tile. Multiple deposits can stack at one
/// location (e.g., quarry slate over a placer gold seam). List ordered by depth
/// (surface first).
/// </summary>
public sealed record ResourceDeposit(
    string DepositType,   // open string — "Iron", "Copper", "Tin", "Gold", "Slate", etc.
    byte Quality,         // 0-255
    byte Depth            // 0=surface, 255=deep
);
```

---

## DisasterType

```csharp
public enum DisasterType
{
    Wildfire      = 0,
    Flood         = 1,
    VolcanicAsh   = 2,    // lingering ash deposit after eruption (TicksRemaining=-1 until cleared)
    SeismicDamage = 3,    // terrain deformation after earthquake
    // V2: Plague, Blight, ArmyPresence (not a disaster but same tracking pattern)
}
```

---

## ActiveDisaster

Stored in `WorldState.ActiveTileDisasters : Dictionary<TileCoord, List<ActiveDisaster>>`. Multiple entries per tile are valid (co-morbid disasters).

```csharp
/// <summary>
/// An ongoing disaster affecting a specific tile.
/// Created by Phase 1 (Environmental). Cleared by Phase 1 when resolved.
/// CauseEventId links to the SimEvent that started this disaster for causal graph.
/// </summary>
public sealed record ActiveDisaster(
    DisasterType Type,
    float Intensity,       // 0.0–1.0 severity
    int TicksRemaining,    // -1 = indefinite (ash deposits, etc.), else countdown
    EventId OriginEventId  // SimEvent.Id of the event that created this disaster
);
```

---

## ActiveDrought

Regional disaster. Not stored per-tile in `ActiveTileDisasters`. Stored in `WorldState.ActiveDroughts : List<ActiveDrought>`.

```csharp
/// <summary>
/// A drought affecting all tiles in a (LatitudeBand, Biome) region.
/// Membership is computed at runtime: ActiveDroughts.Any(d => tile matches d).
/// No per-tile registry entry — the region can contain thousands of tiles.
/// </summary>
public sealed record ActiveDrought(
    int LatitudeBandIndex,
    BiomeType AffectedBiome,
    float Intensity,          // 0.0–1.0
    int SeasonsRemaining,
    EventId OriginEventId
);
```

---

## PendingEvent

Produced by Phase 1 (Environmental). Consumed by Phase 7 (EventGeneration) which enriches into a full `SimEvent`, applies the gate, and writes to the database.

```csharp
/// <summary>
/// Lightweight event record produced by Phase 1.
/// Phase 7 assigns Id, Year, Season, Tick, runs significance classification,
/// applies EventGate, and writes to SQLite + EventCache.
/// </summary>
public sealed record PendingEvent(
    EventType Type,
    TileCoord? Location,
    EventId? CauseEventId,   // null = root event; set = CausalEdge will be created
    string PayloadJson
);
```

---

## IEntity

Unchanged from v0.1. Included for completeness.

```csharp
/// <summary>
/// The core simulation entity interface. Every simulated object implements this.
/// Entities NEVER mutate world state directly. They emit ICommand instances
/// during the EMIT step which are resolved by CommandResolver in the RESOLVE step.
/// </summary>
public interface IEntity
{
    EntityId Id { get; }
    TileCoord Location { get; }
    EntityKind Kind { get; }

    /// <summary>
    /// Emit commands for this tick phase. Must not have side effects.
    /// </summary>
    IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase);

    EntityDisplayData ToDisplayData();
}
```

---

## ICommand

Unchanged from v0.1.

```csharp
/// <summary>
/// Marker interface for simulation commands.
/// All implementations must be sealed records with value-type fields only.
/// No callbacks, delegates, or mutable object references.
/// </summary>
public interface ICommand { }
```

---

## IWorldStateReadOnly

The read-only view passed to entity decision-making in M2+. In M1, tile-level randomness uses `WorldRng` directly (no EntityId available). The entity-focused methods are stubs until M2.

```csharp
/// <summary>
/// Read-only view of world state for entity decision-making (M2+).
/// In M1, the Environmental phase reads WorldState directly as a mutator.
/// </summary>
public interface IWorldStateReadOnly
{
    // === TIME ===
    int CurrentYear { get; }
    Season CurrentSeason { get; }
    long CurrentTick { get; }

    // === TILE ACCESS ===
    /// <summary>Get tile data. Applies East-West cylinder wrapping.</summary>
    TileData GetTile(TileCoord coord);
    bool IsLand(TileCoord coord);
    IEnumerable<TileCoord> GetTilesInRadius(TileCoord center, int radius);

    // === WORLD CONFIG ===
    WorldConfig Config { get; }

    // === DETERMINISTIC RNG (for entity decisions in M2+) ===
    /// <summary>
    /// Deterministic random value for a specific entity this tick.
    /// Internally uses WorldRng.FloatAt(worldSeed, tick, entityId.Value, 0, salt).
    /// Do NOT use System.Random in entity logic.
    /// </summary>
    float GetRandomFloat(EntityId entityId, int salt = 0);
    int GetRandomInt(EntityId entityId, int min, int max, int salt = 0);

    // === DRIFT PARAMETERS (readable by entity decision logic) ===
    float GlobalTemperatureAnomaly { get; }
    float CurrentSeaLevel { get; }

    // Milestone 2+ additions (uncomment when implementing):
    // IEntity? GetEntity(EntityId id);
    // IEnumerable<IEntity> GetEntitiesAt(TileCoord coord);
    // IEnumerable<IEntity> GetEntitiesInRadius(TileCoord center, int radius);
    // float GetRelationshipTrust(EntityId from, EntityId to);
    // IEnumerable<SimEvent> GetRecentEvents(int withinYears);
    // float GetAuthorityAt(TileCoord coord, CivId civId);
}
```

---

## IWorldGenLayer

Unchanged from v0.1.

```csharp
/// <summary>
/// World generation layer interface. Stateless — all state in WorldGenContext.
/// </summary>
public interface IWorldGenLayer<TResult>
{
    TResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default);
}
```

---

## StateCache

Unchanged from v0.1.

```csharp
/// <summary>
/// Thread-safe snapshot bridge. Sim thread calls Commit() after each tick.
/// UI thread calls Read() every frame. Lock held for microseconds only.
/// </summary>
public sealed class StateCache
{
    public void Commit(WorldSnapshot snapshot);
    public WorldSnapshot? Read();   // null until first Commit
}
```

---

## TileDisplayData

**Updated from v0.1:** Added `Fertility`. `Temperature` renamed to `EffectiveTemperature` (computed, not base). `Moisture` renamed to `CurrentMoisture` (dynamic field). `HasActiveDisaster` retained but is now computed from `ActiveTileDisasters.ContainsKey(coord)` by the sim thread — not read from TileDynFlags directly.

```csharp
/// <summary>
/// Per-tile rendering data in WorldSnapshot.VisibleTiles.
/// Contains effective (current) values, not genesis base values.
/// Created by the sim thread for all tiles in the current viewport.
/// HasActiveDisaster is computed from ActiveTileDisasters registry.
/// </summary>
public sealed record TileDisplayData(
    BiomeType Biome,
    byte Elevation,
    byte EffectiveTemperature,  // BaseTemp + seasonal delta + GlobalTemperatureAnomaly
    byte CurrentMoisture,       // dynamic moisture, updated each tick
    byte MagicIntensity,
    byte Fertility,
    TileStaticFlags StaticFlags,
    TileDynFlags DynFlags,
    bool HasActiveDisaster       // computed: ActiveTileDisasters.ContainsKey(coord)
);
```

---

## TileInspectorData

**New in v0.2.** Full tile detail for the UI inspector panel. Created by the sim thread when `SetInspectedTile` command is received. Included in `WorldSnapshot.InspectedTile`.

```csharp
/// <summary>
/// Complete tile data for the inspector panel. Created by sim thread on demand.
/// Contains base values, seasonal profiles, and all registry data for the tile.
/// </summary>
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

## WorldSnapshot

**Updated from v0.1:** Added `InspectedTile`, `GlobalTemperatureAnomaly`, `GlobalPrecipitationMultiplier`, `StormCorridorNormalizedLat`. `VisibleTiles` now typed to updated `TileDisplayData`.

```csharp
/// <summary>
/// Immutable projection of world state for the UI. Created after each tick.
/// UI thread reads this every frame — never touches WorldState directly.
/// </summary>
public sealed record WorldSnapshot(
    // Time
    int CurrentYear,
    Season CurrentSeason,
    SimSpeed CurrentSpeed,
    bool IsPaused,
    long TicksPerSecond,

    // Map
    IReadOnlyDictionary<TileCoord, TileDisplayData> VisibleTiles,
    OverlayType ActiveOverlay,
    int WorldTileWidth,
    int WorldTileHeight,

    // Event log
    IReadOnlyList<SimEvent> RecentEvents,

    // Tile inspector (null if no tile selected)
    TileInspectorData? InspectedTile,

    // World-level drift parameters for UI status display
    float GlobalTemperatureAnomaly,
    float GlobalPrecipitationMultiplier,
    float StormCorridorNormalizedLat
);
```

---

## SimEvent

Unchanged from v0.1.

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

Unchanged from v0.1.

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
```

---

## Strongly-Typed ID Wrappers

Unchanged from v0.1.

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

---

*Document Version: 0.2*  
*Last Updated: June 2026*
