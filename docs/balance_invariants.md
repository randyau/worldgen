# Balance Invariants — Philosophy and Update Procedure

**Related files:**
- `config/balance_invariants.toml` — the actual band definitions (machine-readable)
- `WorldEngine.Tests/Balance/BalanceRegressionTests.cs` — the test that enforces them
- `scripts/balance-run.py` — the tool for calibrating bands

---

## What these invariants are

`config/balance_invariants.toml` defines **expected-healthy ranges** (bands) for key
simulation metrics at specific checkpoint years. The balance regression test
(`BalanceRegressionTests`) runs the sim for each seed and asserts every band.

These bands are **not aspirational targets**. They describe what the sim _currently does_
when healthy, with generous margins (~±40% around observed). They exist to detect
regressions: when a mechanic change collapses all civs, freezes population, or silences
goal formation, the test fails before the change lands in `main`.

---

## Philosophy: observed-healthy ± margin

Every band was derived from an actual multi-seed calibration run:

```
python3 scripts/balance-run.py --seed-list 42,777,9999 --years 300 --label baseline
```

The observed min/max across seeds defines the "healthy envelope." The band min/max
extend ~40% beyond that envelope to give breathing room for normal seed variance while
still catching structural failures.

**Do not tighten bands speculatively.** Tight bands break on normal variance and erode
trust in the harness. Tighten empirically: after a change makes the sim healthier and
you have 5+ seeds of data confirming the new envelope.

---

## When to update the bands

| Situation | Action |
|---|---|
| A mechanic change **legitimately shifts** a metric (e.g. D1 pop-ceiling unification shifts world_population) | Re-calibrate: run the sweep, compute new observed range, widen or shift the band, document in the TOML rationale field |
| A band fails but you don't know why | Investigate the root cause — do not widen |
| You've confirmed the simulation is healthier after a fix | Tighten to new envelope (optional, improves harness sensitivity) |
| A new major mechanic is added (e.g. disease model D4) | Re-calibrate all disease-related bands after the mechanic ships |

---

## Phase D migration notes

The following upcoming phases will require band re-anchoring:

- **D1 (pop-ceiling unification):** `world_population` and `settlements_total` bands will
  shift because the unified ceiling changes how aggressively settlements grow. Re-calibrate
  immediately after D1 ships and before any further balance work.

- **D4 (structural disease model):** `active_diseases` and `deaths_disease` bands will
  shift significantly. The current band for `active_diseases_max` (8) was set when disease
  is effectively dormant at default density.

- **D5 (war consolidation + opportunistic causes):** `wars_active` bands. Currently 0 wars
  observed across 300 years — the test only caps the maximum. After D5, wars should occur
  regularly and a minimum wars-per-century assertion becomes meaningful.

---

## Calibration procedure

```bash
# 1. Run multi-seed sweep (takes ~3 minutes for 3 seeds × 300 years)
python3 scripts/balance-run.py --seed-list 42,777,9999 --years 300 --label post-D1

# 2. Inspect the Year 300 section of the output table
# 3. Set band min = observed_min × 0.6, band max = observed_max × 1.4
# 4. Update config/balance_invariants.toml with new values + update rationale
# 5. Run the balance test suite to confirm green
scripts/test-balance.sh
```

---

## Adding a new invariant

1. Add the band to `config/balance_invariants.toml` under the appropriate `[year_NNN]` section
2. Add the corresponding assertion in `BalanceRegressionTests.cs`
3. Run a calibration sweep to verify the initial band is achievable
4. Document the `rationale` field in the TOML inline comment
