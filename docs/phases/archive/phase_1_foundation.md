# Phase 1 — Epic 1.1: Project Foundation
**Status:** COMPLETE — 2026-06-22  
**Blocks:** Everything. Do this first.  
**Reads required before starting:** CLAUDE.md, GUARDRAILS.md  

---

## Goal
Bootstrap the solution: projects, NuGet packages, SimConfig, core value types, and WorldRng. No simulation logic yet — just the skeleton everything else plugs into.

---

## Story 1.1.1 — Solution and Project Setup

**Create:**
```
WorldEngine.sln
WorldEngine/
  WorldEngine.Sim/WorldEngine.Sim.csproj          # headless, no UI refs
  WorldEngine.UI/WorldEngine.UI.csproj             # MonoGame + Myra
  WorldEngine.Tests/WorldEngine.Tests.csproj       # xUnit
```

**Project references:**
- UI → Sim (one-way only)
- Tests → Sim (Tests may import Sim types; no UI tests in M1)

**NuGet packages:**

| Package | Project | Purpose |
|---|---|---|
| FastNoiseLite 1.1.0 | Sim | Terrain noise |
| Microsoft.Data.Sqlite | Sim | SQLite access |
| Dapper | Sim | SQL mapping |
| MessagePack | Sim | state.bin serialization |
| Tomlyn | Sim | TOML config loading |
| MonoGame.Framework.DesktopGL | UI | Game window + rendering |
| Myra | UI | UI toolkit |
| xunit | Tests | Test framework |
| FluentAssertions | Tests | Assertions |

**Sim csproj settings:**
```xml
<LangVersion>12</LangVersion>
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>  <!-- for Marshal.SizeOf assertions -->
```

**Tests:** Build only — no automated test yet.
```
dotnet build --no-incremental   # must exit 0 with 0 warnings
```

**Done when:** `dotnet build` exits 0 with 0 warnings on all three projects.

---

## Story 1.1.2 — SimConfig + TOML Loading

**Files to create:**
```
WorldEngine.Sim/Config/SimConfig.cs          # root config class
WorldEngine.Sim/Config/SimConfigLoader.cs    # Tomlyn loader
WorldEngine.Sim/Config/WorldGenConfig.cs     # world_gen section
WorldEngine.Sim/Config/DisasterConfig.cs     # disasters section
WorldEngine.Sim/Config/EventsConfig.cs       # events section
WorldEngine.Sim/Config/ClimateConfig.cs      # climate section
WorldEngine.Sim/Config/TectonicsConfig.cs    # world_gen.tectonics section
WorldEngine.Sim/Config/RiversConfig.cs       # world_gen.rivers section
WorldEngine.Sim/Config/BiomeThresholdConfig.cs  # world_gen.biome_thresholds section
config/sim_config.toml                        # add new sections
```

