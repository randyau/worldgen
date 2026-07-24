# M8 Phase 3 — Panel Migration

**Milestone:** M8 — UI Framework Rewrite
**Status:** NOT STARTED
**Depends on:** 8.2 (bus, context, kit). Story 8.3.6's Help sub-part also depends on 8.4.
**Worker model:** Haiku-friendly per story (mechanical rebuilds) — escalate to Sonnet if a
worker stalls on restore or a panel has non-trivial data shaping. **One panel per story = one
commit.**
**Framework refs:** `docs/ui_design_framework.md` §4 (components), §8 (info display), §11 (per-surface table)

> Read first (only these): this doc; `m8_ui_framework_rewrite.md`; framework §8 + the relevant
> row of §11 for your panel; the **one** old panel file you're migrating; the kit files in
> `UI/Kit/` and `UI/Present/Presenter.cs`. Do not read the other panels.

## Goal

Rebuild each panel's internals against the component kit + Presenter, moving them into
`UI/Panels/` as `IWorkspacePanel` implementations, and delete the old file + its `LegacyPanelAdapter`
registration. After a story: that panel has no `AddLine(string)`, no raw Myra, no color/size
literals, no inline formatting; all names are `EntityLink`s; empty states use `EmptyState`.

Stories are independent (each touches one panel) but all sit on the 8.0–8.2 foundation. They can
be assigned to different workers in parallel *after* 8.2 merges, as long as each rebases on the
latest main before finishing.

## Per-story recipe (applies to all 8.3.x)

1. Create `UI/Panels/<Name>Panel.cs` implementing `IWorkspacePanel` (§6 contract). `Build()`
   returns a `PanelFrame`; `Refresh()` rebuilds the body from `PanelContext`.
2. Replace every `AddLine("--- X ---")` → `SectionHeader`; every `AddLine("k: v")` →
   `StatRow`/`KeyValueGrid`; every bar/percent → `Meter`; every name → `EntityLink`.
3. Replace all inline formatting (temps, wellbeing, health, stores, enum strings) with
   `ctx.Present.*` calls.
4. Add the panel's empty state(s) via `EmptyFor(ctx)`.
5. Register it with `SimWorkspace` in place of the legacy adapter; delete the old panel file and
   its adapter wiring in `Game1`.
6. `scripts/test-fast.sh` green; manual check the panel renders and links route.

## Stories

| # | Panel | Old file → new | Notes |
|---|-------|----------------|-------|
| 8.3.1 | Tile Inspector | `TileInspectorPanel.cs` → `UI/Panels/TileInspectorPanel.cs` | Flagship migration; densest. |
| 8.3.2 | Character (unify Watch + Profile) | `CharacterWatchPanel.cs` + `CharacterProfilePanel.cs` → `UI/Panels/CharacterPanel.cs` | Two panels → one, Live/History sub-views. |
| 8.3.3 | Civ History | `CivHistoryPanel.cs` → `UI/Panels/CivHistoryPanel.cs` | Sortable selector; trait chips. |
| 8.3.4 | Event Log (+ filter) | `EventLogPanel.cs` + `FilterPanel.cs` → `UI/Panels/EventLogPanel.cs` | Virtualized `WeList<EventRow>`; filter as collapsible section. |
| 8.3.5 | God Mode + 4 dialogs | `GodModePanel.cs` → `UI/Panels/GodModePanel.cs` | Dialogs via `ModalHost`; `AccentGodMode` `ModeBanner`. |
| 8.3.6 | Timeline + Legends + Toasts + map tooltips + Help | `TimelineBar.cs`, `HelpOverlayPanel.cs`, `OverlayBar.cs` | Help sub-part needs 8.4. |

---

### 8.3.1 — Tile Inspector
Rebuild the `Update(TileInspectorData?, WorldSnapshot?)` body: identity-first ordering (ruin →
settlement identity/health → tile facts → seasonal → resources/disasters → territory → characters
→ artifacts → history). Use `KeyValueGrid` for tile facts, `SeasonalStrip` for the 4-season deltas
(add this Layer-2 composite here — framework §4.2), `Meter` for settlement health, `EntityLink` for
civ/character/artifact names, `Present.TempC/Elevation/Store/...` for all numbers. Add the map
highlight-ring signal for the inspected tile (emit a UI flag the map renderer reads; `// DECISION:`
where). Empty state: none (only shown when a tile is selected).

