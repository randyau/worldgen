# M8 — UI Framework Rewrite (index)

**Milestone:** M8 — UI Framework Rewrite
**Status:** NOT STARTED
**Design authority:** `docs/ui_design_framework.md` — the *why*. This doc set is the *how*.
**Roadmap:** `docs/roadmap.md` § "M8 — UI Framework Rewrite".

> **Every M8 worker reads this file first, then only their phase doc.** Do not read the other
> phase docs or go exploring — each phase doc is self-contained and tells you exactly what to
> read. This is deliberate for token efficiency.

---

## What this milestone is

A `WorldEngine.UI`-only refactor that replaces per-panel, hand-assembled Myra UI with one design
system. No sim changes. The app must run after every phase. The end state (framework §3):

```
Layer 5  Screens        WorldGenScreen · SimWorkspace
Layer 4  Layout host     Regions · z-bands · scroll reserve · hit-test router   ← kills the layout bugs
Layer 3  Panels          rebuilt on the kit, one IWorkspacePanel each
Layer 2  Composite kit   PanelFrame · StatRow · Meter · EntityLink · EmptyState · Tooltip · …
Layer 1  Widget kit       We* wrappers over Myra (only layer that sees Myra)
Layer 0  Myra            never referenced above Layer 1
Cross-cutting: SelectionBus · Presenter · CommandGateway · Keybind/CommandRegistry
```

## Phase sequence (strict)

| Phase | Doc | Depends on | Worker | One-line deliverable |
|-------|-----|-----------|--------|----------------------|
| 8.0 | `m8_phase0_tokens_kit.md` | — | Sonnet | Tokens, Layer 1–2 kit, Presenter, arch tests. No visible change. |
| 8.1 | `m8_phase1_layout_host.md` | 8.0 | Sonnet | Regions/z/hit-test host + tabbed dock; port panels as-is. |
| 8.2 | `m8_phase2_selection.md` | 8.1 | Sonnet | One `SelectionBus`; delete consume-once polling. |
| 8.3 | `m8_phase3_panel_migration.md` | 8.2 (8.3.6 also 8.4) | Haiku* | Rebuild each panel on the kit, one story per panel. |
| 8.4 | `m8_phase4_command_registry.md` | 8.2 | Sonnet | `CommandRegistry`; regenerate Help; rebinding. |
| 8.5 | `m8_phase5_settings.md` | 8.4 | Sonnet | Settings shell + Display/Controls tabs + prefs persistence. |

\* 8.3 stories are mechanical migrations; Haiku is fine **if** it can build/restore (see the
worktree gotcha below). If a Haiku worker stalls on restore, hand the story to Sonnet.

Do **not** start a phase until the previous one is merged and green.

---

## Non-negotiable constraints (every phase, every story)

From `CLAUDE.md` and the framework:
1. `WorldEngine.UI` reads only `WorldSnapshot`; world mutation flows `ICommand` →
   `CommandResolver`. **Never** reference `WorldState` from UI. This refactor touches no sim code.
2. Zero build warnings. `scripts/test-fast.sh` green (arch rules + `doc-check.py`) before a story
   is done.
3. No hardcoded design constants in panels — everything from `UiTheme` tokens (§8.0).
4. Nullable enabled, no `#nullable disable`. C# 12 / .NET 10.
5. XML doc comments on public interfaces/methods; `// MAP:` one-liner atop each new source file
   (the codebase map is generated from these — see any existing UI file for the format).
6. Determinism baseline is untouched because this is UI-only; if you find yourself editing
   anything under `WorldEngine.Sim`, stop — you're off-track.

## New architecture tests this milestone adds (land in 8.0 / 8.1)

Add to `WorldEngine.Tests/Architecture/ArchitectureRuleTests.cs`:
- `NoMyraOutsideKit` — no `using Myra` in `WorldEngine.UI/UI/` except `…/UI/Kit/`.
- `NoColorLiteralsInPanels` — no `Microsoft.Xna.Framework.Color` / pixel literals in
  `WorldEngine.UI/UI/Panels/`.
