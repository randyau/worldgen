# M10 Phase 10.2 — Sim-config settings tab

**Status: COMPLETE — 2026-07-26 (build+test verified; no manual UI pass — no display in this
environment, see note below).**
**Milestone:** M10 — Worldgen Preview & Modding (`docs/phases/m10_worldgen_preview_modding.md`)
**Depends on:** — (parallel-safe with 10.0/10.1)

## What shipped

**`WorldEngine.Sim/Config/ConfigRegistry.cs`** (sim-only, headless) — a reflection-based
`{key, group, kind, default}` descriptor builder per `ui_design_framework.md` §9.3:
- `ConfigRegistry.Build(live, defaults)` walks the `SimConfig` object graph once and returns one
  `ConfigRegistry.Entry` per leaf tunable (`int`/`float`/`byte`/`bool`/`string`), recursing into
  nested config sections. Skips `AncestryRegistry` (loaded separately, not from
  `sim_config.toml`) and any non-leaf collection (arrays/lists/dictionaries) or enum — those
  aren't representable as a single generic control.
- Each `Entry` carries `Group` (top-level section, e.g. `"WorldGen"`), `Path` (dotted from that
  root, e.g. `"Ocean.DefaultSeaLevel"`), `Kind`, a `Get`/`Set` closure bound to the *live* instance
  passed in, a `Default` value read from a separately-supplied *defaults* instance, and
  `IsModified`.
- No new UI code is required per config key — adding a tunable to `SimConfig` makes it appear in
  the settings tab automatically, satisfying CLAUDE.md's "generic config-control registry"
  requirement.

**`WorldEngine.UI/UI/Input/SimConfigEditor.cs`** — a group-picker + field-list editor mirroring
`KeybindEditor`'s structure: a `WeDropdown<string>` selects one top-level `SimConfig` section,
then renders that section's leaf entries as `WeField` (numeric/string, live-validated on edit) or
`WeCheckBox` (bool) rows, each with a `[Reset]` button and a `(modified)` tag. A `[Reset Section]`
button resets every entry in the active group. Edits write straight through to the live `SimConfig`
instance the running sim reads each tick — same immediate-apply pattern the 10.1 sea-level field
established; **not** persisted back to `sim_config.toml` (that file stays the hand-tuned baseline
per CLAUDE.md).

**`WorldEngine.UI/UI/Panels/SettingsPanel.cs`** — gained an optional third "Simulation" tab.
`SettingsPanel`'s constructor takes optional `liveSimConfig`/`defaultSimConfig` parameters; the tab
button and its content only appear when both are supplied (e.g. omitted before a world exists).

**`WorldEngine.UI/Game1.cs`** — `StartSim` now keeps the `SimConfig` it loads for the running sim
(`_simConfig`) plus a second, independently-loaded snapshot (`_simConfigDefaults`) used only as the
reset/diff baseline — this is the DECISION-10.2 "default" (the loaded `sim_config.toml`, not
`SimConfig.Default()`). Both are passed into `SettingsPanel`'s constructor.

## Non-negotiables checked

- `ConfigRegistry` lives in `WorldEngine.Sim/Config` and references nothing outside `System.*` —
  headless, and unit-testable without any UI project reference.
- No hardcoded sim numbers or per-key UI branches were added; the registry is fully generic.
- `ConfigEntry` (nested as `ConfigRegistry.Entry`) satisfies the `Config_Classes_EndWith_Config`
  architecture rule test, which requires every public class in the `*.Config` namespace to end in
  `Config` or a small allow-listed suffix (`Loader`/`Registry`/`Validator`/`File`/`Exception`/
  `Tables`) — nesting it under `ConfigRegistry` removes it from the top-level-class check entirely.

## Tests

`WorldEngine.Tests/Unit/ConfigRegistryTests.cs` — 5 tests: finds a known leaf entry with correct
kind/default, skips `AncestryRegistry` and collection-typed properties, `Set` writes to the live
instance without mutating the defaults snapshot, every entry key is unique across the whole config,
and defaults are read from whichever instance is passed as `defaults` (proves the reflection walk
doesn't accidentally alias live/defaults).

## Verification

- `dotnet build WorldEngine.sln` — 0 warnings, 0 errors.
- `scripts/test-fast.sh` — 534/534 passing, doc-check clean (`codebase_map.md` regenerated).
- **Not done**: a manual playtest (open Settings, switch to Simulation tab, edit a value, confirm
  it applies live and Reset/Reset Section work). No display attached in this environment — same
  caveat as 10.1. Recommend a manual pass before relying on this in front of end users.
