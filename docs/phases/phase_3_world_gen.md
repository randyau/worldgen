# Phase 3 — Epic 1.3: World Generation
**Status:** NOT STARTED  
**Requires:** Phase 2 complete  
**Reads required:** `docs/interface_contracts.md`, `docs/design_session_decisions.md` (DS-A and DS-B sections), `docs/snippets/patterns.md` (WorldRng usage)

---

## Goal
Build the generation pipeline that produces a fully populated WorldState from a seed. This is the longest phase. Story 1.3.8 (TileGrid assembly) contains the reproducibility test — that test must pass before Phase 3 is considered done.

## Key Design Constraints
- All layer results are immutable once produced
- Layers read only from committed predecessors in `WorldGenContext`
- XxHash32 randomness via WorldRng — no `new Random()`, no FastNoiseLite seeds except per-layer seed constants
- FastNoiseLite noise is seeded per-layer via `LayerSeeds.[LayerName]` constants XOR'd with worldSeed
- World is a cylinder: X wraps (width-1 → 0), Y is clamped at poles

---

## Story 1.3.1 — Pipeline Orchestrator

**Files to create:**
```
WorldEngine.Sim/WorldGen/WorldGenContext.cs      # accumulates layer results
WorldEngine.Sim/WorldGen/WorldGenPipeline.cs     # RunFullAsync entry point
WorldEngine.Sim/WorldGen/IWorldGenLayer.cs       # generic layer interface
WorldEngine.Sim/WorldGen/LayerSeeds.cs           # int constants, unique per layer
WorldEngine.Sim/WorldGen/ElevationResult.cs      # stub result types
WorldEngine.Sim/WorldGen/TectonicResult.cs
WorldEngine.Sim/WorldGen/RiverResult.cs
WorldEngine.Sim/WorldGen/ClimateResult.cs
WorldEngine.Sim/WorldGen/BiomeResult.cs
WorldEngine.Sim/WorldGen/MagicResult.cs
WorldEngine.Sim/WorldGen/ResourceResult.cs
WorldEngine.Sim/WorldGen/OceanResult.cs
WorldEngine.Sim/WorldGen/PoiResult.cs
```

**WorldGenPipeline.RunFullAsync signature:**
```csharp
public async Task<WorldState> RunFullAsync(
    WorldConfig config,
    SimConfig simConfig,
    IProgress<(string Layer, float Fraction)>? progress = null,
    CancellationToken ct = default)
```

**LayerSeeds — all must be unique, document what they're for:**
```csharp
public static class LayerSeeds
{
    public const int Tectonic   = 0x1A2B3C;
    public const int Elevation  = 0x2B3C4D;
    public const int Ocean      = 0x3C4D5E;
    public const int River      = 0x4D5E6F;
    public const int Magic      = 0x5E6F7A;
    public const int Climate    = 0x6F7A8B;
    public const int Biome      = 0x7A8B9C;
    public const int Resource   = 0x8B9CAD;
    public const int Poi        = 0x9CADBE;
}
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/WorldGenPipelineTests.cs`):
```
LayerSeeds_AllValuesAreUnique                  # no two LayerSeed constants share a value
Pipeline_RunFullAsyncCompletesWithoutThrow     # smoke test with small config (100km×100km)
Pipeline_ProgressCallbackInvokedForEachLayer   # progress called ≥9 times (once per layer)
Pipeline_ReturnsNonNullWorldState              # result not null
```

**Done when:** Pipeline skeleton runs all layers (stubbed) end-to-end.

---

## Story 1.3.2 — TectonicLayer

**File:** `WorldEngine.Sim/WorldGen/Layers/TectonicLayer.cs`

**Algorithm:**
1. Generate N plate centers using Poisson disc sampling (config: `TectonicsConfig.PlateCount`, `MinPlateSeparationFraction`)
2. Assign each plate as continental or oceanic (config: `ContinentalPlateFraction`)
3. For each tile, assign `PlateId` = index of nearest center (cylinder-aware Euclidean distance)
4. Detect fault lines: tiles where any neighbor has a different PlateId
5. Detect volcanic zones: fault line tiles where one plate is continental and adjacent is oceanic (subduction)
6. Mark continental fault-line tiles with high metal deposit potential