- `PanelsImplementContract` — every type in `…/UI/Panels/` implements `IWorkspacePanel`.
- `PanelsSetNoAbsoluteGeometry` — no assignment to Myra `Top/Left/Width/Height` in `…/UI/Panels/`.

Until a phase creates the folder a test guards, the test asserts trivially — add the test in the
phase that creates the target folder, per each phase doc.

## Target folder layout (created incrementally)

```
WorldEngine.UI/UI/
  Kit/          ← Layer 1 (We*) + Layer 2 composites. ONLY place Myra is referenced.
  Layout/       ← Layer 4: Region, ZBand, LayoutHost, InputRouter, SimWorkspace, ModalHost.
  Panels/       ← Layer 3: rebuilt panels (IWorkspacePanel).
  Present/      ← Presenter service + formatting tables.
  Selection/    ← SelectionState/SelectionRouter (exists) → SelectionBus.
  Input/        ← KeybindRegistry (exists) + CommandRegistry (8.4).
  Theme/        ← UiTheme (exists, expanded in 8.0), PanelChrome (retired by PanelFrame).
```
Old panel files (`EventLogPanel.cs`, `TileInspectorPanel.cs`, …) stay put until 8.3 migrates and
deletes each one. Don't move them early.

## Shared conventions for workers

- **Read-first discipline:** each phase doc lists the *exact* minimal files to read. Use
  `python3 scripts/scip-query.py defs <Type>` to locate symbols; use `docs/codebase_map.md` for
  one-line file descriptions. Don't `grep`-scan the tree.
- **Worktree/build gotcha (known):** parallel worktree agents can branch from a stale base and
  re-implement foundation divergently, and Haiku agents fail on SDK/NuGet/Myra restore. If you
  work in a worktree: `git log --oneline -1` to confirm your base includes the previous phase's
  merge; build with the pinned SDK (`~/.dotnet/dotnet`) and pass `--no-restore` after the first
  successful restore. If restore fails repeatedly, escalate to a Sonnet/direct run.
- **One story = one commit** (`feat(m8): 8.x.y — <deliverable>`), per the commit-per-story rule.
- **Definition of Done per story:** the phase doc's per-story "Done when" **plus** the framework
  §13 checklist items relevant to that story.

## How the pieces connect (so a worker isn't guessing)

- `Game1.cs` is the composition root: it builds the sim, holds the per-frame `WorldSnapshot`,
  and wires UI. Today it also does per-frame `ConsumePendingX()` navigation polling and panel
  registration (`_panelManager.Register(...)`, keybind registration ~L546–564). 8.1 moves panel
  hosting into `SimWorkspace`; 8.2 replaces the polling with `SelectionBus`; 8.4 moves action
  definitions into `CommandRegistry`. Each phase doc says exactly which `Game1` regions it edits.
- Panels receive a `PanelContext` (defined in 8.1): `{ WorldSnapshot Snapshot, SelectionBus
  Selection, Presenter Present, CommandGateway Commands }`. A panel is a pure function of
  `Snapshot + Selection`; it emits commands and selections, never mutates.

## Milestone Definition of Done

- All 6 phases merged; `scripts/test-fast.sh` green; zero warnings.
- Manual smoke (per 8.1/8.3 verification): launch → generate → dock tabs follow selection; no
  panel off-screen/over-map/click-leak/scrollbar-hidden; God Mode vs Spotlight visibly distinct;
  Help lists all binds and they're rebindable; New World (N) resets cleanly.
- `PanelChrome.cs` and all old panel files deleted; no `AddLine(string)` remains; no `using Myra`
  outside `UI/Kit/`.
- On completion, move each `m8_phase*.md` to `docs/phases/archive/` with `Status: COMPLETE — <date>`
  and set the roadmap M8 row + this index to COMPLETE (per CLAUDE.md).
