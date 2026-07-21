# M6 Phase 1 — UI Foundation

**Milestone:** M6 — UI Experience & Polish
**Status:** IN PROGRESS — started 2026-07-21
**Roadmap:** `docs/roadmap.md` § "M6 — UI Experience & Polish" (Epics 6.1, 6.2.1)

## Goal

Build the UI substrate every later M6 epic depends on. No new sim systems — a UI refactor
over `WorldEngine.UI`. Per direction, the **visible UI is the primary interaction path**;
keybinds are accelerators feeding the same command flow as the buttons, not a parallel path.

Constraints (CLAUDE.md): `WorldEngine.UI` stays `WorldSnapshot`-only; sim mutation flows
through `ICommand` → `CommandResolver`; no hardcoded sim constants; zero warnings;
`scripts/test-fast.sh` green (incl. 6 architecture rule tests).

## Stories

| # | Roadmap | Deliverable | State |
|---|---------|-------------|-------|
| 1 | 6.2.1 | `UiTheme` tokens + `PanelChrome` helper; panels off inline literals | DONE |
| 2 | 6.1.3 | `KeybindRegistry` (single source of truth) + `HelpOverlayPanel` | DONE |
| 3 | 6.1.4 | UI-side `SelectionState` + `SelectionRouter` | DONE |
| 4 | 6.1.1 | `OverlayBar` with active-state highlight | DONE |
| 5 | 6.1.2 | `PanelManager` + visible toggle bar | pending |

Deferred to later M6 phases: 6.2.2–6.2.4, all 6.3, all 6.4.

## Key decisions

- **DS:** Design tokens as a static C# class (`UiTheme`) rather than a Myra `Stylesheet`
  asset — panels are built programmatically and `Content/` is empty, so a stylesheet buys
  little now. One retune point.
- **DS:** UI-side selection (`SelectionState`) drives panel routing and is *not* sim state.
  Tile inspection stays a sim command (`SetInspectedTile`) because the snapshot must carry
  `InspectedTile` detail; higher-level "what is selected" is UI-only, no determinism impact.
- **DS:** `CivColor(long)` derivation moves from `TileMapRenderer` into `UiTheme` so panels
  and the territory overlay share one deterministic hue mapping.

## Verification

- `scripts/test-fast.sh` green after each story (arch tests + `doc-check.py`).
- No new `WorldEngine.Sim` → `WorldEngine.UI` reference; UI reads only `WorldSnapshot`.
- Manual run: overlay bar switches + highlights active; keys still work and match bar; panel
  toggle bar matches H/W; "?" help lists all bindings; tile/name clicks route to right panel;
  New World (N) resets cleanly with panels restored; zero build warnings.
