# World Engine — Design Session Decisions
**Date:** June 2026  
**Status:** Five design sessions complete. Sessions A–D are M1 pre-implementation decisions. Session E covers post-M2 character behavior and ancestry (June 2026).  
**Companion:** `implementation_decisions_v0.3.md` (architecture), `interface_contracts.md` (updated contracts)

Read this document before implementing any character-related story. Every decision here has downstream implications.

---

## Design Session A — TileData Layout

### A1: PlateId stored in TileData
**Decision:** `byte PlateId` is a permanent TileData field, not discarded after generation.  
**Rationale:** Costs 120KB at Europe scale (negligible). Enables a tectonic plates map overlay at zero additional cost. Useful for debugging world gen and future geological events.

### A2: Border manifests as sidecar file
**Decision:** Border manifests are computed at world gen time (story 1.3.8) and written to `manifests.bin`. They are never loaded into runtime memory until Milestone 4 (local scale generation).  
**Rationale:** 64 samples × 4 edges × ~5 bytes = ~1,280 bytes per tile × 120k tiles ≈ 150MB sitting idle in M1-M3. Sidecar file costs nothing at runtime. Local gen doesn't exist until M4.  
**Implication:** `BorderManifestStore` has `WriteToFile()` (used in 1.3.8) and `LoadFromFile()` (stub until M4).

### A3: CivControl included as placeholder now
**Decision:** `ushort CivControl` is in TileData from day one, defaulting to 0 (unclaimed).  
**Rationale:** Locking in the 14-byte struct now means M2 just starts populating a field that already exists. Avoids a state.bin migration between milestones.

### A4a: Resource deposits as sparse registry
**Decision:** `HasDeposit` and `HasRareResource` are StaticFlags bits (presence indicators). Deposit types, quality, and stacking live in `ResourceRegistry : Dictionary<TileCoord, List<ResourceDeposit>>` in WorldState.  
**Rationale:** The mineral/resource list is unbounded. Multiple deposits can stack at one location (e.g., slate quarry over a placer gold deposit). Bit fields cannot represent this. Same pattern as the `HasDeposit` flag → registry lookup.

### A4b: IsCoastal kept as StaticFlag
**Decision:** `IsCoastal` remains a StaticFlag. Updated by sea level changes when tiles transition between land and ocean.

### A4c: HasRiver in TileData, crossing details in manifests
**Decision:** `HasRiver` is a StaticFlag. River edge crossing data (position, width, flow volume) lives in `manifests.bin`.  
**Rationale:** At sim time, the only river question is "does a river flow through this tile?" Crossing details are only needed by local scale generation (M4).

### A5: Disasters as registry, not flags
**Decision:** Individual disaster flags (`IsOnFire`, `IsFlooded`, `IsDroughtAffected`) are removed from DynFlags entirely. Replaced by:
- `HasActiveDisaster` (single DynFlag bit) — presence indicator → `ActiveTileDisasters[coord]`
- `ActiveTileDisasters : Dictionary<TileCoord, List<ActiveDisaster>>` in WorldState — per-tile per-type records
- `ActiveDroughts : List<ActiveDrought>` in WorldState — regional disaster model (not per-tile)

**Rationale:** Multiple disasters can co-occur on one tile (drought + wildfire, flood + volcanic ash). Flags cannot represent co-occurrence, severity, or duration. Same "look it up" pattern as ResourceRegistry.  
**Result:** DynFlags has 1 bit used (`HasActiveDisaster`), 7 spare.

### A5b: Bitfield extensibility policy
**Decision:** Fields widen, never proliferate. When a flags field approaches capacity: `byte → ushort → uint`. Never add a `StaticFlags2` field. Promotion happens at milestone boundaries (not mid-implementation) because it changes the state.bin serialization format.  
**Current state:** `TileStaticFlags : ushort` (9 bits used, 7 spare). `TileDynFlags : byte` (1 bit used, 7 spare).

