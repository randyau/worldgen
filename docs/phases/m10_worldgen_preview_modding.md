# M10 — Worldgen Preview & Modding (index)

**Milestone:** M10 — Worldgen Preview & Modding
**Status:** SCOPED — 2026-07-26. No phase started yet.
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
| 10.0 | — | `WorldGenPipeline.RunUpTo(layer)` / `RerunFrom(layer, ctx)`: re-enter the existing 9-layer chain (Tectonic→Poi) at any point without regenerating earlier layers; `WorldGenContext` becomes replayable/resumable. Sim-only, headless, no UI. |
| 10.1 | 10.0 | Worldgen preview screen: per-layer thumbnail overlays, parameter fields (sea level, etc.), "rerun from layer," layer-status list, Commit — built on the M8 component kit per `ui_design_framework.md` §11. |
| 10.2 | — (parallel-safe with 10.0/10.1) | Sim-config settings tab: generic `{key, group, kind, range, default}` config-control registry rendering `sim_config.toml` groups as `WeField`/`Meter`/`WeDropdown`, with per-setting/per-group reset-to-default and diff-from-default (`ui_design_framework.md` §9.2–9.3). Presets ("High conflict," "Peaceful," "Dense population") layer on top once the registry exists. |
| 10.3 | — (parallel-safe) | Data modding: document the moddable data files, add load-time validation (schema/range checks, clear error messages), wire through the existing `PanelRegistry`/`OverlayRegistry`/`Presenter`/token-set seams from M8 §10. No new plugin/scripting surface. |

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

## Open questions to resolve at each phase's start (not pre-decided here)

- **10.0:** does `RerunFrom` need to invalidate/recompute downstream `WorldGenContext` fields
  automatically, or does the caller pass an explicit "these layers are now stale" list? Affects
  whether this is a small targeted change or a bigger context-dependency-tracking feature.
- **10.1:** are per-layer preview thumbnails rendered from existing overlay/color logic
  (`OverlayRegistry`) or do they need their own lightweight raster path? Reuse is strongly
  preferred (§10 coherence-first rule).
- **10.2:** does "diff-from-default" compare against `SimConfig`'s hardcoded defaults or against
  the shipped `sim_config.toml`? These can diverge if the TOML has been hand-tuned past the
  in-code defaults — needs a decision before the diff view is built.
- **10.3:** which data files are actually moddable today (ancestries, names, biomes, resources per
  the roadmap line) vs. which are structural code data (e.g. `CreatedGoodTaxonomy`'s category
  tables, explicitly marked non-tunable in M9) — enumerate the exact file list before writing
  validation.
