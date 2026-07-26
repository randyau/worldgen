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
| **M6** | UI Experience & Polish | ✅ COMPLETE 2026-07-21 | All epics 6.1–6.4 done. See archive. |
| **M7** | Authoring & Agency (Spotlight + God Mode) | ✅ COMPLETE 2026-07-23 | All epics 7.1–7.4 done. See archive. |
| **M8** | UI Framework Rewrite | ✅ COMPLETE 2026-07-24 | All phases 8.0–8.5 done. See archive. |
| M9 | Created-Object Unification & Economic Depth | summary | Pay down G-1 debt; deepen crafting/economy. |
| M10 | Worldgen Preview & Modding | summary | Layered preview + adjustment; player config/data modding. |
| M11 | Scale & Distribution | summary | 10k+ year performance; local-scale gen; packaging. |

Ordering rationale: polish (M6) de-risks and clarifies the surfaces that Spotlight/God Mode
(M7) build on, so we author against a UI that's already coherent. **M8 (UI Framework Rewrite)
is inserted before economic depth (M9)** because M6/M7 grew the UI by accretion again and the
recurring layout defects (panels overflowing off-screen, floating over the map/each other,
click-through leakage, scrollbar-obstructed content) are now structural. M9 adds several *new*
surfaces (economic ledger, created-object detail, trade overlay) — doing the framework first
means those land on the new system instead of adding more debt to migrate later. The design
authority for M8 is `docs/ui_design_framework.md`. The G-1 refactor (M9) still sits after M7
because M7 does not depend on it. M10/M11 are the long-tail platform work.

---

## M6 — UI Experience & Polish  *(DETAILED)*

**Progress:** COMPLETE 2026-07-21. All epics done:
- Phase 1 (6.1.1–6.1.4, 6.2.1): foundation + interaction architecture — `docs/phases/archive/m6_phase1_foundation.md`
- Phase 2 (6.2.2–6.4.3): visual polish, filter panel, causal chain, cross-panel linking, first-run, empty states — `docs/phases/archive/m6_phase2_visual_polish.md`

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

## M8 — UI Framework Rewrite  ✅ COMPLETE 2026-07-24

**Design authority:** `docs/ui_design_framework.md` (read it before scoping any phase).
**Phase docs (archived):** `docs/phases/archive/m8_ui_framework_rewrite.md` (index, has
close-out notes) + `m8_phase0…5_*.md`.