### Final TileData layout (14 bytes, Pack=1)
```
Static (set at world gen, immutable):
  Elevation         byte   0-255
  Fertility         byte   0-255
  BaseTemperature   byte   0-255
  BaseMoisture      byte   0-255
  MagicIntensity    byte   0-255
  BiomeType         byte   (BiomeType enum)
  PlateId           byte   0-255
  StaticFlags       ushort 16 bits (9 used, 7 spare — see below)

Dynamic (mutated during sim):
  CurrentMoisture   byte   0-255
  DynFlags          byte   8 bits (1 used, 7 spare — see below)
  RoadLevel         byte   0=none (M2+)
  CivControl        ushort 0=unclaimed (M2+)
  ─────────────────────────
  Total:            14 bytes (Pack=1)
```

```
TileStaticFlags (ushort):
  bit 0: IsVolcanic
  bit 1: IsFaultLine
  bit 2: HasDeposit         (→ ResourceRegistry)
  bit 3: HasRareResource    (→ ResourceRegistry)
  bit 4: IsCoastal
  bit 5: HasRiver
  bit 6: IsLake
  bit 7: IsPOICandidate
  bit 8: IsStormCorridor    (see DS-B)
  bits 9-15: reserved

TileDynFlags (byte):
  bit 0: HasActiveDisaster  (→ ActiveTileDisasters)
  bits 1-7: reserved (M2+ candidates: HasStructure, IsContested, IsUnderSiege)
```

---

## Design Session B — World Generation Algorithms

### B1: Tectonic plates via explicit Voronoi
**Decision:** N random plate center points generated from `worldSeed + LayerSeeds.Tectonic`. Each tile assigned to nearest center (cylinder-aware distance). Plate count is explicit config parameter.  
**Rejected:** FastNoiseLite Cellular — produces roughly equal-area plates (unrealistic). For a narrative sim, interesting geographic variation is more valuable than physical realism.

**Addendum (M1 bugfix):** Pure Voronoi produces perfectly straight plate boundaries, which propagate through every downstream layer as visible straight-line artifacts (mountain ridges, river valleys, biome bands). Fix: `boundary_perturb_strength` and `boundary_perturb_frequency` config params displace each tile's (x,y) by coherent OpenSimplex2 noise before the nearest-center lookup, making boundaries wavy. A value of 10 tiles of strength is the current default. Set to 0 to restore original straight Voronoi edges.

### B2: Plate center distribution via Poisson disc sampling
**Decision:** Plate centers placed using Poisson disc rejection sampling with `min_plate_separation_fraction` from config. Prevents degenerate sliver plates.  
```toml
[world_gen.tectonics]
plate_count = 15
min_plate_separation_fraction = 0.12
continental_plate_fraction = 0.45
boundary_perturb_strength  = 10.0   # tiles of noise displacement at Voronoi assignment
boundary_perturb_frequency = 0.07   # noise frequency (lower = broader curves)
```

### B3: Continental plate fraction is config-driven
**Decision:** Fraction of plates assigned as continental vs oceanic is a config parameter. Modders and power users will tune this.

### B4: River sinks via Priority Flood (Barnes algorithm)
**Decision:** Sinks below `min_lake_basin_tiles` threshold are filled (forced to drain). Basins above threshold become inland lakes (`IsLake` flag).  
**Rejected:** Carve channels (unrealistic canyon artifacts), leave all sinks as lakes (requires evaporation system).  
**New SimConfig section needed:**
```toml
[world_gen.rivers]
flow_accumulation_threshold = 50
min_lake_basin_tiles = 20
major_river_threshold = 500
```

### B5: Two-band wind model for precipitation
**Decision:** Tropical band (|normalizedLat - 0.5| < 0.25) uses East-to-West moisture sweep (trade winds). Mid-latitude + polar uses West-to-East (westerlies). Two separate passes over the world grid.  
**Rejected:** Single sweep (tropical East coasts incorrectly dry). Three bands (polar easterlies add marginal value at polar regions that are already dry from cold).

**Addendum (M1 bugfix):** The per-tile moisture carry decay was hardcoded at 0.97, causing interiors to reach near-zero moisture (~22% remaining at 50 tiles inland) and become desert. Changed to configurable `moisture_carry_decay = 0.993` in `[climate]`, giving ~70% moisture at 50 tiles and ~45% at 100 tiles. Deep interiors now produce grassland/savanna instead of uniform desert.

### B6: Storm corridors stored in WorldState, not as a flag
**Decision:** Storm corridor membership is computed at runtime from `WorldState.StormCorridorNormalizedLat` and `WorldState.StormCorridorHalfWidth`. `IsStormCorridor` StaticFlag (bit 8) is set at world gen but is secondary — the authoritative check at sim time is the WorldState parameters, which can drift.  
**Rationale:** Storm tracks shift with climate change (poleward drift during warming). A baked flag cannot shift. Computing membership inline is trivial (float subtraction + comparison).

