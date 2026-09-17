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
| M12 | Organization Model *(new — inserted 2026-07-30)* | ✅ COMPLETE 2026-07-31 | Generalize civ/guild/religion/family into a shared Organization abstraction with decoupled org-relationships and multi-membership. Prerequisite for M13–M15. See archive. |
| M13 | Generational & Domestic Drama | ✅ COMPLETE 2026-08-02 | Family bonds, mentorship, non-war rivalry, betrayal within a civ. See archive. |
| M14 | Economy & Independent Wealth | ✅ COMPLETE 2026-08-05 | Persistent trade routes; merchant wealth as a power track separate from rulership. See `docs/phases/archive/m14_economy_independent_wealth.md`. |
| M15 | Religion, Deepened | ✅ COMPLETE 2026-08-06 | Schism, heresy, pilgrimage; religious leaders as a third power track alongside rulers/merchants. See `docs/phases/archive/m15_religion_deepened.md`. |
| M15.9 | Sim Tech Debt *(new — inserted 2026-09-17)* | complete | Shipped 2026-09-17: membership-invariant consolidation, command-dispatch dedup, GoalManager goal-type tables, org succession dedup, cooldown-field conventions, dampening composition, test-scaffolding dedup (`FindLandTile`/`SpawnAt`/reflection call sites → `internal` + direct calls), plus the three items below: config-ified the last M2/M4-era hardcoded need constants, replaced the collision-prone entity-ID base-offset scheme with a tagged `DeterministicId` helper, and added an `IdGenerator.ResetForTests()` hook. |
| M15.95 | Civilization/Organization Unification *(new — inserted 2026-09-17)* | complete | Scoping (2026-09-17) found the problem much smaller than the roadmap assumed: `Members` was already 90% unified per M12's original intent (3 real hot paths already read `Organization.Members` for civ orgs), with exactly one gap (civ-death path never removed the dead character from `Organization.Members`); `SuccessionCrisisEndYear`/`WarsAgainst`/`BorderTension`/`PeaceTreaties` on `Organization` turned out to be 100% dead fields for every org kind, not an active mirror. Shipped same day: `CivTracker.SetCharacterCiv` now keeps `Civilization.Members` in sync too (three-sided invariant, one call site), removed 6 redundant direct `civ.Members.Add/Remove` call sites, routed the death path through `SetCharacterCiv(c, CivId.None, ...)`, and deleted the four dead `Organization` fields (plus `IsAtWarWith`, also dead) and their DTO/persistence coverage — no back-compat shim needed since save compatibility isn't a constraint pre-release. |
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

## M6 — UI Experience & Polish  ✅ COMPLETE 2026-07-21

A cohesive, legible, discoverable UI over the existing simulation (no new sim systems):
overlay control bar, panel manager/docking, global keybind+help overlay, selection model,
design tokens, map legibility, event log readability, filter panel, timeline polish, causal
chain view, cross-panel linking, worldgen screen polish, first-run onboarding. All epics
6.1–6.4 shipped — see `docs/phases/archive/m6_phase1_foundation.md` (6.1.1–6.1.4, 6.2.1) and
`docs/phases/archive/m6_phase2_visual_polish.md` (6.2.2–6.4.3) for story-level detail.

---

## M7 — Authoring & Agency: Spotlight + God Mode  ✅ COMPLETE 2026-07-23

The two "lost" pillars for worldbuilders: **God Mode** (author world events via `ICommand` →
`CommandResolver`, stamped `IsGodMode = true`, excluded from balance baselines) and
**Spotlight** (inhabit a character — intents bias its utility scoring rather than hard-override
it, so it stays a normal sim entity). Determinism preserved: an unauthored run still reproduces
byte-for-byte. All epics 7.1–7.4 (command taxonomy, guardrails, author panel, artifact/disaster/
character authoring, provenance display, Spotlight session model, intent integration, HUD,
camera follow) shipped — see `docs/phases/archive/m7_authoring_agency.md` for story-level detail.

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

**Status: M12–M15 COMPLETE (2026-07-31 through 2026-08-06); M16–M18 not started.** Origin: 2026-07-30 design conversation — the simulation currently
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

