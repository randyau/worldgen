# M16 — Disasters, Reworked

**Status:** COMPLETE — 2026-09-17. All phases (16.0, 16.1, 16.1b, 16.2, 16.3) shipped same day.

See `docs/roadmap.md` § "M16" for the one-line scope statement: wire real consequences onto
disasters (destroyed settlements/improvements, ash-driven famine, displacement); expand variety
(blight, harsh winter); multi-year recovery arcs instead of single-tick events.

## Audit (2026-09-17)

Traced every consumer of `ActiveDisaster`/`ActiveTileDisasters`: disasters (wildfire, flood,
volcanic eruption, earthquake, drought) currently only ever touch `TileDynFlags`, tile fertility
(drought only), and the causal-graph `SimEvent` log. **Zero** disaster ever reads or writes
`WorldState.Settlements` or `WorldState.ImprovementMap` — a volcano can erupt directly under a
capital and nothing happens to it. Drought is the one disaster with a real consequence today
(fertility penalty → `ResourcePressurePhase` food ledger → `PopulationDynamicsPhase` decay), and
it's the template for everything else: reuse existing substrates rather than building parallel
famine/displacement systems.

Reuse targets found:
- `SettlementStub.Health` (0–100, "0 = destroyed") is already the war-damage substrate
  (`CivTracker.War.ResolveRaid`), already recovers passively per tick
  (`PopulationDynamicsPhase.HealthRecoveryPerTick`) — this **is** the multi-year recovery arc,
  no new timer needed.
- `CivTracker.RegisterRuin(tile, stub, cause, world, pending)` takes a free-text `cause` string
  already ("war_damage"/"abandoned"/"destroyed") — adding `"disaster"` is a zero-schema-change
  extension.
- `ResourcePressurePhase`'s food ledger is built from `ImprovementMap` + tile fertility every
  tick — destroying a `Farm` improvement or dropping fertility (already the drought mechanism)
  automatically starves a settlement through the existing pipeline. No new famine system needed.
- `RunAnnualResourceDynamics`'s drought/burned-forest fertility branches are the template for
  ash-driven famine: same "penalty while active, floor, natural per-year recovery" shape.

## Phase plan

- **16.0 — Wire consequences onto existing disasters.** Wildfire/flood/earthquake/eruption damage
  a co-located settlement's `Health` (reusing the raid-damage → destroy-at-zero pattern, cause
  `"disaster"`) and roll a chance to destroy a co-located `TileImprovement`. Volcanic ash gets a
  finite duration (was indefinite) and a fertility penalty while active, mirroring drought —
  ash-driven famine falls out of the existing food-ledger pipeline for free once ash suppresses
  fertility. New events: `ImprovementDestroyed`, `SettlementDamagedByDisaster`; `SettlementDestroyed`
  reused for disaster-caused total destruction.
- **16.1 — New disaster types.** ✅ Blight shipped 2026-09-17: `DisasterType.Blight`, rolled
  per-settlement per-year (not tile-scoped like wildfire/flood/earthquake/eruption), reuses
  `ActiveTileDisasters` keyed by the settlement's own tile — no new state track. Destroys a
  config fraction of stored food on onset (`BlightFoodStoreDestructionFraction`) and suppresses
  fertility on the settlement tile while active (same penalty/floor shape as drought/ash), so a
  blighted settlement starves through the same `ResourcePressurePhase` pipeline. Also eligible
  for the generic improvement-destruction roll from 16.0 (a blighted farm can be destroyed too).
  New event `BlightBegan`. `DisasterType` enum's old `// V2: Plague, Blight, ArmyPresence`
  comment is superseded — Blight is now implemented; Plague/ArmyPresence remain V2.
- **16.1b — Harsh winter.** ✅ Shipped 2026-09-17. Global, not tile-or-settlement-scoped (unlike
  every other disaster) — a single `WorldState.HarshWinterTicksRemaining` counter, rolled once
  per year and persisted through `WorldStateDto`/`WorldStateMapper` like
  `VolcanicActivityMultiplier`. Consumed by `PopulationDynamicsPhase` (extra settlement decayF)
  and `CharacterBehaviorPhase` (per-year character Health drain, killing on 0 like disease).
  New event `HarshWinterBegan`.
- **16.2 — Narrative/UI parity.** ✅ Shipped alongside 16.0/16.1/16.1b: `Presenter.EventVerbPhrase`
  labels for `ImprovementDestroyed`/`SettlementDamagedByDisaster`/`BlightBegan`/`HarshWinterBegan`.
  Ruin-cause UI parity needed **no new code** — `TileInspectorPanel` already renders
  `RuinRecord.Cause` as raw text, and `RegisterRuin`'s free-text `cause` parameter meant
  `"disaster"` shows up distinctly from `"war_damage"`/`"abandoned"`/`"destroyed"` automatically.
- **16.3 — Balance pass.** ✅ Shipped 2026-09-17:
  `WorldEngine.Tests/Balance/DisasterBalanceInstrumentationTests.cs`, 3-seed/300-year sweep (same
  discipline as M14/M15's balance suites). Observed at default config: `ImprovementsDestroyed`
  1–33, `SettlementsDamaged` 0–210, `Blights` 17–25, `HarshWinters` 8–9, `DisasterRuins` 0 across
  all three seeds — consequences fire meaningfully without ever reaching total settlement
  destruction or wiping out a civilization in a 300-year window. No constant needed retuning.