**TectonicResult should include:** per-tile PlateId, IsVolcanic, IsFaultLine, DepositPotential arrays.

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/TectonicLayerTests.cs`):
```
Tectonics_AllTilesHavePlateIdAssigned          # no tile has PlateId == 0xFF (unset sentinel)
Tectonics_PlateCountMatchesConfig              # distinct PlateId values == config.PlateCount
Tectonics_FaultLinesAtPlateBoundaries          # every fault tile has at least one neighbor with different PlateId
Tectonics_VolcanicZonesOnlyAtSubduction        # volcanic tiles must be on a boundary with one continental + one oceanic plate
Tectonics_ContinentalFractionApproximate       # fraction continental ≈ config value ±10%
Tectonics_SameSeedSameResult                   # determinism test (use LayerSeeds template)
```

**Done when:** Tests pass.

---

## Story 1.3.3 — ElevationLayer + OceanLayer

**Files:**
```
WorldEngine.Sim/WorldGen/Layers/ElevationLayer.cs
WorldEngine.Sim/WorldGen/Layers/OceanLayer.cs
```

**ElevationLayer algorithm:**
1. FastNoiseLite Simplex noise seeded with `worldSeed ^ LayerSeeds.Elevation`
2. Add tectonic elevation contribution (mountain ridges at continental collisions, trenches at subduction, highlands on continental plates)
3. Apply volcanic zone boost (peaks at volcanic tiles)
4. Normalize to byte range (0-255)

**OceanLayer algorithm:**
1. Threshold from `SimConfig.WorldGen.SeaLevelFraction` (fraction of tiles that are ocean)
2. Tiles below threshold: IsOcean=true, Elevation=0 (sea floor)
3. Coast detection: land tile adjacent to any ocean tile → `IsCoastal` StaticFlag

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/ElevationLayerTests.cs`, `OceanLayerTests.cs`):
```
Elevation_AllValuesInByteRange                 # all elevations 0-255
Elevation_MountainBoundaryHigherThanPlains     # mean elevation at continental boundaries > global mean
Elevation_VolcanicTilesHaveHighElevation       # mean elevation at volcanic tiles > global mean
Elevation_SameSeedSameResult
Ocean_LandFractionMatchesConfig                # land fraction within ±5% of (1 - SeaLevelFraction)
Ocean_CoastalFlagOnLandAdjacentToOcean         # every coastal tile has ocean neighbor
Ocean_NoCoastalFlagOnInteriorLand              # interior land tiles have IsCoastal=false
Ocean_SameSeedSameResult
```

**Done when:** Tests pass.

---

## Story 1.3.4 — RiverLayer

**File:** `WorldEngine.Sim/WorldGen/Layers/RiverLayer.cs`

**Algorithm:**
1. Initialize flow direction: each land tile points to lowest neighbor (D8 — 8 cardinal + diagonal)
2. Priority Flood sink filling (Barnes 2014 algorithm):
   - Sinks with basin < `min_lake_basin_tiles` tiles: fill to nearest spillpoint (force drain)
   - Sinks with basin ≥ threshold: mark as lake (`IsLake` StaticFlag)