### 8.3.2 — Character (unify Watch + Profile)
One `CharacterPanel` with two sub-views toggled by a segmented control: **Live** (needs/goals/
traits via `Meter`s, spotlight controls, `AccentSpotlight` `ModeBanner` when spotlighted) and
**History** (lifespan, life events as `EventLink`s, relationships as `EntityChip`s). This resolves
the inventory's "Watch and Profile are separate" gap. Spotlight intent buttons enqueue commands via
`ctx.Commands` (per 8.2.3). The permanently-disabled "Generate Narrative" stub: keep as a disabled
`WeButton` with a tooltip "V2 feature" (or drop — `// DECISION:`). Empty state: `NotBuiltYet`-style
if the character has no history rows.

### 8.3.3 — Civ History
`WeDropdown` civ selector with sorting (by status, founding year, size — `// DECISION:` default
sort). Cultural traits → `TagChip`s with meaning tooltips (`Present` supplies the explanation
text). Rulers/wars/major-events lists → `EntityLink`/`EventLink`; `Present` turns type strings into
prose. Stale-summary case → `EmptyState(NotBuiltYet)` showing the next rebuild year (summaries
rebuild every 50 years).

### 8.3.4 — Event Log (+ filter)
This panel is pinned by default. Body = **virtualized** `WeList<EventRow>` (implement virtualization
in `WeList` now — framework §4.1 `// PERF` note; needed for M11 scale). `EventRow` composite
(framework §4.2): tier stripe, `[G]` badge, `Present.YearSeason`, `Present` event text, `EntityLink`
actor/civ, `->` cause (→ `ModalHost` via command), `★` first-of-kind. Filter panel becomes a
collapsible section inside this `PanelFrame` (not a separate panel). Honor `FocusLensState` (dim
non-matching). Empty states: `PreSim` ("no events yet"), `FilteredEmpty` ("no events match" +
Clear). Note the DB full-history search is a *future* enhancement (M11) — keep the ring-buffer
source, just make the list virtual.

### 8.3.5 — God Mode + dialogs
`GodModePanel` = summoned tab, `AccentGodMode` `ModeBanner`, pause-gated via the **shared** pause
gate (framework §7.6 — read pause state from the workspace, don't re-implement `CheckPaused` per
dialog). The four dialogs (Place Artifact / Trigger Disaster / Spawn Character / Nudge) rebuild via
`ModalHost` + `WeDropdown`/`WeField`; on confirm show a plain-language summary, enqueue the existing
`Author*` command via `ctx.Commands`, and fire a `Toast` ("Artifact placed at (12,34)"). Keep the
`[G]` provenance flow. Empty/disabled state when running: buttons disabled with a "Pause to author"
hint (not a warn-on-click).

### 8.3.6 — Timeline, Legends, Toasts, map tooltips, Help
- **Timeline**: rebuild `TimelineBar` as the `Timeline` component in the Timeline region:
  density heatmap, headline pips, century ticks, viewport-clamped scrub tooltip (fixes the
  hardcoded-position overlap), larger hit target, resolution-adaptive stub for M11 (`// PERF:
  coarsen to decade/century at 10k-yr scale`).
- **Legends**: add the missing per-overlay `Legend` (Float region, non-interactive). Source the
  ramp/labels from an `OverlayRegistry` (`// MOD SEAM:`) keyed by `OverlayType`.
- **Toasts**: `Toast` host in the Transient band (bottom-right, auto-dismiss, stack).
- **Map marker tooltips**: attach `Tooltip` to markers; hover highlight.
- **Help** (needs 8.4): rebuild `HelpOverlayPanel` to render from `CommandRegistry` (grouped,
  searchable). If 8.4 isn't merged when you reach this, split Help into a follow-up commit after
  8.4 and land the rest of 8.3.6 first.

## Verification (per story + phase)
- Migrated panel: no `AddLine`, no `using Myra`, no color/pixel literals, no inline formatting
  (`git grep` the new file). Links route via the bus. Empty state renders.
- Phase end: `PanelChrome.cs` deleted; all old panel files + `LegacyPanelAdapter` deleted; the
  four new arch tests (`NoMyraOutsideKit`, `NoColorLiteralsInPanels`, `PanelsImplementContract`,
  `PanelsSetNoAbsoluteGeometry`) enabled at full strength over `UI/Panels/` and green.
- `scripts/test-fast.sh` green; zero warnings; full manual bug sweep from 8.1 still passes.

## Handoff to 8.4/8.5
After 8.3, all user-facing surfaces are on the kit. 8.4 formalizes actions/keybinds; 8.5 adds the
Settings shell. If 8.3.6's Help was deferred, do it immediately after 8.4 merges.