### B7: Biome thresholds in SimConfig
**Decision:** All biome classification thresholds are config parameters under `[world_gen.biome_thresholds]`. `BiomeClassifier` is a pure static function: `Classify(temperature, moisture, elevation, flags, config) → BiomeType`. Hard biome boundaries in M1 (one biome per tile). Interpolation deferred to post-M1.

### B8: Ocean erosion pass to remove thin ridge artifacts
**Decision (M1 bugfix):** After OceanLayer marks tiles as ocean by elevation rank, a configurable erosion pass reclassifies narrow protrusions. Any non-ocean tile with ≥ `min_ocean_8neighbors` (default 5) of its 8 neighbours being ocean is converted to ocean. Applied `erosion_passes` times (default 2). This removes 1–2 tile wide continental fault lines that protrude above sea level as Mountain/Tundra ridges in the middle of ocean tiles, without touching solid continental coastlines (which have ≤4 ocean 8-neighbours). Implemented in `OceanLayer` after the elevation threshold pass and before coastal marking.
```toml
[world_gen.ocean]
erosion_passes       = 2
min_ocean_8neighbors = 5
```

### B9: Elevation smoothing to break fault-line river channels
**Decision (M1 bugfix):** After normalization, `ElevationLayer` applies `smoothing_passes` box-blur iterations (centre weight 4, 4-neighbour weight 1 each, divisor 8). This softens the sharp elevation step at plate boundaries so `RiverLayer` finds a smooth gradient rather than snapping to the lowest point of a fault trench (which was straight). Default 3 passes. Set to 0 to disable.
```toml
[world_gen.elevation]
smoothing_passes = 3
```

### Climate drift parameters in WorldState
These are WorldState fields initialized from SimConfig, can drift during sim:
```csharp
float CurrentSeaLevel              // drifts via disaster/climate events
float GlobalTemperatureAnomaly     // drives temperature drift
float GlobalPrecipitationMultiplier // global wetness/dryness
float StormCorridorNormalizedLat   // shifts poleward with warming
float StormCorridorHalfWidth       // can narrow/widen
float MonsoonIntensityMultiplier   // scales monsoon season bonus
float VolcanicActivityMultiplier   // elevated post-eruption, decays to 1.0
```

### SeasonalProfileGrid
**Decision:** Per-tile seasonal temperature and moisture deltas live in `SeasonalProfile[]` parallel array in WorldState (indexed by tile flat index). 8 bytes per tile × 120k = ~960KB. Populated during 1.3.8, included in state.bin, read every tick.  
**Rationale:** TileData has no room for 8 bytes of seasonal data. Computing profiles from scratch every tick requires re-running parts of the climate simulation.

```csharp
public struct SeasonalProfile  // 8 bytes
{
    public sbyte TempDeltaSpring, TempDeltaSummer, TempDeltaAutumn, TempDeltaWinter;
    public sbyte MoistureDeltaSpring, MoistureDeltaSummer, MoistureDeltaAutumn, MoistureDeltaWinter;
}
```

---

## Design Session C — Environmental Simulation Architecture

### C1: WorldRng static utility for tile-level randomness
**Decision:** `WorldRng` is a static utility class using `System.IO.Hashing.XxHash32`. All tile-level probability rolls use it in M1. Entity-level randomness in M2+ uses the same hash pattern via `IWorldStateReadOnly.GetRandomFloat(EntityId, salt)`.

```csharp
public static class WorldRng
{
    public static float FloatAt(int worldSeed, long tick, int x, int y, int salt);
    public static int IntAt(int worldSeed, long tick, int x, int y, int min, int max, int salt);
}

public static class DisasterSalts
{
    public const int Volcanic = 0, Earthquake = 1, Wildfire = 2,
                     Flood = 3, WildfireSpread = 4, FloodSpread = 5, DroughtCheck = 6;
}
```

