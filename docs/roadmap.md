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
| — | Balance / tuning | 🔄 IN PROGRESS | Unstructured; `docs/archive/tuning_balance_review_2026-07-18.md`. |

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
- **Distribution** — only M1's `scripts/publish-win.sh`. (Pushed out to final pre-release milestone M19 on 2026-07-31 — see below.)
- **Created-object unification (Session G / G-1)** — four divergent "things characters make" taxonomies; self-contained refactor pending.

---

## Forward milestone plan

| # | Name | Detail | Theme |
|---|------|--------|-------|
| **M6** | UI Experience & Polish | ✅ COMPLETE 2026-07-21 | All epics 6.1–6.4 done. See archive. |
| **M7** | Authoring & Agency (Spotlight + God Mode) | ✅ COMPLETE 2026-07-23 | All epics 7.1–7.4 done. See archive. |
| **M8** | UI Framework Rewrite | ✅ COMPLETE 2026-07-24 | All phases 8.0–8.5 done. See archive. |
| M9 | Created-Object Unification & Economic Depth | ✅ COMPLETE 2026-07-26 | Pay down G-1 debt; deepen crafting/economy. See archive. |
| M10 | Worldgen Preview & Modding | ✅ COMPLETE 2026-07-26 | Layered preview + adjustment; player config/data modding. See archive. |
| M11 | Scale | summary | 10k+ year performance; local-scale gen. |
| M12 | Organization Model *(new — inserted 2026-07-30)* | summary | Generalize civ/guild/religion/family into a shared Organization abstraction with decoupled org-relationships and multi-membership. Prerequisite for M13–M15. |
| M13 | Generational & Domestic Drama | summary | Family bonds, mentorship, non-war rivalry, betrayal within a civ. |
| M14 | Economy & Independent Wealth | summary | Persistent trade routes; merchant wealth as a power track separate from rulership. |
| M15 | Religion, Deepened | summary | Schism, heresy, pilgrimage; religious leaders as a third power track alongside rulers/merchants. |
| M16 | Disasters, Reworked | summary | Give eruptions/disasters real consequences; expand variety beyond wildfire/beasts; multi-year recovery arcs. |
| M17 | Exploration & the Unknown | summary | Land expeditions; first contact; discovering ruins/artifacts from prior collapsed civs. |
| M18 | Intrigue & Espionage | summary | Failed assassinations, coups, corrupt Tier-2 role-holders, spies. |
| M19 | Packaging & Release *(final pre-release milestone — pushed out from M11 on 2026-07-31)* | summary | Cross-platform packaging (extend `publish-win.sh`); onboarding/first-run for distributed builds. Deliberately last: hold until the M12–M18 narrative-depth backlog is settled so packaging targets a feature-complete build, not a moving one. |

Ordering rationale: polish (M6) de-risks and clarifies the surfaces that Spotlight/God Mode
(M7) build on, so we author against a UI that's already coherent. **M8 (UI Framework Rewrite)
is inserted before economic depth (M9)** because M6/M7 grew the UI by accretion again and the
recurring layout defects (panels overflowing off-screen, floating over the map/each other,
click-through leakage, scrollbar-obstructed content) are now structural. M9 adds several *new*
surfaces (economic ledger, created-object detail, trade overlay) — doing the framework first
means those land on the new system instead of adding more debt to migrate later. The design
authority for M8 is `docs/ui_design_framework.md`. The G-1 refactor (M9) still sits after M7
because M7 does not depend on it. M10 (complete) and M11 are the long-tail platform work.

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

**Status: COMPLETE — 2026-07-26.** Phase 9.0 (G-1 + G-2), Phase 9.1 (economic depth), and Phase 9.2
(settlement specialization) all shipped and green — see
`docs/phases/archive/m9_created_object_unification.md` (index) and its linked phase docs. Three
`bonus_*` keys (construction_speed, navigation, exploration_range) and full trade-network topology
were considered and deliberately left out of scope — see the index doc's closeout note for why.
Next milestone: **M10 — Worldgen Preview & Modding** (below).

