# M11 Phase 11.0 — Seafaring foundations

**Status:** NOT STARTED.
**Milestone:** M11 — Character Water Crossings (`docs/phases/m11_water_crossings.md`)
**Depends on:** — (first phase)

## Goal

Lay the substrate for water crossings with zero behavior change: a buildable `Port` improvement,
a config section to gate/tune the whole feature, and a way to ask "is this ocean tile crossable."
Nothing moves a character across water yet — that's 11.1.

## Scope

### 1. `Port` improvement type

- Add `Port` to `ImprovementType` (`WorldEngine.Sim/World/TileImprovement.cs`, currently
  `{ Farm, Mine, LoggingCamp, Pasture, Fishery }`).
- `BuildImprovement` resolution (`CivTracker.ResolveBuildImprovement`) must reject building a
  `Port` on a tile that isn't `TileStaticFlags.IsCoastal` — mirror however the other improvement
  types validate their target tile today; add the coastal check as an extra guard specific to
  `Port`.
- `UtilityScorer.BuildCandidates`'s `BuildImprovement` goal branch (the block reading
  `buildGoal.ResourceTag` via `Enum.TryParse<ImpType>`) needs no change — `Port` parses like any
  other improvement name; the coastal gate lives in resolution, not scoring. (If a character with
  a `Port`-tagged `BuildImprovement` goal is standing on a non-coastal tile, the resolver simply
  silently rejects it, same as the existing `GlobalSettlementMinDist` rejection pattern for
  `EstablishSettlement`.)

### 2. `SeafaringConfig`

New `WorldEngine.Sim/Config/SeafaringConfig.cs`, registered as `SimConfig.Seafaring`:

```csharp
public sealed class SeafaringConfig
{
    public bool  OceanCrossingEnabled = true;
    public int   MaxVoyageTiles       = /* TBD in 11.1 once route search is built */;
    public int   PortBuildCostBase    = /* mirror Improvements config's cost knobs */;
}
```

Exact fields depend on what 11.1's route search and build-cost model need — this phase creates the
class and wires it into `SimConfig`/`sim_config.toml` with placeholder-but-reasonable defaults;
11.1 can add fields as needed rather than guessing ahead. Because M10.2's `ConfigRegistry` reflects
`SimConfig` generically, this section appears in the Settings → Simulation tab automatically — no
UI work required here.

### 3. Shallow-ocean classification

Add `bool IsShallowOcean(TileCoord coord)` to `IWorldStateReadOnly`/`WorldState`
(`WorldEngine.Sim/World/WorldState.cs`, alongside the existing `IsLand`): true for a tile whose
biome is `Ocean` or `CoastalWater` *and* has at least one land-tile neighbor (4-directional,
matching the adjacency pattern already used throughout `UtilityScorer`/`EnvironmentalPhase`).
This is the walkable set 11.1's route search will flood-fill over — open ocean with no nearby land
stays impassable, which is what naturally bounds voyages to real straits/channels rather than
letting characters wander into open sea.

`// DECISION:` note the classification choice (adjacency-based, computed on demand vs. a stored
`TileStaticFlags` bit set during worldgen) directly at the method — this is one of the two calls
flagged as an open implementation detail in the index doc.

## Tests

- Unit test: `Port` can be built on a coastal tile, rejected on a non-coastal tile.
- Unit test: `SeafaringConfig` binds from TOML with defaults when absent (mirror the pattern in
  `SimConfigTests` for any other config section).
- Unit test: `IsShallowOcean` true for an ocean/coastal-water tile adjacent to land, false for one
  surrounded entirely by ocean and false for any land tile.
- Architecture tests must still pass unchanged (no new `ICommand`/config-namespace violations).

## Non-negotiables checked

- No behavior change — a world generated before/after this phase produces identical history
  (nothing yet reads `SeafaringConfig` or calls `IsShallowOcean` from behavior code).
- New config lives in `SimConfig`/`sim_config.toml`, not hardcoded.
- `Port` gating logic lives in `WorldEngine.Sim`, no UI reference.
