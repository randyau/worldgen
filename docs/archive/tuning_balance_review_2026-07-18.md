> **Archived:** 2026-07-19 — Completed work; all Phases A-D implemented. Reference: docs/sim_tuning.md for active tuning guidance.

# Tuning & Balance Review — Codebase Improvement Report and Plan

**Date:** 2026-07-18
**Status:** IMPLEMENTED — all Phases A–D shipped (see outcome summary below)
**Scope:** How to make the simulation easier to tune and balance; mechanics that need
refactoring or rethinking; config/tooling hygiene. Based on a survey of `sim_config.toml`,
the config loader, the resource/population/territory phases, `UtilityScorer`, the M4 code,
`docs/sim_observations_and_proposals.txt`, and the last ~25 commits.

---

## Outcome Summary (2026-07-18)

All four phases from the proposal were implemented in the cleanup effort:

**Phase A — Headless runner + metrics (A1–A3 complete)**
- `Program.cs` now implements `RunSynchronous(ticks)` with full sim loop
- `MetricsCollector` samples world health once/year to `yearly_metrics` table
- `balance-run.py` script fans out multi-seed sweeps with `--compare` mode
- `--audit-food` diagnostic added to headless runner (`FoodAuditSink`)

**Phase B — Strict config loader (B1–B5 complete)**
- `SimConfigLoader` strict mode detects unbound TOML keys (no silent drift)
- Dead keys purged from `sim_config.toml`; ~3 dead properties removed this cleanup pass
- Profile overlay and `--set` programmatic overrides implemented
- Validation pass (`SimConfigValidator`) checks ranges and cross-field invariants
- `TicksPerYear` derived constant replaces hardcoded `16`

**Phase C — Balance regression harness (C1–C3 complete)**
- `balance_invariants.toml` with calibrated bands from 3-seed × 300-year sweep
- `BalanceRegressionTests` (Category=Balance) covering 2 seeds × 300 years
- `sim_tuning.md` reference doc for the 25 highest-churn knobs

**Phase D — Structural mechanics (D1–D5 complete)**
- D1: Population ceiling unified — removed dual carry_cap table, added EMA smoothing
- D2: Food audit diagnostic added to headless runner
- D3: `UtilityScorer` goal-affinity and action weights extracted to `[utility_affinity]` config
- D3: BiomeWildlifeRisk table extracted to `[wildlife_risk]` config section
- D4: Structural disease model — base × density × contact × famine factors
- D5: War system consolidated — all knobs to `WarConfig`; opportunistic war causes added;
  campaign battles fire annually regardless of character position; `WarOutcome`/`WarCause` typed

**Current sim behavior:**
- Wars fire regularly; multipolar civ life-cycle works; food binds carrying capacity
- `deaths_war` metric wired to conquest events (population-level estimate)
- `mean_food_ratio` < 1.0 confirms food scarcity is the primary growth constraint

---

## Executive Summary

The sim's balance problems are not primarily *number* problems — they are *feedback loop*
problems. The last two months of commits are dominated by calibration fixes (food/pop scale,
disaster rates, territory caps, disease churn), and each fix required: run the UI, wait,
export the DB, eyeball SQL, guess a new constant, repeat. Three structural gaps make every
tuning pass expensive:

1. **There is no headless batch runner.** `Program.cs` is a stub; the only way to run the
   sim is through the MonoGame UI. Every balance experiment is manual and single-seed.
2. **The config system silently lies.** `IgnoreMissingProperties = true` plus schema drift
   means roughly a quarter of `sim_config.toml` (~250 lines across ~12 sections) binds to
   nothing. Editing those keys does nothing; several duplicated sections show *different
   values* for the "same" constant.
3. **There is no metrics loop.** Balance health is assessed by ad-hoc SQL and python scripts
   after multi-thousand-year runs, with no defined target bands and no regression test that
   catches when a change breaks world health.

Fixing these three (Plan Phases A–C) converts tuning from open-loop guesswork into a
closed feedback loop. Phase D then addresses the specific mechanics that keep needing
re-tuning because of structural issues (dual population ceilings, per-tick vs per-year rate
confusion, hardcoded behavior tables, disease/war loop shape).

---

## Part 1 — Findings

### F1. No headless runner (highest-leverage gap)

