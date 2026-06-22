# Phase 5 — Epic 1.5: Environmental Simulation
**Status:** NOT STARTED  
**Requires:** Phase 4 complete (PhaseRunner Phase 1 stub must exist)  
**Reads required:** `docs/design_session_decisions.md` (DS-C), `docs/snippets/patterns.md` (#3 WorldRng, #5 tile iteration, #6 PendingEvent emission)

---

## Goal
Implement Phase 1 of the 7-phase tick: seasonal climate variation, climate drift, natural disasters, resource dynamics, and sea level changes. The stub Phase 1 from story 1.4.5 gets replaced with real logic here.

## Key Architecture Points
- Phase 1 is a **direct mutator** — no command pattern. Reads and writes WorldState in-place.
- All randomness via `WorldRng.FloatAt()` with `DisasterSalts` constants — never `System.Random`
- Phase 1 emits `List<PendingEvent>` consumed by Phase 7 (story 1.4.5's PhaseRunner already wires this)
- Per-seasonal-tick: disaster ignition and spread
- Per-year (gated on `world.CurrentSeason == Season.Spring`): drift, regeneration, sea level

---

## Story 1.5.1 — Seasonal Climate Variation

**File:** `WorldEngine.Sim/Simulation/Phases/EnvironmentalPhase.cs` (replace stub)

This is the first real Phase 1 logic. Each seasonal tick, update `CurrentMoisture` for all tiles:

```
effectiveMoisture = (BaseMoisture + SeasonalProfile.MoistureDelta[CurrentSeason])
                  × GlobalPrecipitationMultiplier
                  × (IsStormCorridor && CurrentSeason == Autumn ? StormBonus : 1.0f)
                  × (IsMonsoonTile && CurrentSeason == Summer ? MonsoonIntensityMultiplier : 1.0f)
```

Clamp to [0, 255] and write to `tile.CurrentMoisture`.

**Note:** `IsMonsoonTile` is not a StaticFlag. It's derived at runtime: `tile.BiomeType == BiomeType.TropicalRainforest || (tile.BaseMoisture > config.Climate.MonsoonMoistureThreshold && IsInTropicalBand(coord))`.

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/EnvironmentalPhaseTests.cs`):
```
Seasonal_CurrentMoistureUpdatedForAllLandTiles  # after one tick, all land CurrentMoisture != BaseMoisture
Seasonal_StormCorridorWetterInAutumn            # storm corridor tiles: Autumn moisture > Summer moisture
Seasonal_MonsoonTileWetterInSummer              # monsoon tile: Summer moisture >> Winter moisture
Seasonal_MoistureClampedTo255                   # overflow never exceeds byte range
Seasonal_DrySeasonReducesMoisture               # dry season delta reduces CurrentMoisture below base
```

**Done when:** Tests pass. CurrentMoisture is updated each tick.

---

## Story 1.5.2 — Climate Drift

**File:** Add annual drift methods to `EnvironmentalPhase.cs`

Run **once per year** (check: `world.CurrentSeason == Season.Spring && justChangedSeason`):

1. **Temperature drift**: `world.GlobalTemperatureAnomaly += simConfig.Climate.AnnualTempDriftRate`
   - Clamp to `[-config.MaxCoolingAnomaly, +config.MaxWarmingAnomaly]`
2. **Storm corridor shift**: `world.StormCorridorNormalizedLat += anomaly * config.StormCorridorShiftPerDegree`
   - Poleward drift with warming (add positive delta to lat in northern hemisphere)
3. **Monsoon multiplier**: varies ±config value based on anomaly
4. **Biome reclassification**: re-run `BiomeClassifier.Classify()` on every land tile using new effective temp/moisture. If biome changed: update `tile.BiomeType`, emit `PendingEvent(EventType.BiomeChanged, ...)`, update `ChunkSummaryFlags`

**New SimConfig entries (`[climate]` additions):**
```toml
annual_temp_drift_rate = 0.0          # zero = stable climate; set positive for warming scenario
max_warming_anomaly = 5.0             # max degrees warming before stabilizing
max_cooling_anomaly = 3.0
storm_corridor_shift_per_degree = 0.005  # fractional lat shift per degree of anomaly
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/ClimateDriftTests.cs`):
```
Drift_TemperatureAnomalyIncreases              # after annual tick, anomaly changes by drift rate
Drift_AnomalyClamped                           # anomaly cannot exceed MaxWarmingAnomaly
Drift_BiomeChangesAfterSufficientWarming       # warm enough, cold biome tiles reclassify
Drift_BiomeChangedEventQueuedOnChange          # BiomeChanged PendingEvent emitted when tile changes
Drift_StormCorridorShiftsWithAnomaly           # StormCorridorNormalizedLat shifts when anomaly > 0
Drift_NoDriftIfRateIsZero                      # with rate=0, anomaly stays 0 forever
```

**Done when:** Tests pass.

---

## Story 1.5.3 — Natural Disaster System

**File:** Add disaster methods to `EnvironmentalPhase.cs`

**Volcano eruption** (per seasonal tick, with chunk skip on `HasVolcanicTile`):
1. For each volcanic tile: WorldRng roll with `DisasterSalts.Volcanic`
2. If roll < `config.Disasters.VolcanicEruptionProbabilityPerTick`:
   - Add `ActiveDisaster(VolcanicAsh, intensity, TicksRemaining=-1, OriginEventId=default)` to registry
   - Set `HasActiveDisaster` DynFlag
   - Emit `PendingEvent(EventType.VolcanicEruption, coord, null, payload)`
   - Boost `world.VolcanicActivityMultiplier` by config amount

**Earthquake** (per seasonal tick, with chunk skip on `HasFaultLineTile`):
1. For each fault line tile: WorldRng roll with `DisasterSalts.Earthquake`
2. If roll < `config.Disasters.EarthquakeProbabilityPerTick`:
   - Add `ActiveDisaster(SeismicDamage, intensity, TicksRemaining: config.EarthquakeDecayTicks, ...)`
   - Emit `PendingEvent(EventType.EarthquakeOccurred, ...)`

**Wildfire** (per seasonal tick, Summer + Autumn only, `HasForestTile` chunk skip):
1. Ignore tiles already on fire
2. Ignition: roll with `DisasterSalts.Wildfire`; higher chance if `CurrentMoisture < DryThreshold`
3. Spread: for each active wildfire tile, check each forest neighbor with `DisasterSalts.WildfireSpread`
4. Extinguish: tick down `TicksRemaining`; clear registry entry when 0; clear `HasActiveDisaster` if no disasters remain
5. All spread events share the root fire's `OriginEventId` (NOT a new CauseEventId chain)
6. Emit PendingEvent only on initial ignition, not spread

**Flood** (per seasonal tick, Spring + Summer, `HasRiverTile` chunk skip):
1. River tiles: roll with `DisasterSalts.Flood`; higher chance if `CurrentMoisture > WetThreshold`
2. Add `ActiveDisaster(Flood, ...)` to tile and immediate neighbors (radius=1)
3. Emit `PendingEvent(EventType.FloodOccurred, ...)`

**Drought** (per year, Spring gate):
1. For each latitude band × biome combination
2. Roll `DisasterSalts.DroughtCheck`; higher chance if `GlobalPrecipitationMultiplier < DroughtThreshold`
3. If drought starts: add `ActiveDrought` to `world.ActiveDroughts`, emit `PendingEvent(EventType.DroughtBegan, null, null, payload)`
4. Tick down existing droughts; remove when `SeasonsRemaining == 0`, emit `PendingEvent(EventType.DroughtEnded, ...)`

**New SimConfig entries (`[disasters]`):**
```toml
[disasters]
volcanic_eruption_probability_per_tick = 0.0002
earthquake_probability_per_tick = 0.0005
wildfire_ignition_probability_per_tick = 0.0003
wildfire_ignition_dry_multiplier = 3.0         # multiplier when moisture below dry threshold
wildfire_spread_probability_per_tick = 0.2
wildfire_max_ticks = 16
wildfire_dry_moisture_threshold = 60           # byte moisture below which fire spreads faster
flood_ignition_probability_per_tick = 0.0002
flood_wet_moisture_threshold = 200
flood_spread_radius = 1
earthquake_decay_ticks = 8
drought_probability_per_year = 0.05
drought_drought_multiplier = 2.0               # multiplier when precipitation below threshold
drought_precipitation_threshold = 0.7
drought_min_seasons = 2
drought_max_seasons = 8
volcanic_activity_boost = 0.5                  # added to VolcanicActivityMultiplier per eruption
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/DisasterSystemTests.cs`):
```
Disaster_VolcanicEruptionOnlyOnVolcanicTiles   # eruption events only at IsVolcanic tiles
Disaster_EarthquakeOnlyOnFaultLineTiles        # earthquake events only at IsFaultLine tiles
Disaster_WildfireOnlyInForestBiome             # wildfire only on forest biome tiles
Disaster_WildfireDoesNotIgniteOceanTiles       # sanity: no ocean disaster
Disaster_FloodOnlyOnRiverTiles                 # flood only at HasRiver tiles
Disaster_ActiveDisasterRegistryUpdated         # after ignition, tile appears in ActiveTileDisasters
Disaster_HasActiveDisasterFlagSet              # after ignition, DynFlags has HasActiveDisaster
Disaster_HasActiveDisasterFlagClearedOnExpiry  # after TicksRemaining hits 0, flag cleared
Disaster_WildfireSpreadsToAdjacentForest       # with spread prob=1.0, fire spreads in one tick
Disaster_MultipleDisastersCanStackOnOneTile    # flood + seismic damage both in registry (no overwrite)
Disaster_DroughtAddsToActiveDroughtsList       # drought object appears in world.ActiveDroughts
Disaster_DroughtRemovedWhenExpired             # after SeasonsRemaining=0, removed from list
Disaster_PendingEventEmittedOnIgnition         # PendingEvent list has entry after disaster starts
Disaster_SameSeedSameDisasters                 # determinism: same seed + same WorldState = same disaster events
```

**Done when:** All disaster tests pass. Chunk skip optimization working (verify no disaster fires on non-volcanic chunk for volcanic disaster type).

---

## Story 1.5.4 — Resource Dynamics

**File:** Add resource recovery to `EnvironmentalPhase.cs`

Run **per year** (Spring gate):

1. **Wildfire recovery**: tiles where wildfire just cleared (HasActiveDisaster was just removed, biome is Forest) get Fertility boost next season
2. **Drought impact**: tiles in an active drought region get Fertility reduction
3. **Natural recovery**: all land tiles with Fertility < BaseFertility get +1 Fertility/year (slow natural regeneration)
4. Emit `PendingEvent(EventType.ResourceRecovered, ...)` when a specific deposit-bearing tile recovers to full Fertility (Significance: Character tier — background unless combined with other factors)

**New SimConfig entries (`[resources]`):**
```toml
[resources]
fertility_recovery_per_year = 1                # byte units per year
post_fire_fertility_boost = 30                 # temporary fertility bonus after fire clears
drought_fertility_penalty_per_season = 5       # fertility lost per season while in drought
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/ResourceDynamicsTests.cs`):
```
Resource_FertilityIncreasesInRecovery          # after wildfire clears, fertility increases
Resource_FertilityDecreasesInDrought           # during drought, fertility declines
Resource_FertilityClampedTo255                 # post-fire boost does not exceed 255
Resource_NaturalRegenerationOccursAnnually     # low-fertility tile recovers 1/year
```

**Done when:** Tests pass.

---

## Story 1.5.5 — Sea Level Changes + VolcanicActivityMultiplier Decay

**File:** Add annual sea level + multiplier decay to `EnvironmentalPhase.cs`

Run **per year** (Spring gate):

1. **Sea level drift**: `world.CurrentSeaLevel += config.Climate.AnnualSeaLevelDriftRate * GlobalTempAnomalyFactor`
   - When `CurrentSeaLevel` exceeds or drops below tile elevation boundaries: reclassify coast tiles
   - Update `IsCoastal` StaticFlags on affected tiles
   - Emit `PendingEvent(EventType.SeaLevelChanged, null, null, payload)` when sea level changes by ≥ config threshold (always at least Regional significance)

2. **VolcanicActivityMultiplier decay**: `world.VolcanicActivityMultiplier = MathF.Lerp(world.VolcanicActivityMultiplier, 1.0f, config.Disasters.VolcanicDecayRate)`

**New SimConfig entries:**
```toml
[climate]
annual_sea_level_drift_rate = 0.0             # zero = stable; set positive for melting/sinking scenario
sea_level_event_threshold = 0.1              # minimum sea level change to emit SeaLevelChanged event
volcanic_decay_rate = 0.05                   # fraction toward 1.0 per year
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/SeaLevelTests.cs`):
```
SeaLevel_PendingEventEmittedWhenDeltaExceedsThreshold   # large drift emits event
SeaLevel_NoEventForTinyDrift                            # drift < threshold emits no event
SeaLevel_CoastalTilesUpdateWhenOceanExpands             # newly submerged tiles lose IsCoastal, new coast set
VolcanicMultiplier_DecaysTowardOne                      # boosted multiplier decreases each year
VolcanicMultiplier_NeverGoesBelow1                      # decay doesn't undershoot
```

**Done when:** Tests pass.

---

## Phase 5 Done Criteria

- All 5 environmental sub-systems tested and passing
- `SameSeedProducesSameDisasters` passes — environmental determinism verified
- PendingEvents correctly wired to Phase 7 via PhaseRunner (from Phase 4)
- `HasActiveDisaster` flag lifecycle: set on ignition, cleared on expiry, verified by tests
- No hardcoded probabilities — all in `[disasters]`, `[climate]`, `[resources]` SimConfig sections
- `dotnet test` — 0 failures
