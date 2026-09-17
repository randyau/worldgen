# M16 — Disasters, Reworked

**Status:** IN PROGRESS — started 2026-09-17.

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
- **16.1 — New disaster types.** Blight/crop disease (direct fertility/food-store hit, no tile
  spread — a settlement-scoped disaster rather than tile-scoped, first of its kind) and harsh
  winter (seasonal, character `Health`/need drain, mirrors existing character disease-drain
  pattern). Both config-driven, both feed the same food-ledger/Health substrates as 16.0.
  `DisasterType` enum comment currently says `// V2: Plague, Blight, ArmyPresence` — that's
  superseded by this milestone's explicit roadmap scope for Blight; Plague/ArmyPresence remain V2.
- **16.2 — Narrative/UI parity.** Event log labels for the new event types; ruin cause "disaster"
  surfaced distinctly from "war_damage"/"abandoned" where the UI shows ruin history.
- **16.3 — Balance pass.** Long-run instrumented test (same shape as M14/M15's balance suites)
  confirming disaster-driven abandonment/destruction rates are meaningful but not
  civilization-ending at default config.