### C2: Phase 1 (Environmental) is a direct mutator
**Decision:** Phase 1 directly reads and writes `WorldState`. No command pattern.  
**Rationale:** The environment has no agency. There is no contention to arbitrate. Cellular automaton rules (wildfire spread) don't need a resolve step.  
**God Mode hook (M3):** A `HashSet<TileCoord> _suppressedTiles` in WorldState populated from God Mode commands at tick start. Phase 1 checks this before applying disaster logic. No architectural change needed to Phase 1 for M3.

### C3: CausalEdge propagation pattern
**Decision:** `OriginEventId` stored on `ActiveDisaster` and `ActiveDrought` records propagates to `PendingEvent.CauseEventId`. Phase 7 creates a `CausalEdge` row when `CauseEventId` is set.  
**Probabilistic influence creates no edge.** A drought that elevated wildfire probability does not get a causal edge to fires that occurred in the drought zone. Only direct causation (drought → this specific biome change) creates edges.  
**Spread causation:** Wildfire spreading from tile A to tile B uses the *root fire's* `OriginEventId`, not a per-spread-step chain. Keeps causal graph tractable.

### C4: PendingEvent type
**Decision:** Phase 1 produces `PendingEvent` records. Phase 7 enriches them into full `SimEvent` records (assigns Id, runs classifier, applies gate, writes to DB).

```csharp
public sealed record PendingEvent(
    EventType Type,
    TileCoord? Location,
    EventId? CauseEventId,
    string PayloadJson
);
```

Phase 1 knows *what happened and why*. Phase 7 knows *how significant it was and whether to record it*.

### C5: Disaster sampling — iterate all tiles with chunk skip
**Decision:** Iterate all tiles per tick. `ChunkSummaryFlags` on each `TileChunk` enables skipping entire 16×16 chunks that have no eligible tiles for a given disaster type.

```csharp
[Flags] public enum ChunkSummaryFlags : byte
{
    HasVolcanicTile   = 1 << 0,
    HasFaultLineTile  = 1 << 1,
    HasForestTile     = 1 << 2,
    HasRiverTile      = 1 << 3,
    HasActiveDisaster = 1 << 4,
}
```

Candidate lists deferred to M4 profiling. At 120k tiles, simple per-tile checks cost ~1-2ms/pass, well within budget.

### C6: Phase 1 tick cadence
**Decision:**
- Per seasonal tick: wildfire ignition, volcano, earthquake, flood ignition, disaster spread (all disasters)
- Per year (gated on `CurrentSeason == Season.Spring`): drought check, climate drift, resource regeneration, sea level drift, VolcanicActivityMultiplier decay

### C7: DroughtRegion model
**Decision:** Droughts are regional events, not per-tile registry entries. Each `ActiveDrought` covers a `(LatitudeBandIndex, AffectedBiome)` pair. Membership is computed at runtime via `.Any()` check on the short `ActiveDroughts` list.

---

## Design Session D — UI Boundary and Tile Inspector

### D1: Tile inspector via snapshot extension (Option D1)
**Decision:** UI pushes `SetInspectedTile(TileCoord?)` command. Sim thread performs all registry lookups (ResourceRegistry, ActiveTileDisasters, ActiveDroughts) on its thread, builds `TileInspectorData`, includes it in the next `WorldSnapshot`. UI reads it from the snapshot. One-tick latency (imperceptible).  
**Rejected:** Direct WorldState read from UI (data race), request/response channel (complex for a point query).

### D2: TileDisplayData vs TileInspectorData — two distinct shapes
**Decision:** Keep two separate data records for two use cases:
- `TileDisplayData`: lightweight, created for all ~1000+ visible tiles every frame. Contains effective (current) values, not base values.
- `TileInspectorData`: full detail, created for one tile on demand. Contains base values, seasonal profiles, registry data.

`TileDisplayData.HasActiveDisaster` is computed from `ActiveTileDisasters.ContainsKey(coord)` by the sim thread when building the snapshot.

### D3: UI→Sim state updates through CommandQueue — viewport removed
**Original decision:** `SetViewport`, `SetInspectedTile`, `SetActiveOverlay` all go through `CommandQueue`.

**Revised (M1 bugfix):** `SetViewport` is removed from this list. The sim no longer tracks the camera viewport. `WorldSnapshot.AllTiles` contains the full world grid; `TileMapRenderer` computes the visible range from `Camera2D` on the UI thread each frame. This eliminates the one-tick lag that made pan/zoom feel unresponsive and removes a round-trip command on every camera move.