All 6 phases shipped: design tokens/kit/Presenter (8.0), the layout host + tabbed dock (8.1), a
single `SelectionBus` (8.2), every panel rebuilt on the kit (8.3, with the Timeline/Legends/
Toasts/map-tooltips sub-part of 8.3.6 deferred as net-new surfaces out of scope for a kit
migration), a rebindable `CommandRegistry` + Help (8.4), and a Settings shell with `UiPrefs`
persistence (8.5, sim-config tab deferred to M10 as planned). See the index's close-out notes for
the two accepted deviations (Myra usage in `UI/Layout/`+`UI/Panels/` beyond the letter of "only
Kit sees Myra"; a Settings gear button in the top bar not added). 8.0-8.2 were manually
playtested; 8.3-8.5 verified by build+test only (no display in the agent environment) — recommend
a full manual pass before relying on this milestone in front of end users.

### Goal
Replace the accreted, per-panel UI construction in `WorldEngine.UI` with one coherent design
system: a layered stack (wrapped-Myra widget kit → composite components → panels → a layout
host that owns all geometry/z-order/hit-testing → screens), a tabbed contextual dock, a single
selection bus, and a presenter layer. No new sim systems and no change to the sim/UI boundary —
this is a `WorldEngine.UI`-only refactor. The app stays runnable after every phase.

### Why now
The recurring layout defects are structural, not cosmetic: panels run off-screen, float over
the map or over each other, leak clicks through to the map, and hide content behind scrollbars.
The framework makes these **impossible to express** by moving all geometry/z/hit-testing into a
layout host that panels cannot bypass (framework P4, §3 Layer 4, §5). Fixing this before M9's
new surfaces avoids migrating even more debt.

### Success criteria
- No panel sets absolute geometry; the layout host owns every rectangle, z-band, scroll
  reserve, and hit-test. The four historic bug classes cannot be reproduced without editing the
  host (framework §3.2).
- Panels are built only from the component kit — no `AddLine(string)` walls, no raw Myra above
  the Kit layer, no color/size/font literals (enforced by new architecture tests).
- One navigation mechanism: everything routes through `SelectionBus`; the parallel
  `ConsumePendingX()` polling in `Game1` is deleted.
- All sim data rendered through the `Presenter` — no raw `0–255` bytes, no enum/type-string leaks.
- Keybinds are rebindable via a `CommandRegistry`; Help regenerates from it.
- No regressions: `scripts/test-fast.sh` green, architecture tests pass, UI stays
  `WorldSnapshot`-only, determinism baseline unaffected (UI-only change).

### Phases (sequential — each shippable, app runnable throughout)
| Phase | Name | Roadmap epics | Worker model |
|-------|------|---------------|--------------|
| **8.0** | Design tokens & component kit | tokens, Layer 1–2 kit, Presenter, arch tests | Sonnet (foundation) |
| **8.1** | Layout host & tabbed dock | regions, z-bands, scroll reserve, hit-test router, `SimWorkspace`, `ModalHost` | Sonnet (architectural) |
| **8.2** | Selection unification | promote `SelectionBus`; delete consume-once polling; focus-lens service | Sonnet |
| **8.3** | Panel migration | rebuild each panel on the kit (one panel per story) | Haiku-friendly (mechanical) |
| **8.4** | Command registry & Help | `CommandRegistry`; regenerate Help; keybind rebinding | Sonnet |
| **8.5** | Settings scaffold | Settings screen shell + Display/Controls tabs + UI-prefs persistence | Sonnet |

Sequencing is strict: 8.0 → 8.1 → 8.2 gate everything; 8.3 stories are internally parallel-safe
(one panel each) but depend on 8.0–8.2; 8.4 precedes the Help rebuild inside 8.3.6; 8.5 last.
The M9 sim-config settings tab plugs into the 8.5 shell — it is **not** built in M8.

### Moddability posture (framework §10)
Coherence-first: add named registries (`PanelRegistry`, `OverlayRegistry`, `CommandRegistry`,
`Presenter` maps) as the touched code passes through them, each marked `// MOD SEAM:`. Do **not**
build the mod data schema in M8.

---

## M9 — Created-Object Unification & Economic Depth  *(summary)*

**Status:** Phase 9.0 (G-1 + G-2) COMPLETE — 2026-07-26. Phase 9.1 (economic depth) COMPLETE —
2026-07-26 — see `docs/phases/archive/m9_phase1_economic_depth.md`. 9.2 (settlement
specialization) not yet scoped.

Pays down the Session G / G-1 debt and builds economic depth on the cleaned foundation.

- **G-1 unification (north-star) — DONE:** collapsed the four divergent taxonomies (`ArtisanGoodType`, `ArtType`, `DiscoveryType`, `ArtifactCategory`) into one shared `CreatedGoodType` (`WorldEngine.Sim/Core/Enumerations.cs`) plus `CreatedGoodTaxonomy` (`WorldEngine.Sim/Entities/Artifacts/CreatedGoodTaxonomy.cs`). A creative act yields a *product of type X*; quality drives persistence (routine → transient economic good, exceptional → an `Artifact` weighted-derived from that same type X). The `RoleToArtifactCategory` stopgap is deleted (trimmed to a `FallbackRoleCategory` for the roles — General/Governor/Merchant/Physician — that have no "product").
- **G-2 type variety — DONE:** every masterwork good now weighted-rolls across plausible categories (Armor is reachable from Metalwork/Leatherwork/Metallurgy); battle-forged and heroic-death artifacts roll from independent weighted tables (`sim_config.toml [artifacts] battle_category_weight_*` / `heroic_death_category_weight_*`) instead of hardcoded `Weapon`.
- **Economic depth — 9.1 DONE:** per-capita demand for non-vital resources (minerals/timber previously had supply but no demand draw — a pre-existing gap flagged since Session F-3, now generalized via `ResourcePressureConfig.NonVitalDemandPerCapita`); 5 of 8 previously write-only `bonus_*` store keys (`bonus_food_yield`, `bonus_disease_resistance`, `bonus_civ_cohesion`, `bonus_military_strength`, `bonus_trade_income`) now wired to real, capped effects — the remaining three (`bonus_construction_speed`, `bonus_navigation`, `bonus_exploration_range`) stay intentionally inert pending mechanics that don't exist yet (see `// DECISION` in `CreatedGoodTaxonomy.cs`); demand-aware merchant routing weights trade opportunity by the destination's per-capita deficiency. See `docs/phases/archive/m9_phase1_economic_depth.md`. Settlement specialization / trade-network topology stay deferred to a possible 9.2 (not yet re-scoped). Balance regression suite (`scripts/test-balance.sh`, `config/balance_invariants.toml [year_300]`) re-run and green — 9.0 changed only artifact category *mix*, not totals, so it needed no re-sweep; 9.1 touches stockpile growth, disease, war, and unrest rates and was re-swept.

## M10 — Worldgen Preview & Modding  *(summary)*

- **Layered worldgen preview + adjustment:** build the `WorldGenPipeline` (`RunUpTo`/`RerunFrom`) that M1 deferred; let players tweak sea level / parameters and re-preview per layer before committing.
- **Player config exposure:** surface `sim_config.toml` tunables through UI; safe presets. Lands as the **sim-config tab in the M8 Settings screen shell** (`docs/ui_design_framework.md` §9.2), reusing the M8 component kit — not a bespoke UI.
- **Data modding:** documented, moddable config/data (ancestries, names, biomes, resources) with validation — no plugin/code modding (that stays out of scope per CLAUDE.md). Uses the M8 `// MOD SEAM:` registries (framework §10) as the extension points.

## M11 — Scale & Distribution  *(summary)*

- **Long-run performance:** profile and optimize 10k+ year runs (event volume, DB growth, snapshot cost); confirm the disk-as-record model holds at scale.
- **Local-scale generation:** activate the `manifests.bin` border-manifest hook (DS-A2) for local/zoomed generation — the long-reserved M4-era capability.
- **Distribution:** extend `publish-win.sh` to cross-platform packaging; onboarding/first-run for distributed builds.

---

## Cross-cutting backlog (not milestone-bound)

- **Character water crossings** — Characters currently cannot cross ocean tiles; they
  pathfind land-only (`IsLand` check in `UtilityScorer.BestAdjacentTile`), so continents
  separated by water are permanently isolated. Design needed before implementation:
  - Should crossing be cheap (any coastal character can hop narrow straits) or gated
    (requires a ship goal, `NavigationSkill`, or a friendly coastal settlement)?
  - A minimal "coast-to-coast ferry" could let characters at coastal tiles step onto a
    shallow-ocean tile for one move, skipping the pathfinding complexity entirely.
  - A fuller solution would add ocean pathfinding through shallow water only (using
    `TileStaticFlags.IsCoastal` on ocean tiles as the walkable set), governed by a
    `character.ocean_crossing_enabled` config toggle.
  - Either path needs a `CrossesOcean = true` flag on the resulting `MoveToTile` command
    so history events can record sea voyages distinctly.
  - Planned for a future phase — file this under sim character mobility as a standalone
    story in M9/M10 (it is a sim change, so it is **not** part of the M8 UI rewrite).
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
