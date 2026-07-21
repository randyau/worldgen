# M6 Phase 2 — Visual Polish

**Milestone:** M6 — UI Experience & Polish
**Status:** IN PROGRESS
**Roadmap:** `docs/roadmap.md` § M6 (Epics 6.2.2, 6.2.4, 6.3.2, 6.4.1)

## Goal

Four self-contained visual polish stories, each touching different files. No new sim systems.
Constraints: `WorldEngine.UI` stays `WorldSnapshot`-only; no sim mutation from UI; no hardcoded
numbers; zero build warnings; `scripts/test-fast.sh` green.

## Stories

| # | Roadmap | Deliverable | State |
|---|---------|-------------|-------|
| 1 | 6.2.4 | EventLog row readability: tier stripe, richer text, season, IsFirstOfKind badge | TODO |
| 2 | 6.3.2 | TimelineBar polish: century ticks, headline pips, improved gradient | TODO |
| 3 | 6.4.1 | WorldGenScreen polish: % progress, completion state, "Start Simulation" button | TODO |
| 4 | 6.2.2 | OverlayLegend: on-map color legend for the active overlay (SpriteBatch, lower-left) | TODO |

## Key constraints

- No new `WorldEngine.Sim` → `WorldEngine.UI` reference.
- `UiTheme` is the single place for colors/spacing — don't hardcode new Color literals inline.
- Architecture tests must pass (especially ICommand sealed records, no async in sim).
- Each story: compile + `scripts/test-fast.sh` green before calling done.
