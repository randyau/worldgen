# World Engine — Forward Roadmap

**Version:** 1.0
**Date:** 2026-07-20
**Status:** LIVING DOCUMENT — the current source of truth for milestone/phase planning from M6 onward.

This document supersedes the forward-looking milestone summaries in `docs/mvp_spec.md`
(which remains the historical spec of record for M1–M2 and is otherwise frozen). Read
this file when planning or scoping any milestone at or after M6.

Companion design rationale:
- `docs/design_session_decisions.md` — DS-A…G design decisions (Session G = created-object/artifacts)
- `docs/implementation_decisions_v0.3.md` — architecture
- `docs/architecture_decision_records.md` — ADR quick-reference

---

## How we got here (roadmap reconciliation)

The original `mvp_spec.md` (v0.4) planned four milestones ending in an **M4 "UI Experience"**
that would deliver Spotlight, God Mode, worldgen preview, modding, long-run performance, and
distribution. That milestone was **never built as planned** — instead the simulation kept
getting deeper. What actually shipped:

| # | Name | Status | Notes |
|---|------|--------|-------|
| M1 | The Living World | ✅ COMPLETE 2026-06-22 | As specced. |
| M2 | The Character System | ✅ COMPLETE 2026-06-23 | + goal system, ancestry, settlement economics. |
| M3 | Narrative Exploration | ✅ COMPLETE 2026-06-26 | Territory, history query, cultural identity, narrative UI, tile-inspect + **read-only** character watch, ancestry/culture, save/resume. |
| M4 | Civ Dynamics *(replaced "UI Experience")* | ✅ COMPLETE 2026-07-19 | Civ awareness/emissary, territory-war dynamics, religion/specialists. **Not** the originally-planned UI milestone. |
| M5 | Artifacts *(unplanned)* | ✅ COMPLETE 2026-07-20 | Legendary-item lifecycle, covet/goals, decay sink, telemetry. See Session G. |
| — | Balance / tuning | 🔄 IN PROGRESS | Unstructured; `docs/tuning_balance_review_2026-07-18.md`. |

**The "lost" pillars** — the player-facing agency and polish work from the skipped
"UI Experience" milestone — are what this roadmap picks back up, reordered and updated for
where the codebase now is.

### What's genuinely unbuilt (verified against code, 2026-07-20)

- **Spotlight (player-controlled character)** — only `CharacterWatchPanel.cs` (read-only) exists.
- **God Mode authoring** — only plumbing exists: `IsGodMode` on `SimEvent`, reserved `EventType.GodModeArtifactPlaced = 9004`, DB column, event-gate handling. No authoring commands or UI.
- **Layered worldgen preview + player adjustment** — nothing; no `WorldGenPipeline`/`RerunFrom`/`RunUpTo` (M1 built world gen as direct layer calls via `TileGridAssembler`).
- **Modding / config exposure to players** — nothing.
- **Long-run (10k+ year) performance pass** — never formally addressed.
- **Comprehensive UI polish** — functional panels exist (inspector, profile, civ history, event log, timeline, worldgen screen, focus lens) but interaction is keyboard-heavy and never had a cohesive design pass.
- **Distribution** — only M1's `scripts/publish-win.sh`.
- **Created-object unification (Session G / G-1)** — four divergent "things characters make" taxonomies; self-contained refactor pending.

---

## Forward milestone plan

| # | Name | Detail | Theme |
|---|------|--------|-------|
| **M6** | UI Experience & Polish | **story-level (below)** | Make what exists feel finished and legible. |
| **M7** | Authoring & Agency (Spotlight + God Mode) | **story-level (below)** | The core product promise: watch, author, inhabit. |
| M8 | Created-Object Unification & Economic Depth | summary | Pay down G-1 debt; deepen crafting/economy. |
| M9 | Worldgen Preview & Modding | summary | Layered preview + adjustment; player config/data modding. |
| M10 | Scale & Distribution | summary | 10k+ year performance; local-scale gen; packaging. |

Ordering rationale: polish (M6) de-risks and clarifies the surfaces that Spotlight/God Mode
(M7) build on, so we author against a UI that's already coherent. The G-1 refactor (M8) is
deliberately placed **after** M7 because M7 does not depend on it, and doing it under real
authoring pressure clarifies the target taxonomy. M9/M10 are the long-tail platform work.

---

## M6 — UI Experience & Polish  *(DETAILED)*

### Goal
A cohesive, legible, discoverable UI over the existing simulation. No new sim systems — this
milestone makes the current feature set (overlays, panels, timeline, worldgen screen) feel
like one designed product rather than an accretion of panels. Success = a first-time
worldbuilder can open a world, understand what they're seeing, and navigate history without a
keyboard cheat-sheet.

