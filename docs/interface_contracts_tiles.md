# Interface Contracts — Tile Structures
**Parent:** `interface_contracts.md` | **Version:** 0.7 | **Status:** M2 complete

Covers: TileData, flag enums, SeasonalProfile, ResourceDeposit, disaster types.

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
    None              = 0,
    HasActiveDisaster = 1 << 0,  // presence indicator → ActiveTileDisasters[coord]
    RecentlyBurned    = 1 << 1,  // set when a wildfire expires on a forest tile; cleared after post-fire fertility boost applied in RunAnnualResourceDynamics
    // bits 2–7: reserved for M2+
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
