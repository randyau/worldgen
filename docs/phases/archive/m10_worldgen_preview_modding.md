# M10 — Worldgen Preview & Modding (index)

**Milestone:** M10 — Worldgen Preview & Modding
**Status:** COMPLETE — 2026-07-26. Open design questions resolved 2026-07-26 (see "Design
decisions" below). Phase 10.0 (pipeline resume/replay) done 2026-07-26 — see
`docs/phases/archive/m10_phase0_pipeline_resume.md`. Phase 10.1 (preview screen) done
2026-07-26, build+test verified (no manual UI pass — no display in this environment) — see
`docs/phases/archive/m10_phase1_worldgen_preview_screen.md`. Phase 10.2 (sim-config settings tab)
done 2026-07-26, build+test verified (no manual UI pass) — see
`docs/phases/archive/m10_phase2_sim_config_settings_tab.md`. Phase 10.3 (data modding) done
2026-07-26, build+test verified — see `docs/phases/archive/m10_phase3_data_modding.md`. All M10
phases complete.
**Design authority:** `docs/ui_design_framework.md` §9 (Settings screen home) and §11 ("Worldgen
screen" row) for the UI shape; §10 (modding seams) for the data-modding approach.
**Roadmap:** `docs/roadmap.md` § "M10".

> Every M10 worker reads this file first, then only their phase doc.

---

## What this milestone is

Three roadmap items, in dependency order:
1. **Layered worldgen preview + adjustment** — the `WorldGenPipeline.RunUpTo`/`RerunFrom`
   capability M1 deferred, plus a UI to drive it.
2. **Player config exposure** — surface `sim_config.toml` tunables generically as the
   "Simulation config" tab in the M8 Settings shell (`docs/ui_design_framework.md` §9.2).
3. **Data modding** — documented, validated, moddable data files (ancestries, names, biomes,
   resources), using the M8 `// MOD SEAM:` registries as extension points. No plugin/code modding
   (out of scope per `CLAUDE.md`).

(2) and (3) don't depend on (1) and could run in parallel with it, but (1) is the largest, riskiest
piece and should land first so the Settings-shell work has a stable pipeline API to bind against.

## Phase sequence

| Phase | Depends on | One-line deliverable |
|-------|-----------|----------------------|
| 10.0 | — | DONE. `WorldGenPipeline.RunUpToAsync(layer)` / `RerunFromAsync(ctx, layer)`: re-enter the existing 9-layer chain (Tectonic→Poi) at any point without regenerating earlier layers; `WorldGenContext` becomes replayable/resumable. Sim-only, headless, no UI. |
| 10.1 | 10.0 | DONE. Worldgen preview screen: per-layer thumbnail overlays, parameter fields (sea level, etc.), "rerun from layer," layer-status list, Commit — built on the M8 component kit per `ui_design_framework.md` §11. |
| 10.2 | — (parallel-safe with 10.0/10.1) | DONE. `ConfigRegistry` (`WorldEngine.Sim/Config`): generic `{key, group, kind, default}` config-control registry over `SimConfig`, reflected once, no per-key UI code. `SimConfigEditor` renders it as `WeField`/`WeCheckBox` rows grouped by section, with per-setting/per-group reset-to-default and a `(modified)` diff tag, hosted as a new "Simulation" tab on `SettingsPanel`. Presets deferred — not built this phase (no consumer yet beyond the raw registry). |
| 10.3 | — (parallel-safe) | DONE. `AncestryValidator`/`BeastCatalogValidator` (`WorldEngine.Sim`): load-time schema/range/cross-reference validation for `ancestries.toml`/`beasts.toml`, mirroring `SimConfigValidator`'s collect-all-then-throw pattern. `docs/modding.md` documents all three moddable TOML files. No new plugin/scripting surface — biomes/resources remain out of scope. |

Do not start a phase until its dependencies are merged and green (`scripts/test-fast.sh`).

## Non-negotiable constraints (every phase)

From `CLAUDE.md`:
1. `WorldEngine.Sim` stays headless; `WorldGenPipeline` changes (10.0) touch only `WorldEngine.Sim`.
2. All new tunables go in `SimConfig`/`sim_config.toml`; the 10.2 registry *reads* that file
   generically rather than hardcoding a UI per key.
3. UI work (10.1, 10.2) reuses the M8 component kit (`docs/ui_design_framework.md`) — no bespoke
   controls; interaction-state vs. displayed-data split (Mandatory Pattern #4) applies as always.
4. Every changed behavior needs a test; the reproducibility test must still pass. 10.0 in
   particular needs a test asserting `RunUpTo(N)` + `RerunFrom(N, ctx)` with unchanged params
   reproduces the same result as `RunFullAsync` (partial-rerun equivalence).
5. Data-modding validation (10.3) is a load-time gate, not a runtime one — invalid mod data should
   fail fast with a clear message, not silently degrade simulation behavior.

## Design decisions (resolved 2026-07-26)

These were flagged as open when this doc was first scoped; resolved now against the actual
codebase rather than left for each phase to rediscover.

### DECISION (10.0): `RerunFrom` invalidates automatically — no caller-supplied stale list

`WorldGenContext` (`WorldEngine.Sim/WorldGen/WorldGenContext.cs`) is a strictly linear
accumulator: `Tectonic → Elevation → Ocean → River → Magic → Climate → Biome → Resource → Poi`,
each layer reading only completed predecessors (per its own doc comment — "never from layers that
haven't run yet"). There is no diamond dependency graph to track. `RerunFrom(layerIndex, ctx)`
therefore just nulls out every result field from `layerIndex` onward and re-invokes
`WorldGenPipeline`'s existing loop starting there — a mechanical slice of `RunFullAsync`, not a new
dependency-tracking feature. If a future layer ever reads a non-adjacent predecessor, revisit.

### DECISION (10.1): preview thumbnails reuse `OverlayRenderer`, not a new raster path

`WorldEngine.UI/Rendering/OverlayRenderer.GetColor` already maps biome/elevation/temperature/
moisture/resource/magic values to `Color` per tile, keyed by `OverlayType` — exactly the palette
logic a layer-preview thumbnail needs. It currently takes `TileDisplayData` (post-assembly), while
preview needs to render straight from in-progress `WorldGenContext` layer results (e.g.
`ElevationResult` before `Ocean`/`Biome` exist). 10.1 adds small adapters that build a minimal
per-layer color input from whatever `WorldGenContext` fields are populated so far and calls the
same `GetColor`-style helpers — reuse the palette functions, don't fork them, and don't invent a
second rendering path (coherence-first, framework §10).

### DECISION (10.2): "default" for diff/reset means the shipped `sim_config.toml`, not the C# property initializers

`SimConfigLoader.LoadFromToml` deserializes TOML values directly onto `SimConfig` properties
(`Toml.ToModel<SimConfig>`); the C# property initializers are only a fallback for keys absent from
the file, not the intended "factory" values. `sim_config.toml` is the actual tuned baseline —
it's what the balance sweep (`scripts/test-balance.sh`) validates against, and it's expected to
drift from bare `new SimConfig()` over time as knobs get tuned. "Reset to default" / "diff from
default" must therefore snapshot the loaded config immediately after `SimConfigLoader.Load()` at
startup and diff/reset against *that* snapshot — not against `SimConfig.Default()`. This also
composes correctly with the existing profile system (`config/profiles/`): the snapshot is
post-profile-merge, so "default" means "what this session actually started with."

### DECISION (10.3): only `ancestries.toml` and `beasts.toml` are in scope for 10.3; biomes/resources are a separate, larger lift

Enumerated the actual data files: `config/ancestries.toml` (ancestry definitions, including
first-name lists and `civ_name_suffix` — "names" in the roadmap line isn't a separate file, it's
inside ancestries) and `config/beasts.toml` (species catalog) are already TOML-loaded, data-driven
catalogs (`AncestryLoader`, and the beast-spawn loader) — these are what 10.3 documents and adds
load-time validation to. Biomes (`BiomeType`) and resource type identity are **not** data-driven
today — they're hardcoded C# enums with logic keyed off them throughout the sim (`OverlayRenderer`,
`ResourceDeposit`, etc.), not TOML catalogs. Converting them to moddable data is a real feature
(new schema + loader + every enum-keyed switch statement in the codebase), not a validation pass
on an existing file, and is **out of scope for 10.3** as originally worded. If biome/resource
modding is wanted, it needs its own follow-up phase (10.4+) scoped separately, not folded into this
one. Structural code-data tables that stay non-tunable regardless (e.g. `CreatedGoodTaxonomy`'s
category-weight *structure*, `ArtifactNameGenerator.NounsFor`) are explicitly out of scope per
their existing `// DECISION` comments from M9.
