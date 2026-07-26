# M10 Phase 10.0 — WorldGenPipeline resume/replay

**Status: COMPLETE — 2026-07-26.**
**Milestone:** M10 — Worldgen Preview & Modding (`docs/phases/m10_worldgen_preview_modding.md`)

## What shipped

`WorldEngine.Sim/WorldGen/WorldGenPipeline.cs`:
- `RunUpToAsync(config, simConfig, layerIndex, progress?, ct)` — fresh context, runs layers
  `0..layerIndex` inclusive, returns the in-progress `WorldGenContext` (no assembly).
- `RerunFromAsync(ctx, layerIndex, progress?, ct)` — clears `ctx`'s result fields from
  `layerIndex` onward, re-runs from there to the end of the chain, returns the same `ctx`.
- `RunFullAsync` unchanged in behavior; refactored to share the layer-dispatch loop
  (`RunLayersAsync`) with the two new entry points instead of duplicating the 9-line sequence.
- `LayerCount` (was private) and a new `LayerNames` array are now public, for 10.1's
  layer-status list.

Per the design decision already recorded in the M10 index doc: the chain is strictly linear
(each layer reads only completed predecessors), so `RerunFromAsync` needs no dependency
tracking — it's a mechanical null-and-resume slice of `RunFullAsync`.

Sim-only change (`WorldEngine.Sim`), no UI references.

## Tests

`WorldEngine.Tests/Unit/WorldGenPipelineTests.cs`:
- `RunUpToAsync_PopulatesOnlyLayersThroughIndex` — layers past the cutoff stay null.
- `RunUpToAsync_RejectsOutOfRangeLayerIndex` (`[Theory]` -1 and `LayerCount`).
- `RerunFromAsync_ClearsAndRegeneratesFromRequestedLayerOnward` — rerunning the last layer alone
  reproduces an equal result (deterministic layer, same seed/config).
- `PartialRerun_WithUnchangedParams_ReproducesFullRun` — the required partial-rerun equivalence
  test: `RunUpTo(4)` + `RerunFrom(5)` assembles to a `WorldState` equivalent to `RunFullAsync`.

`scripts/test-fast.sh` green (529 tests, doc-check clean).
