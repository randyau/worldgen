# M11 Phase 0 — Long-Run Performance Profiling & Optimization

**Status:** COMPLETE — 2026-07-27

## Scope

From `docs/roadmap.md` § M11 (Scale & Distribution):

> Long-run performance: profile and optimize 10k+ year runs (event volume, DB growth, snapshot
> cost); confirm the disk-as-record model holds at scale.

This phase is a measure-first pass: run the existing headless runner (`WorldEngine.Sim`
`Program.cs`, driven via `dotnet run --project WorldEngine.Sim -c Release -- --seed N --years Y`)
out to 10k+ years, capture real numbers for wall-clock/tick, DB file growth, event volume, and
snapshot-build cost, then fix whatever the numbers say is actually the bottleneck — not a
preemptive rewrite of subsystems that turn out to be fine.

## Baseline run

- Seed 42, 10,000 years, `-c Release`, via the headless runner.
- Metrics captured: total wall time, ticks/sec, ticks/sec trend over the run (early vs. late —
  looking for superlinear slowdown, which would indicate an unbounded in-memory collection),
  final `world.db` size, `Events` table row count, `yearly_metrics` row count.

## Findings

**Baseline (seed 42, 10,000 years / 160,000 ticks, `-c Release`):**
- Wall time: 8,275.9s (2h18m), average 19 ticks/sec.
- Rate was not flat — it degraded through the run: ~99 years/min in the first ~84 min, falling to
  ~29 years/min in the final stretch. A ~3x slowdown from start to finish, i.e. superlinear
  per-tick cost growth, not a flat per-entity cost.
- Final: 75,394 population, 28 active + 129 collapsed civs, 107 settlements (1,310 ever founded),
  1,727,929 total events, 531MB `world.db`.

**Root cause:** `PhaseRunner.RunTick` called `EventStore.BuildSummaries()` unconditionally every
50 in-game years (200 times over the run) to keep `CivHistoryPanel`'s pre-aggregated tables
(`CharacterSummaries`, `CivSummaries`, `SuccessionChain`, `Dynasties`, `Eras`, causal edges,
significance rescoring) fresh. Every sub-builder does a full `DELETE FROM <table>` followed by an
unfiltered rescan of the entire `Events` table (no "since last rebuild" bound) — see
`SummaryBuilder.cs`, `CausalEdgeBuilder.cs`, `SignificanceRescoringPass.cs`. Cost per call scales
with *total* historical event count, and it's called repeatedly across the run, so cumulative cost
is roughly quadratic in final event count. Worse: the headless runner (`Program.cs`) never calls
`GetHistoryQuery()` mid-run — only the interactive UI's `CivHistoryPanel` reads it — so every one
of those 200 rebuilds during a headless run was pure waste.

**Fix (commit pending):** `SimLoopConfig.SummaryRebuildIntervalYears` (default 50, matches prior
hardcoded cadence) replaces the hardcoded `% 50` check in `PhaseRunner.RunTick`; `0` disables
automatic rebuilds entirely. `Program.cs` (headless runner) sets it to `0` and instead calls
`BuildSummaries()` exactly once, after the run completes, so the produced `world.db` still has
queryable summaries for post-run analysis without paying the repeated-rescan cost during the run.
Interactive play (`Game1.cs`/`SimLoop.Run()`) is unaffected — same default cadence as before.

**Validation:** re-ran seed 42 for 3,000 years post-fix — sustained 65-66 ticks/sec near the end
of the run, matching the run's own overall average (65 ticks/sec), i.e. flat, not degrading. That's
already ~2.5-3x faster than the baseline's blended average (19 ticks/sec) and faster than even the
baseline's *best* early-run rate, with no sign of the superlinear falloff. A full 10k-year re-run
to get a directly comparable final number was not repeated given the already-clear trend match
(cost of an extra 2+ hour run vs. the confidence already gained was not worthwhile).

**Disk-as-record model:** holds fine at this scale — WAL mode + batched transactions kept the DB
responsive throughout even at 1.7M+ events; 531MB for 10,000 years / 75k final population is not
concerning for a local dev save. No changes needed here in this phase.

**Also shipped in this phase (not a fix, but needed to observe any of the above):**
`SimLoop.RunSynchronous` now accepts an optional `IProgress<(int TicksDone, int TotalTicks)>`
reported once/simulated-year; `Program.cs` logs it throttled to
`SimLoopConfig.HeadlessProgressIntervalSeconds` (default 10s). Long headless runs were previously
completely silent until finished, which is what made this investigation slow to start — there was
no way to tell a healthy multi-hour run from a hung one.

## Remaining candidates (not yet investigated — only pursue if further profiling shows they matter)

The `BuildSummaries` fix explains the observed superlinear slowdown; it's plausible other, smaller
costs exist (e.g. `world.Civilizations.Values` iteration touching ~157 civs/tick including
collapsed ones already filtered early in most call sites — looks cheap, not worth touching without
evidence). Do not optimize further speculatively; re-profile a full 10k-year run first if a future
milestone needs still-lower wall time than the ~2.5-3x improvement already measured here.

## Phase sequence

| Phase | Depends on | One-line deliverable |
|-------|-----------|----------------------|
| 11.0 | — | Baseline 10k-year profiling run; root cause identified (`BuildSummaries` unconditional periodic full-rescan); config-gated fix + progress logging shipped; validated via 3k-year re-run. COMPLETE. |

## Non-negotiable constraints

From `CLAUDE.md`:
1. Any new tunable (batch sizes, cache eviction thresholds, etc.) goes in `SimConfig`/
   `sim_config.toml` — never hardcoded.
2. The reproducibility test must still pass after any optimization (same seed ⇒ same history).
3. `WorldEngine.Sim` stays headless; this phase touches no UI code.
4. Disk stays the system of record — no change here should make in-memory state authoritative
   for anything that needs to survive a crash.
