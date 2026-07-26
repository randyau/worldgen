# M10 Phase 10.1 — Worldgen preview screen

**Status: COMPLETE — 2026-07-26 (build+test verified; no manual UI pass — no display in this
environment, see note below).**
**Milestone:** M10 — Worldgen Preview & Modding (`docs/phases/m10_worldgen_preview_modding.md`)
**Depends on:** 10.0 (`docs/phases/archive/m10_phase0_pipeline_resume.md`)

## What shipped

`WorldEngine.UI/UI/WorldGenPreviewScreen.cs` replaces `WorldGenScreen` (deleted) as the
pre-sim screen shown from `Game1`:

- **Progress panel** — same role as before (header, progress bar, per-layer status text) while
  the initial `WorldGenPipeline.RunUpToAsync(config, simConfig, LayerCount - 1)` runs in the
  background.
- **Preview panel** (shown once the initial run completes) — per the M10 index doc's design
  decisions:
  - **Layer-status list**: one row per layer (`WorldGenPipeline.LayerNames`), each with a
    thumbnail slot and a Pending/Done label.
  - **Per-layer thumbnails**: `WorldEngine.UI/Rendering/WorldGenPreviewRenderer.cs` builds a
    `Color[]` per layer straight from the in-progress `WorldGenContext`, reusing
    `OverlayRenderer.GetColor` with a minimal `TileDisplayData` built via `with` expressions
    (Elevation/Magic/Temperature/Biome layers feed their one relevant field; Ocean/River/Poi
    substitute a placeholder `BiomeType` — Ocean/CoastalWater/Beach/Plains — so they still route
    through the real biome palette instead of a forked one; Resource reuses the real
    `HasDeposit`/`HasRareResource` flag derivation from `TileGridAssembler`; only Tectonic (plate
    id, no existing `OverlayType`) gets a small standalone palette). Rendered as a `Texture2D` per
    layer (`SetData` once per (re)generation, not per frame) and drawn via `SpriteBatch` in
    `Game1.Draw`, at the Myra-arranged thumbnail slot's `Bounds` — the same pattern `TimelineBar`
    already uses for non-Myra raster content.
  - **Parameter field**: sea level (`SimConfig.WorldGen.Ocean.DefaultSeaLevel`), pre-filled from
    the loaded config; edits apply to the shared `WorldGenContext.SimConfig` on Rerun.
  - **"Rerun from layer"**: a layer dropdown + Rerun button call
    `WorldGenPipeline.RerunFromAsync(ctx, layerIndex)`; affected rows flip to Pending, then back to
    Done with rebuilt thumbnails once the background task completes.
  - **Commit**: assembles the final `WorldState` via `TileGridAssembler.Assemble(ctx)` and hands it
    back to `Game1`, which starts the sim exactly as the old "Start Simulation" button did.

`Game1.cs` changes: `GenerateWorld` (the hand-rolled duplicate of the 9-layer loop) is deleted —
world generation now goes through the real `WorldGenPipeline` (10.0) instead of a second,
divergence-prone copy of the layer sequence. `StartNewWorldGen` calls
`WorldGenPreviewScreen.BeginGeneration`; the resume-from-save path calls the new `ShowMessage`
helper instead of overloading the old `Update(string, float)` signature.

## Non-negotiables checked

- Sim-only pipeline change stayed in 10.0; this phase is `WorldEngine.UI`-only.
- No hardcoded sim numbers added — sea level continues to come from `SimConfig`.
- Interaction-state vs. displayed-data split (Mandatory Pattern #4) doesn't apply here in the
  usual sense (no live tick cadence pre-sim), but button enable/disable state updates
  synchronously with each click, not gated behind any tick.

## Verification

- `dotnet build WorldEngine.sln` — 0 warnings, 0 errors.
- `scripts/test-fast.sh` — 529/529 passing, doc-check clean (`codebase_map.md` regenerated).
- **Not done**: a manual playtest of the screen. This environment has no display attached to run
  the MonoGame app; per the M8 close-out precedent, this is build+test-verified only. Recommend a
  manual pass (generate a world, exercise Rerun on a couple of layers, confirm thumbnails update
  and Commit still starts the sim) before relying on this in front of end users.