- **M12 — Organization Model.** ✅ COMPLETE 2026-07-31 — see `docs/phases/archive/m12_organization_model.md`. Generalized civ/guild/religion/family into a shared `Organization` abstraction (entity/registry, org-to-org alliance state decoupled from the leader's personal relationship, `Tier1Character.Memberships` replacing the single `CivId` field, a generalized leadership-succession kernel). Hard prerequisite for M13–M15, all of which need "an organization with a leader, members, and relationships to other organizations."
- **M13 — Generational & Domestic Drama.** ✅ COMPLETE 2026-08-02 — see `docs/phases/archive/m13_generational_domestic_drama.md` (phases 13.0–13.7, including the 13.7 lifespan-units-mismatch fix). Parent-child bonds with inherited traits/grudges/goals; mentorship; non-war rivalry within a family; betrayal by an ally, spouse, or heir. Activated the previously-dormant `RelationshipEdge` fields (`Fear`, `Debt`, `IsFamily`/`IsMarried`) that a 2026-07-30 audit found written but never read. First consumer of the generalized succession mechanic (family-head seats) and M12's weighted-loyalty scoring.
- **M13.8 — Tier2 Relationship Exposure.** ✅ COMPLETE 2026-08-03 — see `docs/phases/archive/m13_8_tier2_relationship_exposure.md`. Let Tier1 relationship actions (Bond/Rivalry/Placate/Marriage) target a co-located Tier2 character the same way `GrantAid`/`ForgiveDebt` already did, plus a Notability signal so Tier2 characters pulled into Tier1-driven drama crystallize into heroes more readily.
- **M14 — Economy & Independent Wealth.** ✅ COMPLETE 2026-08-05 — see `docs/phases/archive/m14_economy_independent_wealth.md` (all six phases 14.0–14.5). Trade routes as persistent, severable entities between settlements (full caravan/travel-time simulation, not a lightweight link); a real fungible per-character `Wealth` balance backed by the economy's existing precious-metal production, corrected over time by a world-wide `GlobalPriceIndex`; guilds modeled as `Organization`s; economic ruin/reparations extend the existing civ-collapse pathway. Builds on M9's economic-depth foundation.
- **M15 — Religion, Deepened.** ✅ COMPLETE 2026-08-06 — see `docs/phases/archive/m15_religion_deepened.md` (all six phases 15.0–15.6). Religion modeled as an `Organization` with real followers (not a flavor event); schism (reusing the `CivSplintered` pattern), heresy/persecution short of holy war, pilgrimage as a goal type, religious leaders as a third power track using the generalized succession mechanic.
- **M15.9 — Sim Tech Debt.** ✅ COMPLETE 2026-09-17 — see commit `9382e01` for full detail if needed. Consolidated membership join/leave/loyalty sites, deduped civ-vs-org command dispatch, collapsed `GoalManager`'s goal-type lists into one table, merged Guild/Religion succession blocks, unified cooldown-field conventions, deduped `*Dampening` composition, consolidated test-scaffolding duplication. Also config-ified the last hardcoded need constants, replaced the entity-ID base-offset scheme with a collision-free `DeterministicId` tag, and added `IdGenerator.ResetForTests()`. **Known trade-off:** the ID-formula fix reshuffled one seed-42 long-run Religion balance test's outcome (a pre-existing flaky suite, excluded from `test-fast.sh`) — not re-baselined; revisit if M16 needs a stable religion baseline.
- **M15.95 — Civilization/Organization Unification.** ✅ COMPLETE 2026-09-17 — see commit `780b726`. Scoping found the migration much smaller than assumed: `Organization.Members` was already canonical on 3 production hot paths; the one real gap (civ-death path not clearing `Organization.Members`) is now fixed via `CivTracker.SetCharacterCiv` maintaining a three-sided invariant (`Memberships`/`Organization.Members`/`Civilization.Members`). `SuccessionCrisisEndYear`/`WarsAgainst`/`BorderTension`/`PeaceTreaties` turned out to be 100% dead fields on `Organization` (deleted, along with `IsAtWarWith`) — no back-compat shim needed pre-release.
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