Pays down the Session G / G-1 debt and builds economic depth on the cleaned foundation.

- **G-1 unification (north-star) — DONE:** collapsed the four divergent taxonomies (`ArtisanGoodType`, `ArtType`, `DiscoveryType`, `ArtifactCategory`) into one shared `CreatedGoodType` (`WorldEngine.Sim/Core/Enumerations.cs`) plus `CreatedGoodTaxonomy` (`WorldEngine.Sim/Entities/Artifacts/CreatedGoodTaxonomy.cs`). A creative act yields a *product of type X*; quality drives persistence (routine → transient economic good, exceptional → an `Artifact` weighted-derived from that same type X). The `RoleToArtifactCategory` stopgap is deleted (trimmed to a `FallbackRoleCategory` for the roles — General/Governor/Merchant/Physician — that have no "product").
- **G-2 type variety — DONE:** every masterwork good now weighted-rolls across plausible categories (Armor is reachable from Metalwork/Leatherwork/Metallurgy); battle-forged and heroic-death artifacts roll from independent weighted tables (`sim_config.toml [artifacts] battle_category_weight_*` / `heroic_death_category_weight_*`) instead of hardcoded `Weapon`.
- **Economic depth — 9.1 DONE:** per-capita demand for non-vital resources (minerals/timber previously had supply but no demand draw — a pre-existing gap flagged since Session F-3, now generalized via `ResourcePressureConfig.NonVitalDemandPerCapita`); 5 of 8 previously write-only `bonus_*` store keys (`bonus_food_yield`, `bonus_disease_resistance`, `bonus_civ_cohesion`, `bonus_military_strength`, `bonus_trade_income`) now wired to real, capped effects — the remaining three (`bonus_construction_speed`, `bonus_navigation`, `bonus_exploration_range`) stay intentionally inert pending mechanics that don't exist yet (see `// DECISION` in `CreatedGoodTaxonomy.cs`); demand-aware merchant routing weights trade opportunity by the destination's per-capita deficiency. See `docs/phases/archive/m9_phase1_economic_depth.md`. Balance regression suite (`scripts/test-balance.sh`, `config/balance_invariants.toml [year_300]`) re-run and green — 9.0 changed only artifact category *mix*, not totals, so it needed no re-sweep; 9.1 touches stockpile growth, disease, war, and unrest rates and was re-swept.
- **Settlement specialization — 9.2 DONE:** settlements EMA-track their dominant non-vital resource (`SettlementStub.Specialization`/`SpecializationStrength`, `ResourcePressurePhase.UpdateSpecialization`) and get a capped production multiplier on it (`ResourcePressureConfig.SpecializationBonusScale`/`Cap`); `Tier2BehaviorPhase.RunMerchant` adds a matching export-side routing bonus (`CharacterSimConfig.MerchantSpecializationBonusScale`) so a settlement's merchants preferentially trade what it's known for. Full trade-network topology (named routes, travel time/caravans, price/currency) stays out of scope — trade remains the existing teleport-style transfer. See `docs/phases/archive/m9_phase2_settlement_specialization.md`. Balance sweep re-run and green.

## M10 — Worldgen Preview & Modding  *(summary)*

**Status: COMPLETE — 2026-07-26.** Phase sequence (10.0–10.3) defined in
`docs/phases/archive/m10_worldgen_preview_modding.md`; all four phases shipped and green —
see the index doc and its linked phase docs (`m10_phase0_pipeline_resume.md` /
`m10_phase1_worldgen_preview_screen.md` / `m10_phase2_sim_config_settings_tab.md` /
`m10_phase3_data_modding.md`). Next milestone: **M11 — Scale** (below).

