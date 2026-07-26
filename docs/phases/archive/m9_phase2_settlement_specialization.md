# M9 Phase 9.2 — Settlement Specialization

**Status:** COMPLETE — 2026-07-26.
**Depends on:** 9.1 (`docs/phases/archive/m9_phase1_economic_depth.md`) — done.
**Read first:** `docs/phases/m9_created_object_unification.md` (index), then this doc.

## Goal

9.1 gave every settlement a per-capita demand ratio for every non-vital resource
(`ResourcePressurePhase.BuildLedger`), but nothing yet rewards a settlement for being
consistently good at producing one thing — every settlement banks every resource type at the
same flat `WealthAccumulateRate`, and `RunMerchant` picks trades purely on raw opportunity/demand,
with no notion that some settlements are simply *better positioned* to export a given good. This
phase closes that gap with **settlement specialization**: a settlement that sustains a strong
per-capita ratio in one resource develops a growing production bonus in that resource, and
merchants from that settlement get a matching export bonus when trading it — a lightweight
comparative-advantage effect layered on the existing ledger, not a new trade-routing system.

Full trade-network topology (named routes, travel time/caravans, price/currency) stays out of
scope, same as 9.1 — Merchant trade remains the existing teleport-style store-to-store transfer
(`docs/design_session_decisions.md:415`). This phase only adds a production multiplier and an
export-side routing bonus, both keyed off one new per-settlement scalar.

## Design

### 1. Specialization tracking (`SettlementStub`)

Two new fields:
- `string? Specialization` — the non-vital resource key this settlement is specializing in, or
  null if none has stabilized yet.
- `float SpecializationStrength` — EMA-smoothed confidence in `[0, 1]`, same shape as
  `SmoothedCapacity`'s smoothing pattern.

Each tick, after `BuildLedger` computes normalized per-capita ratios (9.1), find the non-vital,
non-`bonus_*` key with the highest ratio ("this tick's candidate"). If the candidate matches
`stub.Specialization`, grow `SpecializationStrength` toward 1 via EMA
(`ResourcePressureConfig.SpecializationSmoothingAlpha`). If it doesn't match, decay
`SpecializationStrength` toward 0 at the same alpha; once it reaches (near) zero, switch
`Specialization` to the new candidate. A candidate whose ratio is below
`ResourcePressureConfig.SpecializationMinRatio` doesn't count — a settlement with no meaningful
surplus of anything shouldn't "specialize" in a rounding error.

This mirrors `CapacitySmoothingAlpha` deliberately: same damping-oscillation rationale (a
settlement's dominant resource can flicker tick-to-tick near a tie; smoothing prevents constant
specialization flip-flopping from being visible to players/trade logic).

### 2. Production bonus

In `ResourcePressurePhase.Execute`'s non-vital accumulation loop (post-9.1, reads `ledger` as a
per-capita ratio and multiplies by `WealthAccumulateRate`), apply an additional multiplier when
`res == stub.Specialization`:

```csharp
float specMult = res == stub.Specialization
    ? 1f + Math.Min(_cfg.SpecializationBonusCap, stub.SpecializationStrength * _cfg.SpecializationBonusScale)
    : 1f;
current += supply * _cfg.WealthAccumulateRate * specMult;
```

New config keys (`[resource_pressure]`): `SpecializationSmoothingAlpha`, `SpecializationMinRatio`,
`SpecializationBonusScale`, `SpecializationBonusCap` — same cap-pattern precedent as the 9.1
bonus-store consumers.

### 3. Merchant export bonus

In `Tier2BehaviorPhase.RunMerchant`'s opportunity scoring loop (`Tier2BehaviorPhase.cs:275-299`),
after the existing ally/demand weighting, add a specialization weight keyed on the *home*
settlement (a merchant trades best in what their home settlement is known for):

```csharp
float specWeight = res == home.Specialization
    ? 1f + home.SpecializationStrength * _cfg.MerchantSpecializationBonusScale
    : 1f;
opportunity *= demandWeight * specWeight;
```

New config key (`[character]`): `MerchantSpecializationBonusScale` (default modest, e.g. 0.5 — a
fully-specialized settlement's export opportunity gets up to 50% amplified, same order of
magnitude as `MerchantMaxDemandWeight`'s effect).

## Persistence

`SettlementStub.Specialization`/`SpecializationStrength` need DTO + mapper round-trip:
`WorldStateDto.cs` (`SettlementStubDto`) and `WorldStateMapper.cs` (`MapSettlements` /
settlement-load loop), same pattern as `ResourceStores`/`Unrest`.

## Testing

- Unit test: given a settlement with a sustained high per-capita ratio for one resource across
  several ticks, `Specialization` converges to that resource and `SpecializationStrength` rises
  toward 1.
- Unit test: switching dominant resource decays the old `SpecializationStrength` toward 0 before
  `Specialization` flips (no instant flip-flop).
- Unit test: stockpile growth for the specialized resource is measurably higher than an identical
  settlement with no specialization, and the bonus respects `SpecializationBonusCap`.
- Unit test: `RunMerchant` prefers exporting the home settlement's specialized resource over an
  equally-scored alternative once `SpecializationStrength` is nonzero.
- Reproducibility test must still pass unchanged (same seed → same world).
- Balance sweep: specialization amplifies non-vital stockpile growth for one resource per
  settlement — re-check `config/balance_invariants.toml [year_300]` mineral/timber/gold bands
  still hold; adjust `SpecializationBonusScale`/`Cap` defaults if it pushes them out.

## Definition of done

- `SettlementStub` carries `Specialization`/`SpecializationStrength`, persisted through
  save/load.
- Specialized resource production and merchant export both demonstrably respond to
  `SpecializationStrength`.
- Zero warnings, all tests green, `scripts/doc-check.py` clean, architecture tests unaffected,
  balance sweep confirms bands hold (or updated with rationale).
- Move this doc to `docs/phases/archive/`, update the index doc's status/phase table.