`SetInspectedTile` and `SetActiveOverlay` remain in `CommandQueue` — they change sim-side state (`WorldState.InspectedTile`, `SimLoop._overlay`) so must cross the thread boundary.

### D4: WorldSnapshot includes drift parameters
**Decision:** `WorldSnapshot` includes the current drift parameters (`GlobalTemperatureAnomaly`, `GlobalPrecipitationMultiplier`, `StormCorridorNormalizedLat`) so the UI can display world-level climate status without additional queries.

### D5: World gen startup sequence
**Decision:**
1. Game1 constructor starts world gen on a background Task
2. Progress reported via `ConcurrentQueue<(string LayerName, float Fraction)>` (thread-safe bridge, drained in `Game1.Update()`)
3. `WorldGenPipeline.RunFullAsync()` returns a complete `WorldState` (pipeline IS the WorldState factory)
4. On completion, `SimLoop` is created with the WorldState and started on a dedicated background Thread
5. UI transitions from progress screen to main sim view

---

---

## Design Session E — Post-M2 Character Behavior and Ancestry

**Date:** June 2026. Post-milestone playtest found zero wars/rivalries in 449 simulated years. The following decisions address this by seeding the conflict pipeline and giving characters more varied lives.

### E1: Wanderlust via tile-dwell counter
**Decision:** Add `TicksInCurrentTile` to `Tier1Character`. Travel utility score gets a bonus that scales from 0 → `WanderlustBonus` over `WanderlustMaxTicks` ticks stationary (approximately 2 seasons). The bonus is multiplied by a role factor: founders × 0.15, civ members × 0.5, free agents × 1.0. Curiosity scales from a floor (0.3 at Curiosity=0) to 1.0 at Curiosity=1, so even the least curious roles occasionally leave.  
**Rationale:** Without wanderlust, staying put and socializing always scored higher than travel — characters never expanded or explored. Role modifiers prevent kings from wandering away from their settlements while keeping rangers and outcasts mobile.

### E2: Social action cap — one target per tick
**Decision:** Changed from emitting one social command per co-located character to picking the single best-scoring social target. With 10 chars per tile, uncapped softmax allocated ~9.5% probability to travel; capped → ~51%.  
**Rationale:** The probability mass was spread too thin across many social targets. Capping forces the char to choose their most important relationship interaction, which also makes the action selection more narratively coherent.

### E3: Territorial trust drain to seed rivalry
**Decision:** Aggressive founders (Aggression > threshold, default 0.55) at their own settlement tile drain trust of foreign-civ visitors by a fixed amount per tick (0.025). This stops when the relationship is already decided (ally/rival/war).  
**Rationale:** `RivalryDeclared` required Trust < -0.1, but trust only went upward through positive interactions. Territorial pressure creates the first negative trust without requiring explicit hostility, and emergent within a few years of cross-civ contact.

### E4: Beast-character combat
**Decision:** Predatory beasts (Aggression ≥ 0.4) on the same tile as a character have a 15% per-tick chance to attack. Damage = `beast.Strength × 0.3`, floored at 1. Characters can die. New event type `BeastAttackedChar = 2007`.  
**Rationale:** `FindPrey()` on `LegendaryBeast` only targeted other beasts — characters were completely invisible to them. Adding this creates the adventure/survival narrative the history log needed.

### E5: Ancestry system design
**Decision:** Six ancestries loaded from `config/ancestries.toml`: human (baseline), elf (long-lived, curious), dwarf (steadfast, mountain-born), dark_elf (aggressive, long-lived), orc (short-lived, aggressive), halfling (sociable, honest). Each ancestry defines:
- Biome-weighted spawn probability
- Personality and aptitude bias offsets (max ±0.2 — individual variation ≈ stddev 0.2, equal to max bias, keeping individuals more distinctive than their ancestry stereotype)
- Lifespan range in seasons (orc 120–320 → 30–80 years; elf 800–2000 → 200–500 years)
- Ancestry-specific first-name and epithet lists
- First-meeting trust modifiers and cultural distance values

**Rejected alternatives:**
- Configuring ancestry at world-builder time (deferred — UI for world config is M3/M4 scope)
- Mixed-ancestry tracking (deferred — 5% cutoff threshold designed but not implemented; traces below threshold are dropped on birth)
- Color-coding by ancestry in UI (deferred — would require filtering controls not in scope for the current debug UI)
- Tolkien-style names (rejected — own lists instead; more original and avoids IP issues)