- **Layered worldgen preview + adjustment — DONE:** `WorldGenPipeline` (`RunUpTo`/`RerunFrom`) that M1 deferred; players tweak sea level / parameters and re-preview per layer before committing, via the worldgen preview screen (10.1).
- **Player config exposure — DONE:** `sim_config.toml` tunables surfaced through UI via a generic `ConfigRegistry` (10.2a) and the sim-config tab in the M8 Settings screen shell (10.2, `docs/ui_design_framework.md` §9.2), reusing the M8 component kit — not a bespoke UI.
- **Data modding — DONE:** documented, moddable config/data with load-time validation for `config/ancestries.toml` and `config/beasts.toml` (10.3, see `docs/modding.md`) — no plugin/code modding (stays out of scope per CLAUDE.md). Biomes/resources are still hardcoded C# enums and would need their own follow-up phase to become data-driven — see `docs/phases/archive/m10_worldgen_preview_modding.md` DECISION (10.3).
- **Pipeline resume/replay — DONE:** phase 10.0, prerequisite plumbing for the preview screen.

## M11 — Scale  *(summary)*

- **Long-run performance — phase 0 DONE (2026-07-27):** profiled a 10k-year baseline run (seed 42,
  `-c Release`) and found a ~3x tick-rate slowdown over the run's lifetime, root-caused to
  `EventStore.BuildSummaries()` being called on a hardcoded 50-year cadence regardless of whether
  anything reads the summary tables — each call does a full `Events`-table rescan, so cumulative
  cost scaled with total historical event count. Fixed via a config-driven
  `SimLoopConfig.SummaryRebuildIntervalYears` (0 = disabled); the headless runner now disables
  periodic rebuilds and does one at the end instead. Validated via a 3k-year re-run sustaining a
  flat ~65 ticks/sec (vs. baseline's degrading 19 ticks/sec average). Disk-as-record model holds
  fine at scale (531MB/1.7M events for 10k years, no issues). Also added periodic progress logging
  to the headless runner (`SimLoop.RunSynchronous` + `SimLoopConfig.HeadlessProgressIntervalSeconds`)
  since long runs were previously silent until complete. See
  `docs/phases/archive/m11_phase0_longrun_performance.md`.
- **Local-scale generation — DONE (2026-07-29):** full phase sequence 11.1–11.8, see
  `docs/phases/archive/m11_local_scale_generation.md`. This turned out to be a from-scratch
  subsystem, not an "activation": the `manifests.bin` hook (DS-A2) was vestigial (never wired into
  the real pipeline, `LoadFromFile` threw), and the River layer didn't carry the per-edge crossing
  data the manifest format assumes — both built from scratch (11.1). Chunked/lazy (Minecraft-style)
  10m-resolution terrain generation (11.2/11.3), river threading (11.4), and a sparse persisted
  delta overlay for permanent modifications (11.5) shape a foundation for future local-scale
  character interaction — a nullable, unpopulated local-presence stub landed on `Tier1Character`
  (11.6) but no local movement/interaction behavior was implemented this sequence, per its explicit
  scope. The UI (11.7, plus four same-day follow-up passes after playtesting): a `[View Local]`
  button opens a pannable/zoomable local-scale render of the clicked world tile *within* the
  existing MapCanvas region — not a full-screen takeover — so TopBar (time controls) and RightDock
  (contextual panels) stay live throughout; clicking a character/beast marker selects it through
  the same `SelectionBus` the main map uses, so whatever contextual panel that selection already
  shows (with its own working Watch button) appears for free. Sub-tile decoration (tree stands,
  rock outcroppings, wetland patches, sand dunes) gives chunks visual variety beyond a flat biome
  wash; a DECISION made with the user established that decorations stay purely cosmetic for now —
  `(ChunkCoord, LocalTileCoord)` is already the stable per-cell key a future "mine/collect"
  interaction would need, so no new identity scheme had to be added ahead of that milestone.

M11's remaining scope as originally planned — cross-platform packaging/distribution — has been
pushed out to **M19 — Packaging & Release** (see below), now the final pre-release milestone.
M11 itself is otherwise complete (phase 0 + local-scale generation both done).

---

## M12–M18 — Narrative Depth Expansion  *(summary)*

**Status: not started.** Origin: 2026-07-30 design conversation — the simulation currently
generates history, but nearly every *headline*-tier story that surfaces is war/conquest.
Investigation found the event schema already has non-war event types
(`CharacterMarried`, `CharacterGrieved`, `ScholarDiscovery`, `ArtisanCrafted`,
`ReligionFounded`, `MerchantTradeCompleted`, `SeaVoyage*`, `DiseaseOutbreak`) that mostly go
unused in the stories that get told — likely because goal-utility weights
(`[utility_affinity.goal_affinity]`) and the headline significance threshold
(`events.minimum_recorded_tier`) both bias toward conflict. **Before scoping any of
M12–M18 in detail, do a diagnostic pass on those two config surfaces** to establish how much of
the war-dominance problem is a tuning bug (events already firing but filtered/outscored) versus
a genuine missing-mechanic gap — the milestones below assume some of both.

An M17-equivalent idea (myth/unreliable-history — what a civ *believes* happened diverging from
the event log) was considered and explicitly deferred: characters have no belief system to hang
it on, and it would need one to mean anything. Revisit after M15 (religious leaders may partially
establish belief machinery).

These are sim-depth milestones, independent of M11's platform/distribution track — sequencing
between them (which comes first, whether they interleave with M11) is not yet decided. **M12
(Organization Model) is a hard prerequisite for M13–M15**, not just a nice-to-have — see below.

### Reusable mechanics — schema evolution notes

Found while scoping M12–M18: several existing systems have more depth already built into their
data than the current behavior/story surface uses. Flagging these now so schema work for
M12–M18 extends them instead of duplicating them.

- **Relationship-system depth audit (2026-07-30, code-verified — see M13 detail below for the
  full findings and mechanic proposals).** Confirmed by tracing actual reads/writes, not
  inference: of `RelationshipEdge`'s 7 fields (`Trust`, `Fear`, `Debt`, `IsAlly`, `IsRival`,
  `IsFamily`, `IsMarried`), only `Trust` and `IsRival` drive any behavior today. `Fear` is
  written once and never read. `Debt` is never written with a nonzero value and never read —
  fully vestigial, not merely underused. `IsFamily`/`IsMarried` are defined but never read
  anywhere. Full detail and reuse plan under M12.