### Success criteria
- Every overlay and panel is reachable from visible, labeled UI (not keyboard-only).
- A consistent visual language: color, typography, spacing, panel chrome are unified.
- Overlays have on-screen legends; the active overlay is always indicated.
- Event log and history views are filterable and readable at a glance (tier, type, civ, character).
- The app has an onboarding path: launch → generate → oriented, with no dead ends.
- No regressions: `scripts/test-fast.sh` green, architecture tests pass, UI stays `WorldSnapshot`-only.

### Epic 6.1 — Interaction & information architecture
- **6.1.1 — Overlay control bar.** Replace keyboard-only overlay switching (B/E/T/M/R/G) with a visible, labeled overlay toolbar; keep keybinds as accelerators. Show the active overlay state. Source of truth stays `SetActiveOverlay` command.
- **6.1.2 — Panel manager & docking.** Unify panel show/hide (inspector, profile, civ history, watch, timeline) under one consistent toggle model with visible affordances, replacing the ad-hoc `H`/`W` toggles. Panels remember open/closed state.
- **6.1.3 — Global keybind + help overlay.** A single discoverable "?" help panel listing every shortcut, generated from one keybind registry (so it can't drift from `Game1` input handling).
- **6.1.4 — Selection model.** One consistent "selected thing" concept (tile / settlement / character / civ) that drives which contextual panel is shown, instead of independent click handlers.

### Epic 6.2 — Visual design pass
- **6.2.1 — Design tokens.** Centralize colors, fonts, spacing, panel chrome into a single theme applied across all Myra panels (today each panel styles itself). One place to retune.
- **6.2.2 — Map legibility.** Overlay legends (color ramp + labels) rendered on-map for each `OverlayType`; consistent civ-color derivation shared between territory overlay and panels.
- **6.2.3 — Marker & icon consistency.** Unify settlement markers, improvement icons, character/beast markers into one styled sprite set with zoom-appropriate scaling.
- **6.2.4 — Event log readability.** Tier color-coding, type icons/grouping, timestamp formatting, and readable density in `EventLogPanel`.

### Epic 6.3 — History navigation polish
- **6.3.1 — Filter panel.** First-class filter UI over the event log/history: by tier, event type, civ, character, year range. (Focus-lens state exists — surface it properly.)
- **6.3.2 — Timeline scrubber polish.** `TimelineBar` gets tick density markers, headline-event pips, and readable scrub feedback.
- **6.3.3 — Causal chain view.** "What led to this?" — render the causal-edge graph for a selected event as a navigable chain (data exists in `CausalEdges`/`HistoryQueryService`).
- **6.3.4 — Cross-panel linking.** Clicking a civ/character/settlement name anywhere opens the relevant profile/history panel (wire the existing panels together).

### Epic 6.4 — Onboarding & worldgen screen
- **6.4.1 — Worldgen screen polish.** `WorldGenScreen` shows per-layer progress with names and a final map preview before "Start Simulation."
- **6.4.2 — First-run orientation.** A dismissible intro pointing at time controls, overlays, and the event log — no dead-end empty states.
- **6.4.3 — Empty/loading states.** Consistent handling for pre-sim, loading-save, and no-results-in-filter states.

---

## M7 — Authoring & Agency: Spotlight + God Mode  *(DETAILED)*

### Goal
Deliver the two "lost" pillars that define the product for worldbuilders: **God Mode**
(author world events and nudge history) and **Spotlight** (inhabit and steer a single
character). Both are player agency layered on top of the deterministic sim without breaking
the command/resolve architecture or reproducibility of the un-authored baseline.

### Success criteria
- A player can pause, author a world event (disaster, artifact placement, character nudge) via UI, and see it land in history flagged `IsGodMode = true`.
- A player can enter Spotlight on any living character, issue high-level intents, and return control to the AI; the character remains a normal sim entity throughout.
- All authoring flows through `ICommand` → `CommandResolver`; no direct `WorldState` mutation from UI.
- God-Mode-authored events are visually distinguished in history and excluded from balance-invariant baselines where appropriate.
- Determinism preserved: a run with **no** authoring reproduces byte-for-byte against the current baseline.

### Epic 7.1 — God Mode foundation
- **7.1.1 — Authoring command taxonomy.** Define the `ICommand` set for authored acts (place artifact, trigger disaster, spawn/modify character, alter tile, force event). Sealed records, value-type fields only.
- **7.1.2 — Authoring resolve + provenance.** `CommandResolver` handles authored commands, stamping resulting `SimEvent`s with `IsGodMode = true`. Wire `EventType.GodModeArtifactPlaced = 9004` (Session G / G-3) as the first concrete case.
- **7.1.3 — Authoring guardrails.** Validation so authored acts can't corrupt invariants (valid coords, living targets, config-bounded magnitudes). Reject invalid commands cleanly.

### Epic 7.2 — God Mode UI
- **7.2.1 — Author panel.** A God Mode toolbar/panel (pause-gated) to pick an authoring action and target via map/selection, then confirm → command.
- **7.2.2 — Artifact & disaster authoring.** Concrete authoring flows for placing an artifact (leans on Session G artifact system) and triggering a disaster at a tile/region.
- **7.2.3 — Character authoring.** Spawn a character, or nudge an existing one's goal/needs/relationships, within guardrails.
- **7.2.4 — Provenance display.** God-Mode events badged distinctly in the event log/timeline; a toggle to show/hide authored history.

### Epic 7.3 — Spotlight foundation
- **7.3.1 — Spotlight session model.** Entering/exiting Spotlight on a character; the character stays a normal sim entity, but player intent biases its decision-making. Define the intent surface (movement, social, goal-setting) as commands.
- **7.3.2 — Intent → behavior integration.** `CharacterBehaviorPhase`/decision system consumes standing Spotlight intents as strong utility inputs rather than hard overrides, so the character still behaves coherently. `// DECISION:` the override-vs-bias policy.
- **7.3.3 — Determinism & handoff.** Spotlight input is command-sourced and logged; releasing Spotlight returns the character to pure-AI control with no residual state divergence.

### Epic 7.4 — Spotlight UI
- **7.4.1 — Spotlight HUD.** Promote `CharacterWatchPanel` into an interactive Spotlight view: current needs/goals/relationships + intent controls.
- **7.4.2 — Intent issuance.** Map/panel controls to issue movement, social, and goal intents; visible feedback on what the character is doing and why.
- **7.4.3 — Camera & follow.** Camera follow mode for the spotlighted character; smooth enter/exit transitions.

---

## M8 — Created-Object Unification & Economic Depth  *(summary)*

Pays down the Session G / G-1 debt and builds economic depth on the cleaned foundation.

- **G-1 unification (north-star):** collapse the four divergent taxonomies (`ArtisanGoodType`, `ArtType`, `DiscoveryType`, `ArtifactCategory`) into one shared `CraftedGoodType`/`CreatedObjectType`. A creative act yields a *product of type X*; **quality drives persistence** (routine → transient economic good, exceptional → an `Artifact` of the same type X). Delete the `RoleToArtifactCategory` stopgap.
- **G-2 type variety:** ensure every category has a creation path (battle → weighted Weapon/Armor/Regalia; heroic death → Weapon/Relic/Regalia), folded into the G-1 refactor — not a piecemeal patch.
- **Economic depth:** goods flow, per-capita demand, richer trade networks and specialization on top of the unified type system. Re-sweep artifact stock against the M5 decay-sink balance bands (`config/balance_invariants.toml [year_300]`) after any new creation source.

## M9 — Worldgen Preview & Modding  *(summary)*

- **Layered worldgen preview + adjustment:** build the `WorldGenPipeline` (`RunUpTo`/`RerunFrom`) that M1 deferred; let players tweak sea level / parameters and re-preview per layer before committing.
- **Player config exposure:** surface `sim_config.toml` tunables through UI; safe presets.
- **Data modding:** documented, moddable config/data (ancestries, names, biomes, resources) with validation — no plugin/code modding (that stays out of scope per CLAUDE.md).

## M10 — Scale & Distribution  *(summary)*

- **Long-run performance:** profile and optimize 10k+ year runs (event volume, DB growth, snapshot cost); confirm the disk-as-record model holds at scale.
- **Local-scale generation:** activate the `manifests.bin` border-manifest hook (DS-A2) for local/zoomed generation — the long-reserved M4-era capability.
- **Distribution:** extend `publish-win.sh` to cross-platform packaging; onboarding/first-run for distributed builds.

---

## Cross-cutting backlog (not milestone-bound)

- **LLM prose generation** — V2 feature; hook only (per CLAUDE.md), no build.
- **Magic as physical substrate** — V2; `MagicIntensity` stays a stored, behavior-free layer.
- **Voxel rendering, plugin/code modding, multiplayer** — out of scope.
- **Balance/tuning** — ongoing; owned by `docs/tuning_balance_review_2026-07-18.md` and `config/balance_invariants.toml`, not a milestone.

---

## Maintenance

When a milestone starts, create story-level phase docs under `docs/phases/` (archive them to
`docs/phases/archive/` on completion, per CLAUDE.md). Promote the next summary-level milestone
here to story-level detail as it approaches. Update the milestone table's Status/Detail
columns as work lands. Keep this doc as the single forward source of truth; do not re-fork
the plan into `mvp_spec.md`.