### E6: Cultural and personality trust drains
**Decision:** Two passive per-tick drains applied when characters of different civs share a tile:
1. `CulturalDistance × CulturalDistanceDrainRate` — how alien the ancestries feel to each other (elf/dark_elf: 0.70; human/halfling: 0.10)
2. `|stabilityA - stabilityB| × PersonalityMismatchDrainRate` — chars with very different Stability punish each other

First-meeting modifier (one-time): average of both ancestries' `FirstMeetingTrust` for the other, applied at the tick the relationship edge is first created.  
**Rationale:** These create organic negative trust gradients between mixed-ancestry civs even without direct conflict, making cross-ancestry political situations naturally more volatile.

### E7: Ancestry loaded alongside SimConfig, not in it
**Decision:** `AncestryRegistry` is stored as `SimConfig.AncestryRegistry` but NOT populated by Tomlyn — it's set by `AncestryLoader.LoadOrDefault()` called inside `SimConfigLoader.LoadOrCreateDefault()` after TOML loading. `ancestries.toml` lives in the same `config/` directory as `sim_config.toml`.  
**Rationale:** TOML can't express the nested array-of-tables structure of ancestry configs using the existing flat `SimConfig` mapping approach. Separate loader keeps the ancestry format independent and follows the same pattern as `BeastCatalogLoader`.

---

## New Types Introduced by Design Sessions

| Type | Location | Purpose |
|---|---|---|
| `ResourceDeposit` | WorldEngine.Sim/World/ | Per-deposit data (type, quality, depth) |
| `ResourceRegistry` | WorldState field | `Dictionary<TileCoord, List<ResourceDeposit>>` |
| `DisasterType` | WorldEngine.Sim/Events/ | Enum: Wildfire, Flood, VolcanicAsh, SeismicDamage |
| `ActiveDisaster` | WorldEngine.Sim/World/ | Per-tile disaster record with intensity, duration, origin |
| `ActiveDrought` | WorldEngine.Sim/World/ | Regional drought record with band, biome, intensity |
| `ActiveTileDisasters` | WorldState field | `Dictionary<TileCoord, List<ActiveDisaster>>` |
| `ActiveDroughts` | WorldState field | `List<ActiveDrought>` |
| `SeasonalProfile` | WorldEngine.Sim/Tiles/ | 8-byte struct, per-tile seasonal deltas |
| `SeasonalProfiles` | WorldState field | `SeasonalProfile[]` parallel to TileGrid |
| `WorldRng` | WorldEngine.Sim/Core/ | Static deterministic RNG utility |
| `DisasterSalts` | WorldEngine.Sim/Core/ | Salt constants for WorldRng disambiguation |
| `ChunkSummaryFlags` | WorldEngine.Sim/Tiles/ | Byte flags per TileChunk for disaster skip |
| `PendingEvent` | WorldEngine.Sim/Events/ | Phase 1 output, Phase 7 input |
| `TileInspectorData` | WorldEngine.Sim/World/ | Full tile detail for UI inspector |
| `WorldGenProgressQueue` | WorldEngine.UI/ | `ConcurrentQueue<(string, float)>` for progress |
| `SettlementSnapshot` | WorldEngine.Sim/World/ | UI-facing settlement summary in WorldSnapshot |
| `AncestryConfig` | WorldEngine.Sim/Config/ | Per-ancestry data (lifespan, biases, names, weights) |
| `AncestryRegistry` | WorldEngine.Sim/Config/ | Dictionary wrapper + biome sampler for all ancestries |
| `AncestryLoader` | WorldEngine.Sim/Config/ | Loads `config/ancestries.toml` alongside sim_config.toml |

## SimConfig Sections to Add

```toml
[world_gen.tectonics]
plate_count = 15
min_plate_separation_fraction = 0.12
continental_plate_fraction = 0.45

[world_gen.rivers]
flow_accumulation_threshold = 50
min_lake_basin_tiles = 20
major_river_threshold = 500

[world_gen.biome_thresholds]
# Populated when implementing BiomeClassifier (story 1.3.7)
# All values are 0-255 scaled to match byte fields in TileData
```