- **Personality/aptitude bias fields exist per-ancestry** (`AncestryConfig` — Gaussian offsets on
  a base-0.5 mean) — **confirmed (2026-07-30):** they feed `CharacterFactory` → spawned
  `Personality` traits → goal-formation *thresholds* (Compassion→Bond, Aggression→Dominance/
  Avenge, Sociability→Alliance) and `UtilityAffinityConfig` action-affinity lookups. They do
  **not** touch `Trust`/`Fear`/`Debt` values directly — personality decides which goals a
  character is inclined to form, not how it treats a specific other character. Not a gap to fix;
  just documenting the actual mechanism so future milestones don't assume personality already
  varies relationship-edge values.
- **Succession today is hardcoded to civ rulers only**
  (`Civilization.SuccessionCrisisEndYear`, wired in `CivTracker.Diplomacy.cs`, backed by the
  `SuccessionChain`/`Dynasties` tables). The underlying shape — a "seat" that becomes vacant, a
  pool of eligible heirs/claimants, a crisis window, a resolution event — is not ruler-specific.
  **Generalize it into a reusable "leadership succession" mechanic**, scoped as part of M12's
  Organization Model (the seat naturally belongs to an `Organization`'s `LeaderId`, not to
  `Civilization` specifically), so the same machinery drives: civ rulers (existing), family
  heads (M13), guild/merchant-house heads (M14), and religious leaders (M15) — instead of
  hand-rolling three more bespoke succession-crisis implementations.
- **Tier2 roles are fixed-behavior** (`Tier2BehaviorPhase` — one hardcoded routine per role:
  Merchant/Physician/Scholar/Artisan/General/Governor). M14 (merchant wealth) and M18
  (corrupt role-holders) both need *variable* behavior per role-holder rather than one script per
  role — build that flexibility once as shared infrastructure rather than twice.
- **`CulturalProfile`/`CivTraits`** (cultural distance, acquired traits) are computed once at civ
  founding — needs verification of whether culture ever drifts from sustained contact. If it's
  static-after-founding, letting it drift over time from trade/contact (M14/M17) is a cheap way
  to generate contact-driven stories using a value that already exists, rather than a new system.