3. Flow accumulation: each tile's flow = 1 + sum(uphill neighbors' flow)
4. Mark tiles with flow ≥ `flow_accumulation_threshold` as `HasRiver`
5. Mark tiles with flow ≥ `major_river_threshold` as `IsPOICandidate`
6. Cylinder-aware throughout (X wraps for neighbor lookups)
7. Rivers terminate at ocean tiles or lake tiles — flow stops entering ocean

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/RiverLayerTests.cs`):
```
River_AllRiversFlowDownhill                    # every river tile's flow direction points to equal-or-lower elevation
River_TerminatesAtOceanOrLake                  # flow paths all reach ocean or lake
River_NoSinksBelowThreshold                    # no unresolved sinks in small-basin terrain
River_SmallBasinsFilledNotLakes                # basins < min_lake_basin_tiles have no IsLake flag
River_LargeBasinsBecomeLakes                   # basins >= threshold get IsLake
River_EastWestWrappingHandled                  # river near East edge can wrap to West side
River_SameSeedSameResult
```

**Done when:** Tests pass.

---

## Story 1.3.5 — MagicLayer (Stub for M1)

**File:** `WorldEngine.Sim/WorldGen/Layers/MagicLayer.cs`

**Algorithm:** FastNoiseLite Simplex noise + volcanic zone weighting (volcanic tiles get ×2.0 magic multiplier). Mark high-magic tiles near volcanic zones as `IsPOICandidate`.

```csharp
// V2: magic physical substrate — behaviors driven by magic intensity not implemented until M2+
// In M1: generate and store MagicIntensity, mark IsPOICandidate, nothing else
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/MagicLayerTests.cs`):
```
Magic_AllValuesInByteRange                     # 0-255
Magic_VolcanicZonesStatisticallyHigherMagic    # mean magic near volcanic > global mean
Magic_SameSeedSameResult
```

**Done when:** Tests pass.

---

## Story 1.3.6 — ClimateLayer

**File:** `WorldEngine.Sim/WorldGen/Layers/ClimateLayer.cs`

**Algorithm:**
1. **BaseTemperature**: latitude-based cosine curve (hottest at equator/lat 0.5, coldest at poles). Apply elevation lapse rate (−6°C/1000m, scaled to byte).
2. **BaseMoisture two-band wind sweep:**
   - Tropical band (|normalizedLat − 0.5| < 0.25): West-to-East moisture carry (trade winds flow East)
     - CORRECTION: Actually trade winds blow East-to-West (toward equator). Let me use: tropical band sweeps East-to-West for moisture.
     - Each tile receives moisture from East neighbor minus mountain rain shadow loss.
   - Mid-latitude + polar: West-to-East sweep (westerlies blow East)
   - Rain shadow: tiles immediately lee of mountain lose 60% moisture (config-driven)
3. **Monsoon zones**: tropical tiles with BaseMoisture > threshold get `IsMonsoonTile` flag (stored in ClimateResult, not TileData — no bit for it yet)
4. **Storm corridor**: tiles where `|normalizedLat - stormLat| < stormHalfWidth` set `IsStormCorridor` StaticFlag
5. **SeasonalProfile**: compute per-tile (4 temp deltas + 4 moisture deltas):
   - Tropical: high moisture bonus in Summer, moderate in all seasons
   - Storm corridor: moisture bonus in Autumn (storm season)
   - Temperate: cold Winter temp delta, cool Summer temp delta
   - Polar: extreme Winter temp delta

**New SimConfig entries to add** (`[climate]` section):
```toml
[climate]
tropical_band_half_width = 0.25             # fraction of world height
rain_shadow_loss_fraction = 0.6             # moisture lost in mountain rain shadow
mountain_elevation_threshold = 180          # byte elevation above which rain shadow applies
monsoon_moisture_threshold = 160            # byte moisture above which tile is monsoon zone
storm_corridor_normalized_lat = 0.35        # fractional lat (0=south, 1=north)
storm_corridor_half_width = 0.08            # fraction of world height
storm_corridor_moisture_bonus = 0.3         # multiplier bonus during storm season
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/ClimateLayerTests.cs`):
```
Climate_PolarTilesColderThanEquatorial         # mean temp polar band < mean temp tropical band
Climate_ElevationReducesTemperature            # high elevation tiles colder than low elevation same lat
Climate_RainShadowBehindMountains              # tile immediately East of mountain has lower moisture
Climate_MonsoonZonesOnlyInTropics              # IsMonsoonTile tiles are all in tropical band
Climate_StormCorridorAtConfiguredLatitude      # IsStormCorridor tiles centered on config lat
Climate_SeasonalProfilesAllFourValuesNonZero   # no all-zero seasonal profiles for land tiles
Climate_SameSeedSameResult
```

**Done when:** Tests pass.

---

## Story 1.3.7 — BiomeLayer + ResourceLayer + PoiCandidateLayer

**Files:**
```
WorldEngine.Sim/WorldGen/Layers/BiomeLayer.cs
WorldEngine.Sim/WorldGen/Layers/ResourceLayer.cs
WorldEngine.Sim/WorldGen/Layers/PoiCandidateLayer.cs
WorldEngine.Sim/WorldGen/BiomeClassifier.cs      # pure static function
```

**BiomeClassifier — pure function:**
```csharp
public static class BiomeClassifier
{
    public static BiomeType Classify(
        byte temperature, byte moisture, byte elevation,
        TileStaticFlags flags, SimConfig config) { ... }
}
```

**Biome priority rules (from config thresholds):**
1. Ocean if OceanLayer says IsOcean → `BiomeType.Ocean`
2. Elevation > `HighMountain` threshold → `BiomeType.HighMountain`
3. Elevation > `Mountain` threshold → `BiomeType.Mountain`
4. IsVolcanic flag → `BiomeType.Volcanic`
5. IsLake flag → `BiomeType.CoastalWater`
6. IsCoastal and low elevation → `BiomeType.Beach`
7. Temperature/moisture matrix (Whittaker diagram, all thresholds from config)

**SimConfig biome thresholds to add** (`[world_gen.biome_thresholds]`):
```toml
[world_gen.biome_thresholds]
high_mountain_elevation = 220
mountain_elevation = 180
hills_elevation = 140
hot_temperature = 180
cold_temperature = 80
polar_temperature = 40
wet_moisture = 160
dry_moisture = 60
arid_moisture = 30
```

**ResourceLayer algorithm:**
1. Continental fault-line tiles: assign Iron, Copper, or Stone deposits (from TectonicResult.DepositPotential)
2. Volcanic tiles: assign Sulfur, sometimes rare Obsidian (`HasRareResource`)
3. Hill/Mountain tiles: assign Stone, sometimes Coal
4. All assignments: write to ResourceRegistry, set HasDeposit/HasRareResource StaticFlags

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/BiomeLayerTests.cs`, `ResourceLayerTests.cs`):
```
Biome_HighMountainForHighElevation             # tiles with elevation > threshold get HighMountain
Biome_VolcanicOverridesClimate                 # volcanic tiles always Volcanic biome
Biome_OceanTilesAreOceanBiome                  # ocean tiles get BiomeType.Ocean
Biome_AllThresholdsFromConfig                  # BiomeClassifier.Classify with different config changes result
Biome_SameSeedSameResult
BiomeClassifier_DesertForHotDryInput           # pure unit test — hot+dry=Desert
BiomeClassifier_TropicalRainforestForHotWet    # hot+wet=TropicalRainforest
BiomeClassifier_TundraForColdInput             # cold=Tundra (any moisture)
Resource_DepositAtVolcanicTiles                # at least some volcanic tiles have deposits
Resource_HasDepositFlagWhenRegistryEntry       # HasDeposit flag set iff ResourceRegistry has entry
Resource_HasRareResourceForRareDeposits        # rare resources set HasRareResource
Resource_SameSeedSameResult
```