`WorldEngine.Sim/Program.cs` parses `--seed` and `--years`, then does nothing
("`WorldGenPipeline.Run() will be called here once implemented (Phase 3)`" — Phase 3
shipped long ago). The architecture is already perfect for this — the sim is fully headless
by design (ADR rule #1) — but the entry point was never finished, so:

- Balance runs require launching the UI and letting it run at Ultrafast.
- No multi-seed sweeps: every calibration conclusion is drawn from n=1 seeds
  (the 5,876-year run in `sim_observations_and_proposals.txt` is one seed).
- No A/B: comparing two constant values means two manual UI sessions.
- No CI guard: nothing detects when a code change collapses all civs by year 200.

### F2. Dead and duplicated config — `sim_config.toml` silently diverges from reality

`SimConfigLoader` uses Tomlyn with `IgnoreMissingProperties = true` and performs no
validation. `SimConfig` has bound properties for only a subset of the TOML sections.
Sections with **no binding at all** (editing them does nothing):

| Dead TOML section | Where the real knobs live (if the system exists) |
|---|---|
| `[characters]` (+`.needs`, `.skills`, `.aging`) | `[character]` → `CharacterSimConfig` |
| `[utility]` | `[character]` (`needs_weight=0.5` there vs dead `0.40` here) |
| `[goals]` | `[character]` goal thresholds |
| `[civilization]` (+`.settler_seeding`) | partly `[character]` (`civ_floor_*`), partly nowhere |
| `[environment]` | `[disasters]` + `[climate]` |
| `[resources]` (top-level) | `ResourcesConfig` binds to `[world_gen.resources]` only — `fertility_recovery_per_year` etc. are **C# defaults**, edited in code to stay in sync (see commit history "was 1" comments in both files) |
| `[specialists]`, `[artifacts]`, `[cultural_modifiers]`, `[admin_distance]`, `[spatial_buffer]`, `[performance]` | unimplemented or implemented elsewhere |

Concrete traps this has already caused or invites:

- Commit `6981883` ("align hardcoded CharacterSimConfig defaults with sim_config.toml")
  is exactly this failure mode surfacing.
- `[characters.needs] food_decay = 0.10` vs `[character] needs_decay_food = 0.08` — a
  tuner editing the first sees no effect and draws a false conclusion about the mechanic.
- `tier2_per_population` appears as `200` in dead `[civilization]` and `10` in live `[character]`.
- `autosave_interval_ticks`: `960` in `[sim_loop]`, `40` in dead `[performance]`.
- Two different `suppressed_types` lists (`[events]` live, `[events.gate]` legacy).
- Disaster probabilities appear in both dead `[environment]` and live `[disasters]` with
  values that differ by 100× (`wildfire_prob = 0.0005` vs
  `wildfire_ignition_probability_per_tick = 0.000003`).

The file header promises a profile system (`config/profiles/` overlays) that the loader
does not implement.

### F3. Balance-critical constants still hardcoded — but the worst offenders are *tables*

The "all constants in SimConfig" rule is mostly followed for scalar rates, but the
highest-impact tuning surfaces are hardcoded C# tables and expressions:

- `UtilityScorer.cs` (~70 float literals): the **action base-score formulas** (how each
  need maps to each action), the **goal→action affinity matrix**, wellbeing → social
  multiplier bands, trust thresholds (`0.4f`, `0.7f`), raid aggression minimums. This file
  *is* Tier-1 character personality at the system level — it should be the most tunable
  thing in the project and is currently the least.
- `PopulationDynamicsPhase.cs`: `BiomeWildlifeRisk` per-biome multiplier table, the
  `pop > 200` safety cutoff, `0.3f + nearby * 0.1f` safety formula, `TicksPerYear = 16`
  as a private const.
- `ResourcePressurePhase.cs`: `TimberPerForestTile = 0.5f`.
- `CivTracker.*`: a handful of scattered thresholds.

Note the pattern: per-biome tables *did* make it to config for carry capacity
(`carry_cap_*`) and food (`biome_food_bonus_scale`) — the same treatment is needed for
wildlife risk, action scoring, and goal affinity.

### F4. No metrics pipeline — tuning is open-loop

Current workflow: run for thousands of years → open `world.db` → run SQL from
`docs/queries/event_log_queries.md` or `scripts/world-sanity.py` → interpret. Problems:

- Aggregates are computed *after the fact* from the event log, so anything suppressed by
  the event gate (deliberately, for DB size) is invisible to balance analysis. You cannot
  see the food-ratio distribution over time, per-settlement factor breakdowns, or
  population-by-biome curves from events.
- There are no *defined targets*. What counts as healthy? ("4–12 active civs at year 500",
  "settlement 100-year survival rate > 40%", "at least one conquest per century of war")
  These exist implicitly in commit messages and the observations doc, but nowhere machine-checkable.
- The excellent `docs/worldgen_tuning.md` format (knob / current / safe range / artifact
  at each extreme) exists only for world gen, not for the sim, where tuning churn is far higher.

### F5. Time-base confusion is a recurring bug factory

Rates in config are variously per-tick, per-season, per-year, and per-century, with the
tick/year relationship itself inconsistent:

- `PopulationDynamicsPhase` hardcodes `TicksPerYear = 16`; a comment in `[climate]` says
  "~14 ticks/year"; `[sim_loop]` implies 4 ticks/season × 4 seasons; comments in
  `[disasters]` assume 16.
- The Tier-2 lifespan bug (`tier2_max_age_seasons_min = 60` → 4-year physicians, item A1
  of the observations doc) was precisely a seasons-vs-years unit error.
- Ratio-pairs that only matter relative to each other are expressed as absolute rates in
  different units (e.g. `fertility_recovery_per_year` vs `drought_fertility_penalty_per_season`
  — the commit note literally says "ratio was 1:5, now 1:1", meaning the *ratio* is the
  real knob).

Every constant a tuner touches requires mental unit conversion, and the penalty for
getting it wrong is a silent 16× error.

### F6. Mechanics that need structural rethinking (not just retuning)

These are the systems whose constants keep getting revisited because the *shape* of the
mechanic fights the tuning:

**F6a. Two overlapping population ceilings.** Population is capped both by biome carrying
capacity (`[settlement] carry_cap_*`, logistic suppression in `PopulationDynamicsPhase`)
and by the food ledger ratio (`[resource_pressure]` fertility × moisture-floors ×
growing-season × biome-multiplier × improvements). These encode the same real-world
concept twice with two independent parameter sets that must be co-tuned — the recent
food/pop calibration commits (`1d32683`, `e56ab90`, `cb0725d`) had to touch both. Either
system alone can silently become the binding constraint, making the other system's knobs
appear dead.

**F6b. The food model has too many multiplicative factors to reason about.** Effective
food per tile ≈ fertility × max(moisture, prop-floor, abs-floor) × growing-season(temp,
with cold-hardy floor) × biome multiplier × improvement multiplier × hinterland drain,
then through store accumulation/spoilage/draw. Six-plus factors multiplied means any one
being 0.5× off is invisible, and floors were patched in twice (`food_moisture_floor`,
then `food_moisture_absolute_floor`; then `cold_hardy_food_floor`) to rescue specific
biomes. The anchor-scale idea already present (`people_per_tile_peak` — "single scale
factor for world population") is the right pattern; it needs a diagnostic to go with it.

**F6c. Disease is tuned by flat probability, so it whack-a-moles.** The 5,876-year run
showed disease as the dominant world dynamic (13 outbreaks/year, 97% settlement
abandonment); it was fixed by halving/quartering constants. But the model is still
"annual coin flip × density multiplier," so any future change to settlement count or
density re-breaks it. A structural coupling (outbreak risk derived from population
density over carrying capacity, trade/contact links, and active-war status) would make
it self-balancing and produce better narrative causality.

**F6d. War produces tension → truce loops.** M4 Phase 2 added campaign battles and
territory transfer, which addresses observation A3, but the *causes* remain narrow
(border tension + character encounter), and war knobs are split across `[character]`
(max_war_duration, tension_*, war_exhaustion, peace_cooldown, raid damage) and `[war]`
(campaign battles, tiles transferred). Consolidating into one `[war]` section with the
tension model, resolution model, and cost model together would make the loop tunable as
a unit. Opportunistic war causes (succession crisis, disease-weakened neighbor, resource
shortage) from observation A4 remain open.

**F6e. Civ spawn placement ignores biome viability.** (Tracked in memory: tundra spawn
ruins.) Spawning uses fertility thresholds but not biome/latitude weights, so cold-band
civs spawn, struggle against the cold-hardy floor economics, and litter ruins. Spawn-time
weighting is cheaper than making tundra artificially viable.

**F6f. Behavior emergent-rate constants have no observable.** Things like
`civ_birth_chance_per_season`, `wanderlust_*`, goal thresholds control *rates of emergent
events* (new civs/century, goals formed/character/lifetime). Nobody can predict the
emergent rate from the constant; only a metrics run reveals it. This is not fixable in
the constant — it requires F4's metrics loop. (Observation A5 — emotional events never
firing — was this exact class of bug.)

### F7. What's already good (preserve these patterns)

- The command/resolve architecture and headless sim boundary are clean and are exactly
  what makes a batch harness cheap to build.
- `WorldRng.FloatAt(seed, tick, x, y, salt)` determinism → sweeps are reproducible.
- `worldgen_tuning.md` is a model tuning document.
- Extensible resource ledger (string-keyed) — new resources without code changes.
- Pre-baked lookup tables in `ResourcePressurePhase` (config → 256-entry tables at
  construction) is the right performance pattern for config-driven curves.
- `scripts/world-sanity.py`, `civ-history.py`, `character-analysis.py` are the seed of
  the metrics pipeline — they need a data source better than the gated event log.

---

## Part 2 — Plan

Ordered by leverage. Phases A–C are tooling/infrastructure (~independent of game code
churn, low risk); Phase D is mechanics work that becomes far cheaper once A–C exist.
Suggested as the next milestone's Phase 0–2 before any further balance constant changes —
every future tuning pass repays the cost.

### Phase A — Headless batch runner + metrics (do first)

**A1. Finish `Program.cs`.**
`WorldEngine.Sim --seed N --years Y [--config path] [--profile name] [--out dir]`
runs world gen + sim loop with no UI, writes `world.db` and a metrics file, prints a
one-screen summary (final civ count, population, settlements, war/conquest totals,
biggest anomalies). Reuses `SimLoop`/`PhaseRunner` untouched.

**A2. MetricsCollector in the sim.**
A per-year sampler (sim-thread, cheap aggregates only) writing a `yearly_metrics` table
in `world.db` (or CSV): world population, active civs, settlements
(founded/abandoned/conquered), deaths by cause, mean/min food ratio, settlements in
shortage/crisis, active diseases, wars active/declared/ended by outcome, characters by
tier, goals formed/resolved by type, wellbeing distribution buckets. Independent of the
event gate, so suppressing an event type never blinds balance analysis.

**A3. Sweep script.** `scripts/balance-run.py --seeds 10 --years 1000 [--set key=value]`
fans out headless runs (processes, not threads — SimLoop is single-instance),
aggregates metrics across seeds, and diffs two configurations side by side. This is the
A/B tool every calibration commit so far lacked.

### Phase B — Config hygiene (small, mechanical, high trust payoff)

**B1. Strict loader mode.** On load, walk the TOML document model and warn (dev builds:
throw) for any table/key that did not bind to a config property. This single change makes
every F2 trap impossible to reintroduce.

**B2. Purge and reconcile `sim_config.toml`.** Delete dead sections (`[characters]`,
`[utility]`, `[goals]`, `[environment]`, top-level `[resources]`, `[civilization]`,
legacy `[events]` keys, duplicate `suppressed_types`, …). Where a dead section describes
a *future* system (`[admin_distance]`, `[spatial_buffer]`), move it to
`docs/config_future.md` rather than leaving live-looking dead keys. Bind top-level
`[resources]` runtime keys (fertility recovery etc.) to a real property or fold them into
`[disasters]`/`[resource_pressure]`.

**B3. Validation pass at load.** Range checks (probabilities in [0,1]), cross-field
invariants (weights sum to 1.0, `optimal_temperature_low < high`, threshold orderings),
fail fast with the TOML key name in the message.

**B4. Implement config profiles.** The promised `config/profiles/*.toml` overlay merge
(base → profile → CLI `--set` overrides). Needed by A3, and lets you keep e.g.
`fast_history.toml`, `harsh_world.toml` as first-class artifacts.

**B5. One time base.** Add a `TimeScale` (ticks/season, ticks/year) derived from
`[sim_loop]` and injected everywhere `TicksPerYear = 16` is currently assumed. Adopt a
config convention: **all rates in TOML are per-year** (suffix `_per_year`), converted
once at load into per-tick fields on the config objects. Migrate incrementally —
per-tick keys keep working until touched, but new/edited keys follow the convention.
Where a ratio is the real knob (fertility recovery vs drought penalty), expose the ratio.

### Phase C — Balance regression harness

**C1. Define world-health invariants** (start from the observations doc + recent commit
targets), e.g.: at year 500 with default config, across 5 seeds — active civs in [4, 12];
world population in [X, Y]; settlement 100-year survival > 40%; ≥1 conquest per 200 years
of active war; no single death cause > 60%; at least N GoalFormed/CharacterGrieved
events (guards observation-A5 regressions).

**C2. `BalanceRegressionTests`** — an xUnit category (or nightly script) that runs the
headless sim ~500 years on 2–3 seeds and asserts the bands. Marked `[Trait("Category",
"Balance")]` and excluded from the fast suite. Any mechanic change that nukes world
health now fails a test instead of surfacing three sessions later.

**C3. `docs/sim_tuning.md`** — the `worldgen_tuning.md` format applied to the ~30
highest-churn sim knobs: current value, safe range, observable symptom at each extreme,
and *which metric in A2 to watch*. Populate it as knobs get touched, not speculatively.

### Phase D — Mechanics refactors (ordered, each independently shippable)

**D1. Unify the population ceiling.** Make carrying capacity *derived from* the food
ledger (capacity = sustainable population at foodRatio = 1.0 given the territory's
computed yield) instead of a parallel `carry_cap_*` table. One system, one parameter
set; biome differentiation already lives in the food model. Keep `carry_cap_minimum`
as the floor. This removes ~14 config keys and the co-tuning trap (F6a).

**D2. Food-audit diagnostic.** A headless flag (`--audit-food <settlement|all>`) that
prints the per-tile factor breakdown (fertility, moisture after floors, growing season,
biome, improvement, drain → contribution) and the settlement rollup. Turns "why is this
tundra town starving" from a debugging session into one command. (Cheap: the factors
already exist in `BuildLedger`; it's a formatting exercise.)

**D3. Extract behavior tables to config.** Move `UtilityScorer`'s goal→action affinity
matrix and action base-score weights, plus `BiomeWildlifeRisk`, into TOML tables (same
pattern as `carry_cap_*` / pre-baked lookup arrays). Do these three first; chase the
remaining scattered literals opportunistically under the strict loader, not as a big-bang
sweep.

**D4. Structural disease model.** Outbreak probability = f(pop / carrying capacity,
contact links via trade/emissary/war, active famine), replacing part of the flat annual
chance. Mortality and spread keep their current knobs. Self-balances as world density
changes; produces causally-linked narratives (siege → plague) that the causal-edge
builder can pick up for free.

**D5. War system consolidation + causes.** Move the `[character]` war keys into `[war]`;
add opportunistic war causes (succession crisis window, diseased/starving neighbor,
resource-deficit-driven) each with its own `WarCause` enum value so the history log and
cultural traits can distinguish them. The M4 Phase 2 outcome machinery already gives
these causes real consequences.

**D6. Biome-weighted civ spawning.** Spawn-site scoring multiplies in a per-biome weight
table (config), heavily down-weighting tundra/desert; also fixes the tundra-ruins issue
from the backlog. ~Half-day with the harness from Phase A to verify spawn distribution.

### Sequencing and effort (rough)

| Phase | Effort | Depends on |
|---|---|---|
| A1–A3 runner+metrics+sweep | 2–3 sessions | — |
| B1–B5 config hygiene | 1–2 sessions | — (B4 feeds A3) |
| C1–C3 regression harness | 1 session | A |
| D1 unify pop ceiling | 1–2 sessions | A (to verify), C (to guard) |
| D2 food audit | 0.5 session | A1 |
| D3 behavior tables | 1 session | B1 |
| D4 disease model | 1–2 sessions | A, C |
| D5 war consolidation | 1–2 sessions | B2 |
| D6 spawn weights | 0.5 session | A |

**Recommended order:** A1 → B1/B2 → A2/A3 → C1/C2 → then D in listed order.
A1 and B1/B2 alone would have prevented or shortened most of the calibration commits
in the current log.

---

## Open questions for review

1. **Metrics storage:** `yearly_metrics` table inside `world.db` (queryable alongside
   events, survives with the save) vs separate CSV per run (simpler diffing)? Proposal: both —
   table as source of truth, sweep script exports CSV.
2. **How strict should strict mode be?** Warn-only in release, throw in Debug/tests is
   the proposal; throwing always would break old profile files.
3. **D1 (unified pop ceiling) changes world outcomes** and will break reproducibility
   against old saves/seeds by design. OK to schedule it before any long "canonical" runs
   you care about, or should it wait behind a config flag?
4. **Scope check:** is a balance milestone (A–C + selected D) worth inserting before M4
   Phase 4 content work, or should A1+B1+B2 be squeezed in as a mini-phase and the rest
   deferred? My recommendation: the mini-phase at minimum — it's ~3 sessions and
   de-risks every subsequent balance commit.