- **M12 — Organization Model.** New foundational milestone, inserted 2026-07-30 — hard prerequisite for M13 (family), M14 (guilds), and M15 (religion), all of which need "an organization with a leader, members, and relationships to other organizations" and would otherwise each hand-roll a bespoke version of what `Civilization` already does ad hoc.

  **Why this exists.** Audited how civ diplomacy actually works today (`Civilization.cs`,
  `CivTracker*.cs`): there is no civ-pair relationship record. `WarsAgainst`/`BorderTension`/
  `PeaceTreaties` are per-civ dictionaries, but alliance state itself is **derived on the fly
  from the ruler-pair's personal `RelationshipEdge`** — `CivTracker.Diplomacy.cs` fires
  `AllianceFormed` when the ruler-to-ruler `Trust` crosses a threshold, breaks it when `Trust`
  drops. There is no independent "these two civs are allied" fact, just two individuals'
  feelings read as if they were one. Character civ-membership is a single `CivId` field on
  `IdentityData` (not a set) — `Tier2Character` doesn't even have that. Religion currently has
  no entity, no follower list, and no membership tracking at all — `ReligionFounded` is a pure
  flavor event. There is no shared type or interface between `Civilization`, settlements, or
  (nonexistent) religion — every concept is independently bespoke. This pattern will not survive
  three more copies.

  **Design decisions made 2026-07-30 (user-confirmed, both cross-cutting/schema-level per
  CLAUDE.md's "stop and ask" rule — do not revisit without reopening the discussion):**
  1. **Org-to-org relationships are decoupled from individual leaders.** An `Organization`
     (Kind: Civilization / Guild / Religion / Family) gets its own persisted relationship state
     to other organizations (alliance/war/tension), instead of being derived from its leader's
     personal `RelationshipEdge`. The leader's personal trust becomes one *input/lever* into
     that state, not the source of truth — fixes the "assassinate the ruler, alliance evaporates"
     fragility and supports organizations without a single leader later. `Civilization`'s
     existing `WarsAgainst`/`BorderTension`/`PeaceTreaties` machinery is the model to generalize
     onto `Organization`, not to replace.
  2. **Multi-membership uses weighted loyalty, not a fixed priority order.** Replace the
     single `CivId` field with a membership set per character (`OrganizationId`, `Role`,
     `Loyalty` — a continuous value analogous to `RelationshipEdge.Trust`). When memberships
     conflict (e.g. a character's religion's mother civ is at war with their own civ), goal/
     utility scoring weighs by whichever organization has the higher `Loyalty` stake in the
     decision at hand, rather than a hardcoded ranking. This is genuinely new scoring logic in
     `UtilityScorer`/`GoalManager`, not just a schema change — budget real design time for it,
     likely alongside M13 since domestic/family loyalty is the first real test of it.
  3. **Leadership succession (see the reusable-mechanics note above) becomes part of this
     model** — the vacant-seat/heir-pool/crisis-window pattern hangs off `Organization.LeaderId`
     generically, with civ rulers as the existing (now-migrated) instance.

  **Scope boundaries:** `Civilization`-specific mechanics that aren't about
  membership/leadership/relationships (territory, `CulturalProfile`, war mechanics themselves)
  stay on `Civilization`; only the generalizable parts (membership, leader seat, org-relationship
  state) move up into the shared `Organization` layer. This is a `WorldEngine.Sim`-only schema
  and behavior migration — no new UI surface required, though existing panels that read `CivId`
  will need updating to read the membership set instead.

- **M13 — Generational & Domestic Drama.** Parent-child bonds with inherited traits/grudges/goals; mentorship (master→apprentice skill transfer, and its failure modes); non-war rivalry (romantic, professional, succession disputes within a family); betrayal by an ally, spouse, or heir. Highest-leverage item — mostly wiring new goal types onto existing character relationships, no new worldgen or event-log domains required. First consumer of the generalized succession mechanic (family-head seats) and the M12 Organization Model's weighted-loyalty scoring.

  **Relationship-system audit (2026-07-30) — why this milestone exists and what it should fix.**
  Traced every read/write of `RelationshipEdge` (`WorldEngine.Sim/Entities/Characters/
  RelationshipEdge.cs`) and its consumers. Functionally the palette is 7 fields but only 2 do
  anything:
  - `Trust` — gates Bond formation (`GoalManager.FindHighTrustCompanion`), feeds
    `UtilityScorer` alliance/rivalry/negotiate scoring, drains via territorial disputes
    (`CharacterBehaviorPhase`). **Also reused verbatim as civ-level diplomacy** —
    `CivTracker` reads/writes the *ruler pair's* personal `RelationshipEdge` as the civ's
    diplomatic state (`CivTracker.Diplomacy.cs`, `CivTracker.War.cs`). One substrate, two
    callers: a ruler's personal feelings toward another ruler *are* their civs' foreign policy.
  - `IsRival` — feeds Dominance-goal targeting (`GoalManager.FindNearbyRival`) and
    territorial-dispute resolution. No non-war outlet exists — every rivalry's only mechanical
    endpoint is a war goal.
  - `Fear` — written once (`CivTracker.cs`, ruler intimidation), persisted, **never read by
    anything**. Dead code path, not just underused.
  - `Debt` — **never written with a nonzero value anywhere in the codebase and never read.**
    Fully vestigial despite being a modeled field with persistence support.
  - `IsFamily` / `IsMarried` — defined, **never read anywhere in behavior.** Marriage and
    family are cosmetic flags today.
  - **`Grieve` (the goal that produces `CharacterGrieved`, `3005`) is the only behavioral
    consequence of any bond**, and it isn't even gated by `IsFamily`/`IsMarried` — it fires
    for *any* live `Bond` goal above the `Trust` threshold, so a spouse and a co-located
    trusted stranger grieve identically today.
  - No test in `WorldEngine.Tests` exercises `Fear`, `Debt`, the Bond→Grieve pipeline, or the
    family/marriage flags — this surface is unverified, not just unbalanced.

  **Mechanic proposals to widen the palette (reuse-first order):**
  1. Activate `Fear` as a submission/appeasement axis distinct from `Trust` — a feared rival
     gets avoided or placated rather than confronted, giving rivalry an outlet other than
     Dominance/war.
  2. Activate `Debt` as the obligation mechanic — an indebted character protects/favors their
     creditor even against self-interest; debt is inheritable (ties to the generalized
     succession mechanic) and forgivable (a reconciliation event).
  3. Wire `IsFamily`/`IsMarried` into actual consequence weight: grief severity/probability
     scaled by relationship type (spouse > family > bonded stranger, not uniform); shared
     household/resource effects; and — since civ diplomacy already reuses the ruler's personal
     edge — a ruler married across civ lines becomes a real diplomatic lever (arranged marriage
     as alliance-cement).
  4. Let non-ruler bonds reach the wider world. Today only the *ruler's* personal relationships
     ever escape the character layer. A trusted confidant could become an emissary candidate; a
     cross-civ friendship could dampen war tension or trigger asylum/defection — reuses the
     existing emissary and civ-tension systems rather than new ones.
  5. New relationship-transition events, cheap given `CivSplintered`-style patterns already
     exist: Reconciliation, Feud, Estrangement, Oath-breaking (a violated `Debt`).
- **M14 — Economy & Independent Wealth.** Trade routes as persistent entities between settlements (replacing the current one-shot `MerchantTradeCompleted` transaction) that can be severed by war/disaster/piracy, creating dependency and scarcity stories. Wealthy merchant characters/dynasties as a power track independent of political rulership — wealth that buys influence or funds factions without holding a title. Guilds/monopolies model as `Organization`s (M12), whose heads use the generalized succession mechanic. Debt and economic ruin as a civ-level failure mode distinct from military collapse. Builds on M9's economic-depth foundation (per-capita demand, settlement specialization) and the trade-network topology M9 deliberately left out of scope; also the second consumer (after M18) of Tier2-role behavior variability.
- **M15 — Religion, Deepened.** Schism — reuse the `CivSplintered` pattern (`3212`) for religions splitting into competing sects, now modeled as an `Organization` (M12) with real followers instead of a flavor event. Heresy/persecution short of holy war. Pilgrimage as a goal type. Religious leaders as a third power track alongside rulers (political) and merchants (M14, economic), using the generalized succession mechanic for a religious-leader seat (e.g. contested succession of a high priest).
- **M16 — Disasters, Reworked.** Eruptions currently fire (`DisasterConfig`/`[disasters]`) but have no gameplay consequence — wire real effects (destroyed settlements/improvements, ash-driven famine, displacement). Expand disaster variety beyond wildfire/beasts: flood, drought, earthquake, blight/crop disease, harsh winter. Model disasters as multi-year recovery arcs rather than single-tick events, so they leave a visible scar in a settlement's history instead of resolving instantly.
- **M17 — Exploration & the Unknown.** Land expeditions mirroring the M11 water-crossing pattern (`Port`/`SeaVoyage` delegation) — lost expeditions, first contact with an unknown ancestry or beast species. Ruins/artifacts from a *previously collapsed* civ (`CivilizationCollapsed`, `3202`, is already logged) discoverable by a later civ, resurfacing dead history as new story material.
- **M18 — Intrigue & Espionage.** Failed/attempted assassinations (today only resolved outcomes are logged). Coups — a civ's power changing hands without full `CivilizationCollapsed`. Corruption or abuse by an appointed Tier-2 role-holder (`AppointedToRole`, `3301`, exists; nothing currently exploits the role) — first consumer of the Tier2-role behavior variability described above. Spies/informants as a character role, feeding `CivIntelGathered` (`5004`) into deliberate sabotage rather than passive intel.

---

## M19 — Packaging & Release  *(summary — final pre-release milestone)*

**Status: not started.** Pushed out from M11 on 2026-07-31 — M11's Distribution scope
(cross-platform packaging, onboarding/first-run) was deferred so it isn't stale by the time it
ships: M12–M18 add new player-facing surfaces
(organizations, trade routes, religious leaders, exploration, espionage) that packaging and
onboarding should reflect. Sequenced last deliberately — this is the milestone that turns the
project into an actual release, so it should run once the narrative-depth backlog (M12–M18) is
settled, not before.

- **Cross-platform packaging** — extend `scripts/publish-win.sh` (currently Windows-only) to produce Linux/macOS builds.
- **Onboarding / first-run experience** — first-run flow for a distributed build (no dev environment assumed), distinct from the in-editor worldgen preview screen (M10).

---

## Cross-cutting backlog (not milestone-bound)

- **Character water crossings** — ✅ COMPLETE 2026-07-27. Characters can now cross open water via
  a civ-built `Port` improvement + ruler-delegated `SeaVoyage` goal (mirrors the existing
  `FoundCity` delegation pattern), characters only (not beasts), always succeeds for now (a
  `// V2: sea voyage failure (weather, sea monsters)` hook is left for a future milestone), on by
  default via `SeafaringConfig.OceanCrossingEnabled`. All four phases (11.0–11.3) shipped — see
  `docs/phases/archive/m11_water_crossings.md` for the full design rationale and
  `m11_phase0…3_*.md` for what shipped in each.
- **LLM prose generation** — V2 feature; hook only (per CLAUDE.md), no build.
- **Magic as physical substrate** — V2; `MagicIntensity` stays a stored, behavior-free layer.
- **Voxel rendering, plugin/code modding, multiplayer** — out of scope.
- **Balance/tuning** — ongoing; owned by `docs/archive/tuning_balance_review_2026-07-18.md` and `config/balance_invariants.toml`, not a milestone.

---

## Maintenance

When a milestone starts, create story-level phase docs under `docs/phases/` (archive them to
`docs/phases/archive/` on completion, per CLAUDE.md). Promote the next summary-level milestone
here to story-level detail as it approaches. Update the milestone table's Status/Detail
columns as work lands. Keep this doc as the single forward source of truth; do not re-fork
the plan into `mvp_spec.md`.