**Done when:** Tests pass.

---

## Story 1.3.8 — TileGrid Assembly

**File:** `WorldEngine.Sim/WorldGen/TileGridAssembler.cs` (or method in WorldGenPipeline)

**Algorithm:**
1. Allocate `TileGrid` for `config.TileWidth × config.TileHeight`
2. For each tile in parallel (`Parallel.For` over Y rows):
   - Read all layer results for (x, y)
   - Populate all TileData fields
   - Set StaticFlags based on layer results
   - Initialize `CurrentMoisture = BaseMoisture`, `DynFlags = None`, `RoadLevel = 0`, `CivControl = 0`
3. Allocate `SeasonalProfile[]` parallel array (length = TileWidth × TileHeight)
4. Populate SeasonalProfile for each tile from ClimateResult
5. Initialize WorldState with: TileGrid, SeasonalProfiles, WorldConfig, SimConfig, ResourceRegistry, drift parameters at genesis defaults
6. Compute and write border manifests to `manifests.bin` sidecar
7. Release all layer result objects (let GC reclaim)

**WRITE TESTS FIRST** (`WorldEngine.Tests/Reproducibility/ReproducibilityTests.cs`):

**This is the most important test. It must fail before BuildTileGrid is written, then pass after.**

```
SameSeedProducesSameWorld                      # TWO full pipeline runs with seed=12345 must produce
                                               # byte-identical TileData at every coordinate and
                                               # matching SeasonalProfile at every index
TileGrid_AllLandTilesHaveNonDefaultBiome       # no land tile has BiomeType.Ocean
TileGrid_SeasonalProfilesPopulated             # no tile has all-zero SeasonalProfile
TileGrid_ElevationPopulated                    # no land tile has Elevation == 0
BorderManifests_FileWritten                    # manifests.bin exists after pipeline runs
ResourceRegistry_DepositsOnlyAtFlaggedTiles    # every registry entry has HasDeposit flag set
WorldState_DriftParametersInitialized          # GlobalTemperatureAnomaly == 0.0 at genesis
```

**Remove the `[Skip]` attribute from `SameSeedProducesSameWorld` here.** It should now actually run and pass.

**Done when:** ALL tests in Reproducibility/ pass. This is the hardest Phase 3 milestone.

---

## Phase 3 Done Criteria

- `SameSeedProducesSameWorld` passes — this is the non-negotiable gate
- All 7 layers produce correct output as verified by their unit tests
- `manifests.bin` is written by the pipeline
- `WorldState` returned by `RunFullAsync` is fully populated
- No hardcoded numbers in any layer — all thresholds come from SimConfig
- `dotnet build` — 0 warnings
- `dotnet test` — all tests pass
