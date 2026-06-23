# Phase 2.4 — Tier 3 Population

**Milestone:** 2 — The Character System  
**Status:** COMPLETE — 2026-06-23  
**Goal:** Activate `SimPhase.PopulationDynamics` (currently a stub). Settlements grow and shrink based on food supply (fertility) and safety. Population pressure crystallizes new Tier 2 specialists. Population-driven events (SettlementGrew, SettlementShrank, SettlementAbandoned) feed the history log. This replaces the static `Population = 50` stub in SettlementStub with living dynamics.

**Companion docs:**
- `docs/implementation_decisions_v0.3.md` §19 (Settlement Population Thresholds), §23 (Significance Classification — Tier 3 noise suppression)
- `docs/mvp_spec.md` — Phase 2.4 scope

---

## Scope

Phase 2.4 focuses on aggregate population mechanics, not individual people:
- `SettlementStub.Population` updates each season based on growth/shrink rate
- Growth rate formula: `FertilityBonus × SafetyBonus × BaseRate − DecayRate`
- Shrink/abandonment: below minimum viable population
- New Tier 2 specialist spawned when population crosses a threshold
- Population events: SettlementGrew, SettlementShrank, SettlementAbandoned (suppressed by default — too noisy)

Administrative distance penalty is **deferred to Phase 2.5** — the influence map requires road/river traversal cost data that doesn't exist yet in TileData.

---

## Stories

### Story 2.4.1 — SettlementStub becomes mutable
- Currently `SettlementStub` is a record — change `Population` and `Health` to mutable fields
- OR keep record, store in `WorldState.Settlements` as a dict that replaces on update (current pattern)
- **Decision:** keep record pattern, update by replacing in dict. No struct change needed.
- Add `sim_config.toml [settlement]` section with growth/decay rates and thresholds

### Story 2.4.2 — PopulationDynamicsPhase
- `PopulationDynamicsPhase.cs` — runs each tick:
  - For each settlement: compute growth = `tile.Fertility/255f × SafetyScore × cfg.PopGrowthRate`
  - `SafetyScore` = clamp(0,1) of nearby Tier1/Tier2 presence + base ambient safety
  - Apply growth to Population (float accumulator, integer floor)
  - Apply decay: `cfg.PopDecayRate` per season (hunger, disease stub)
  - If Population < `cfg.SettlementMinViablePop` → mark for abandonment
  - Cap at `cfg.SettlementMaxPop`
- Wire into `PhaseRunner.RunTick` replacing the `RunPhaseStub(SimPhase.PopulationDynamics)` call

### Story 2.4.3 — Specialist crystallization from population pressure
- When Population crosses thresholds (from §19 of impl decisions), spawn a Tier 2 specialist
  - 200 → Artisan, 300 → Scholar, 500 → Physician, 1000 → Merchant (simplified subset)
- Each threshold fires once per settlement (track `LastCrystallizedThreshold` in SettlementStub)
- Fires `AppointedToRole` event for the new specialist

### Story 2.4.4 — Population events + EventTypes
- `SettlementGrew = 3401`, `SettlementShrank = 3402`, `SettlementAbandoned = 3403`
- `SettlementAbandoned` removes settlement from world, fires as Regional tier
- `SettlementGrew`/`Shrank` default to Background (suppressed per §23 noise policy)
- Add to VerbClassification and SignificanceClassifier

### Story 2.4.5 — Tests
- `PopulationDynamicsTests.cs`: high fertility → growth; low fertility → decay; abandoned below min
- `CrystallizationFromPopTests.cs`: population crossing 200 threshold spawns an Artisan

---

## Definition of Done
- `PopulationDynamicsPhase` runs each tick; settlement population updates visibly
- `SettlementAbandoned` event fires and appears in event log
- Tier 2 specialist crystallizes when settlement hits threshold population
- 225+ tests pass, zero warnings
