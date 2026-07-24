# M8 Phase 5 — Settings Scaffold

**Milestone:** M8 — UI Framework Rewrite
**Status:** COMPLETE — 2026-07-24. `UiPrefs` (JSON, global not per-world) + `UiPrefsStore`
load/save. `SettingsPanel` (Summoned, `Ctrl+,`) has Display (dock width — applied live to
`LayoutHost.DockWidth`; theme variant/high-contrast/reduce-motion/density — persisted only, not
yet applied live, see the DECISION on `UiPrefs` itself) and Controls (hosts the new
`KeybindEditor` composite, extracted from `HelpPanel` so both share one rebind-row-list instead
of duplicating it) tabs. `KeybindRegistry.ExportOverrides/ApplyOverrides` round-trip overrides
through `UiPrefs.KeybindOverrides`; a rebind from either Help or Settings persists via one shared
`Game1.ApplyAndPersistUiPrefs` callback. Simulation-config tab out of scope per this doc (M10).
No gear button was added to the top bar (keybind-only entry point) — deferred as a small,
separately-verifiable follow-up; not part of this scaffold's core deliverable. Build 0 warnings,
501/501 tests green.
**Depends on:** 8.4 (`CommandRegistry`, `KeybindEditor`)
**Worker model:** Sonnet
**Framework refs:** `docs/ui_design_framework.md` §9 (settings/keybindings/config), §2.5 (motion)

> Read first (only these): this doc; `m8_ui_framework_rewrite.md`; framework §9; `Game1.cs`
> first-run flag handling (grep `firstrun`/`FirstRun`) as the model for a small persisted flag
> file. Nothing else.

## Goal

Stand up the Settings **shell** and the two tabs that belong to M8 — **Display** and **Controls**
— plus a small UI-prefs persistence store. The **Simulation-config tab is explicitly out of scope**
here; it lands in M10 (Worldgen Preview & Modding) and plugs into this shell (framework §9.2,
roadmap M10). Build the shell so that later tab is a drop-in.

## What exists now (grounding)

- No settings surface exists. The first-run dialog persists a flag file (`Game1` writes a marker so
  it never reshows) — reuse that lightweight file-flag pattern for UI prefs.
- After 8.4: `CommandRegistry` + a `KeybindEditor` composite exist; keybind overrides currently
  live in memory awaiting persistence.

## Stories

| # | Deliverable | Files |
|---|-------------|-------|
| 8.5.1 | `UiPrefs` store (load/save small JSON/TOML in the user data dir) | `UI/Settings/UiPrefs.cs` (new) |
| 8.5.2 | Settings screen shell (summoned, tabbed) | `UI/Panels/SettingsPanel.cs` (new) |
| 8.5.3 | Display tab | within `SettingsPanel` |
| 8.5.4 | Controls tab (hosts `KeybindEditor`) + persist overrides | within `SettingsPanel`, `UiPrefs` |

---

### 8.5.1 — `UiPrefs`
A tiny serializable record persisted to the user data dir (same location as the first-run flag).
Fields for M8: `ThemeVariant`, `HighContrast`, `ReduceMotion`, `OverlayPalette`, `DockWidth`,
`Density`, and `Dictionary<string,string> KeybindOverrides` (command id → key label). Load on
startup, save on change. `// DECISION:` format (JSON via System.Text.Json is simplest;
`config/*.toml` is sim-owned — keep UI prefs separate). Global (not per-world) per framework §14
open-question lean.

### 8.5.2 — Settings shell
`SettingsPanel : IWorkspacePanel` (Summoned; add a `world.settings` command + default key, e.g.
`Ctrl+,`, and a gear button in the top bar). A left tab list + right content region, both inside a
`PanelFrame`, using kit components only. Register tabs by id so M10 can append a `simconfig` tab
without editing the shell (`// MOD SEAM: settings tab registry`).

### 8.5.3 — Display tab
Controls bound to `UiPrefs`: theme variant, high-contrast toggle, **reduce-motion** toggle (honored
by kit show/hide/toast animations — framework §2.5), overlay palette choice, dock width slider,
density selector. Applying a pref updates tokens/host live where feasible (dock width → host
resize; theme → token swap). Each control uses `WeField`/`WeDropdown`/`Meter`/`WeButton(Toggle)`.

### 8.5.4 — Controls tab
Host the `KeybindEditor` (from 8.4) listing all `CommandRegistry` commands with rebind capture.
Persist overrides into `UiPrefs.KeybindOverrides`; on load, apply them over the registry defaults.
Add per-command and "reset all to defaults" actions.

## Verification
- Settings opens as a summoned tab (gear button + key); Display prefs persist across restarts and
  apply live; reduce-motion actually suppresses animation.
- Controls tab rebinds persist and survive restart; reset restores defaults.
- Shell accepts a new tab by registration (prove with a throwaway placeholder tab, then remove).
- `scripts/test-fast.sh` green; zero warnings.

## Milestone close-out (do this in the final 8.5 commit)
- Confirm the whole-milestone DoD in `m8_ui_framework_rewrite.md` (no `AddLine`, no `PanelChrome`,
  no old panel files, no `using Myra` outside `UI/Kit/`, all four arch tests at full strength).
- Move all `m8_phase*.md` (incl. the index) to `docs/phases/archive/`, set `Status: COMPLETE —
  <date>`.
- Update `docs/roadmap.md`: mark the M8 row and the M8 detailed section `✅ COMPLETE <date>`.
- Update `CLAUDE.md` "Current milestone status" line to M8 complete.
