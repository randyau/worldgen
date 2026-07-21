# M6 Phase 2 — Visual Polish & Navigation

**Milestone:** M6 — UI Experience & Polish
**Status:** COMPLETE — 2026-07-21
**Roadmap:** `docs/roadmap.md` § M6 (Epics 6.2.2–6.2.4, 6.3.1–6.3.4, 6.4.1–6.4.3)

## Goal

Self-contained UI stories covering the remaining M6 epics: overlay legend, history navigation
polish (filter panel, causal chain view, cross-panel linking), and onboarding (first-run
orientation, empty/loading states). No new sim systems.

Constraints: `WorldEngine.UI` stays `WorldSnapshot`-only; no sim mutation from UI; no hardcoded
numbers in code (use `UiTheme` or `SimConfig`); zero build warnings; `scripts/test-fast.sh` green.

## Stories

| # | Roadmap | Deliverable | State |
|---|---------|-------------|-------|
| 1 | 6.2.4 | EventLog row readability: tier stripe, richer text, season, IsFirstOfKind badge | DONE |
| 2 | 6.3.2 | TimelineBar polish: century ticks, headline pips, improved gradient | DONE |
| 3 | 6.4.1 | WorldGenScreen polish: % progress, completion state, "Start Simulation" button | DONE |
| 4 | 6.2.2 | OverlayLegend: on-map color legend for the active overlay (SpriteBatch, lower-left) | DONE |
| 5 | 6.3.1 | FilterPanel: first-class filter UI over event log — tier, event type, civ, char, year range | DONE |
| - | 6.2.3 | Marker consistency: standardized zoom helper, character=cross, ruin=×, beast=dot, settlement=square | DONE |
| 6 | 6.3.3 | CausalChainPanel: "What led to this?" — navigable causal-edge graph for a selected event | DONE |
| 7 | 6.3.4 | Cross-panel linking: clicking civ/char/settlement name anywhere opens the profile panel | DONE |
| 8 | 6.4.2 | FirstRunOrientation: dismissible intro pointing at time controls, overlays, event log | DONE |
| 9 | 6.4.3 | EmptyStates: consistent handling for pre-sim, loading-save, and no-results-in-filter | DONE |

## Key constraints

- No new `WorldEngine.Sim` → `WorldEngine.UI` reference.
- `UiTheme` is the single place for colors/spacing — don't hardcode new Color literals inline.
- Architecture tests must pass (especially ICommand sealed records, no async in sim).
- Each story: compile + `scripts/test-fast.sh` green before calling done.
- Stories 6 (causal chain) must be done before story 7 (cross-panel linking needs the new panels).
- Story 9 (empty states) should be done last — it sweeps all panels for consistent treatment.