**New TOML sections to add** (append to existing sim_config.toml):
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
# Populated during 1.3.7 — add fields when implementing BiomeClassifier
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/SimConfigTests.cs`):
```
SimConfigLoader_LoadsExistingToml              # loads sim_config.toml, checks at least one value
SimConfigLoader_AllSectionsPresent             # verify TectonicsConfig, RiversConfig non-null
SimConfigLoader_MissingFileCreatesDefault      # delete toml, verify DefaultConfig() runs without throw
SimConfig_TectonicsPlateCountIsPositive        # plate_count > 0
```

**Done when:** Tests pass, `SimConfigLoader.LoadOrCreateDefault()` returns populated object.

---

## Story 1.1.3 — Core Value Types

**Files to create:**
```
WorldEngine.Sim/Core/TileCoord.cs      # TileCoord record struct
WorldEngine.Sim/Core/EntityId.cs       # EntityId + IdGenerator
WorldEngine.Sim/Core/EventId.cs        # EventId
WorldEngine.Sim/Core/CivId.cs          # CivId
WorldEngine.Sim/Core/ModifierId.cs     # ModifierId
WorldEngine.Sim/Core/ArtifactId.cs     # ArtifactId
WorldEngine.Sim/Core/WorldRng.cs       # WorldRng static class
WorldEngine.Sim/Core/DisasterSalts.cs  # salt constants
WorldEngine.Sim/Core/WorldConfig.cs    # WorldConfig (km-based)
```

**TileCoord must:**
- Wrap X East-West (via a `Wrap(int width)` method — NOT auto-wrapping in constructor)
- Have `North()`, `South()`, `East(int width)`, `West(int width)` helpers
- Have `ChebyshevDistance(TileCoord other)` for radius queries
- Be a `readonly record struct` (value equality, stack-allocated)

**WorldRng must:**
- Use `System.IO.Hashing.XxHash32` (built-in, no package needed)
- Signature: `public static float FloatAt(int worldSeed, long tick, int x, int y, int salt)`
- Signature: `public static int IntAt(int worldSeed, long tick, int x, int y, int min, int max, int salt)`
- Two calls with the same arguments must return the same result
- Two calls with different salts should return statistically independent results

**WorldConfig:**
```csharp
public sealed class WorldConfig
{
    public int Seed { get; init; }
    public int WidthKm { get; init; } = 4000;
    public int HeightKm { get; init; } = 3000;
    public int TileWidthKm { get; init; } = 10;
    // Derived:
    public int TileWidth => WidthKm / TileWidthKm;
    public int TileHeight => HeightKm / TileWidthKm;
}
```

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/CoreValueTypeTests.cs`):
```
TileCoord_EastWestWrapsAt0                     # Wrap: X=-1, width=400 → X=399
TileCoord_EastWestWrapsAtMax                   # Wrap: X=400, width=400 → X=0
TileCoord_ChebyshevDistanceIsSymmetric         # dist(A,B) == dist(B,A)
TileCoord_CardinalNeighborsReturnFourCoords     # North/South/East/West exist
TileCoord_RecordEqualityByValue                # two TileCoords with same X,Y are equal
EntityId_NewIsUnique                           # EntityId.New() != EntityId.New()
WorldRng_DeterministicSameInputs               # two calls, same args → same float
WorldRng_DifferentSaltsDifferentOutputs        # salt=0 vs salt=1 → different float
WorldRng_DifferentCoordsDifferentOutputs       # x=0,y=0 vs x=1,y=0 → different float
WorldRng_OutputInRange0To1                     # all returned floats in [0, 1)
WorldConfig_TileCountsDerivedFromKm            # 4000km / 10km = 400 tiles
```

**Done when:** Tests pass.

---

## Story 1.1.4 — Logging + Headless Entry Point

**Files to create:**
```
WorldEngine.Sim/Program.cs    # headless entry point for --seed and --years args
```

**No TDD for plumbing.** Add `Microsoft.Extensions.Logging` package to Sim.

Program.cs should parse `--seed <int>` and `--years <int>`, log start/done, exit 0. It will call WorldGenPipeline once implemented but for now just logs args received.

**Done when:** `dotnet run --project WorldEngine.Sim -- --seed 42 --years 100` exits 0.

---

## Story 1.1.5 — Test Suite Structure

**Create folder structure in Tests:**
```
WorldEngine.Tests/
  Unit/               # per-class unit tests
  Integration/        # end-to-end workflow tests
  Reproducibility/    # SameSeedProducesSameWorld lives here
  Helpers/
    TestSimConfig.cs  # (from snippets/test_templates.md)
```

The `SameSeedProducesSameWorld` test goes in `Reproducibility/` but is STUBBED (always passes) until story 1.3.8. Add it with `[Fact(Skip = "Implement after story 1.3.8")]`.

**Done when:** `dotnet test` exits 0 (stub test passes, no failures).

---

## Phase 1 Done Criteria

- `dotnet build` — 0 warnings, 0 errors, all three projects
- `dotnet test` — all tests pass
- `SimConfigLoader.LoadOrCreateDefault()` works
- All core value types implemented and tested
- `WorldRng` determinism verified by test
- Phase 2 can start without any blockers from Phase 1
