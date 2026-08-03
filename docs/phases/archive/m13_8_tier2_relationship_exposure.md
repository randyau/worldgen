# M13.8 — Tier2 Relationship Exposure

**Status:** COMPLETE — 2026-08-03 (all four phases, 13.8.0-13.8.3, shipped same session).

Scoped 2026-08-03 as a follow-on to M13 before M14 begins (M13 itself is done — see
`docs/phases/archive/m13_generational_domestic_drama.md`).

See `docs/roadmap.md` § "Tier2 roles are fixed-behavior" for the existing open note on Tier2's
fixed-behavior model — M14/M18 want *variable role behavior*; this milestone is a different axis
(making Tier2 characters eligible *targets* of Tier1 relationship drama), but both share the same
underlying tension: Tier2 exists specifically so the sim doesn't have to run full utility-scored
AI on a population that can run into the thousands, and any expansion has to preserve that.

## Why this exists

Tier2 is meant to be a farm system: named, individually-trackable background characters that
`TryCrystallize` (`Tier2BehaviorPhase.cs`) promotes into full Tier1 heroes when they become
"important enough." Today, `TryCrystallize`'s gate is `Ambition > threshold && Status > threshold
&& random roll` — entirely internal to the character, with zero input from relationships or
events. The one place Tier2 already receives outside contact (`GrantAid`/`ForgiveDebt` accepting a
Tier2 recipient via the M13 balance pass's "shared homeland" shortcut) doesn't feed promotion at
all. So the intended arc — a background character gets pulled into enough Tier1-driven drama that
they crystallize into a hero — doesn't actually exist as a causal chain yet.

**The scale constraint:** Tier1 population is small (single digits to ~15 per civ). Tier2 is the
bulk background population and can be large. `UtilityScorer`'s per-tick action scoring is only
affordable because it runs once per Tier1 character, each scanning a bounded `PerceptionRadius`
around itself — it never iterates the full Tier2 population. Any expansion here must preserve that
invariant: **Tier2 never runs its own scorer, and no new mechanic may iterate all Tier2 characters
every tick.** The existing same-civ Familiarity source/sink (`ApplySameCivFamiliarity`, M13 13.6)
is the cautionary example of what NOT to extend to Tier2 wholesale — it iterates all Tier1-Tier1
co-located pairs, which is cheap only because Tier1 is scarce; the same approach over Tier2 pairs
would be the O(n²) cost the two-tier split exists to avoid.

The pattern that stays cheap is the one `GrantAid`/`ForgiveDebt` already use: **Tier1-initiated,
Tier1-scanned, Tier2-received.** Cost is bounded by Tier1 count × `PerceptionRadius`, not by Tier2
population size. This milestone generalizes that pattern to more action types and closes the loop
into crystallization.

## Phase sequence

- **13.8.0 — SHIPPED 2026-08-03 — Harden Tier1-only civ-level isolation (regression guard, built
  and merged before 13.8.1 enables Tier2 targeting).**
  Most rivalry-driven effects operate at civ level (war, alliance, territory), while a Tier2 is
  just a high-ranking specialist/citizen — it shouldn't be able to move those civ-level dials.
  Auditing every consumer of `IsRival` found this is *already* true today, but only as an
  accident of each loop's type filter, never as a deliberate, protected invariant:
  - War hostility check (`UtilityScorer.cs` ~322-324) — `if (e2 is not Tier1Character enemy ...)
    continue;`
  - Fear-based War/Raid dampening (`UtilityScorer.FearDampening`, ~827-833) — same guard on the
    rival lookup
  - Dominance goal target search (`GoalManager.FindNearbyRival`) and Alliance goal exclusion
    (`FindNearbyNeutral`) — both iterate `e is Tier1Character other`
  - Territorial pressure gate (`CharacterBehaviorPhase.cs` ~939-945) — same guard
  Before 13.8.1 relaxes `ResolveRivalry`/`ResolvePlacate`'s Tier1-only target guard (the one
  change actually required to let a Tier2 become a rival at all), make each of these five sites'
  Tier1-only behavior explicit and protected:
  - Add a `// Tier1-only by design — see docs/phases/m13_8_tier2_relationship_exposure.md` comment
    at each of the 5 sites.
  - Add a regression test (e.g. `Tier2RivalryIsolationTests.cs`) that constructs a Tier1 with a
    Tier2 rival and asserts none of War-eligibility, `FearDampening`, `FindNearbyRival`,
    `FindNearbyNeutral`, or the territorial-pressure gate react to it — one test file proving the
    structural invariant holds, mirroring the `ArchitectureRuleTests`-style "prove an invariant,
    don't just trust the current code shape" pattern already used this milestone for command
    dispatch (see `feedback_command_dispatch_wiring` memory).
  Pure hardening of already-correct behavior — no new mechanic, no balance risk — so this can ship
  independently and doesn't block on the rest of 13.8's design settling.
  **Shipped:** explicit comments at all 5 sites; `Tier2RivalryIsolationTests.cs` (5 tests) proving
  the invariant behaviorally, including a positive-control test (an equivalent Tier1 rival *does*
  justify war) to prove the Tier2 absence is caused by the type filter, not a missing precondition.

- **13.8.1 — SHIPPED 2026-08-03 — Tier2 as an eligible target for more Tier1 relationship actions.**
  Extend `UtilityScorer`'s candidate loops for `Bond`, `DeclareRivalry`, and `Placate` to accept a
  co-located Tier2 candidate, mirroring the existing `GrantAid`/`ForgiveDebt` Tier2 branch exactly
  (same `GetEntitiesInRadius` scan, same "is this a valid target" checks). Extend the matching
  `CivTracker.Resolve*` methods to handle a Tier2 target — `RelationshipEdge`/`RelationshipGraph`
  are keyed by generic `EntityId` pairs already, so no schema change expected there, but each
  Resolve method needs verifying it doesn't assume `Tier1Character` on both sides (several read
  `.CivId`/`.Personality` on the target directly today).
  **Marriage is in scope** per the design decision below: a Tier1 proposing marriage to a Tier2
  triggers immediate crystallization as part of resolving `ProposeMarriage` — the Tier2 is promoted
  to Tier1 first (reusing `TryCrystallize`'s promotion path, triggered rather than rolled), *then*
  the marriage resolves as an ordinary Tier1-Tier1 marriage. This keeps `RelationshipEdge`/Family
  Organization membership Tier1-only long-term — no Tier2-compatible childbirth/succession path
  needs to be built. Grief already has a working Tier2 precedent to mirror (`GoalManager.
  ApplyGriefToMourners` already notifies Tier1 mourners of a Tier2 death).

  **Shipped, plus two bugs the build surfaced and fixed along the way (not scoped in advance):**
  - **Bond's Tier2 path already existed** — `GoalManager.FindHighTrustCompanion` already lets a
    Bond goal target a same-civ co-located Tier2 (the M13 balance pass's "shared homeland" shortcut,
    reused for GrantAid/ForgiveDebt). Only the Marry candidate check (`UtilityScorer.cs` ~150-172)
    was still hard-typed to `Tier1Character`, silently dropping any Bond goal that happened to
    target a Tier2 — generalized to `SimEntity` (`Tier1Character or Tier2Character`).
  - **Promotion resets AgeSeason to 0** — `PromoteToTier1` (extracted from `TryCrystallize` so
    `ResolveMarriage` can trigger the same promotion path) spawns via `CharacterFactory.Spawn`
    without `startAsAdult: true`, so a freshly-promoted Tier1 started as an infant — which broke
    the very next line of `ResolveMarriage` (the `MarriageMinAgeSeasons` check) every time. Fixed by
    passing `startAsAdult: true` (this also improves the pre-existing organic crystallization path,
    which had the same latent bug, just never exercised by an immediately-following age check).
    Added `PromoteForMarriage` to additionally floor the rolled age at `MarriageMinAgeSeasons`
    specifically, since the adult-fraction roll is only clamped to the lower `MinRulerAgeSeasons`
    bar and could still land below the marriage threshold.
  - **Relationship history was getting discarded on promotion** — promotion assigns the new Tier1 a
    new `EntityId` (well, usually the same *numeric* value as the dying Tier2's, since
    `CharacterFactory.Spawn`'s `entitySeq`-derived id reuses it — but a distinct `EntityId` value in
    the type system either way), so `RelationshipEdge`s keyed to the old Tier2 id — including the
    very Bond-Trust edge a Tier2-marriage depends on — would otherwise silently reset to a blank
    edge. Added `RelationshipGraph.RekeyEntity(oldId, newId)`, called from `PromoteToTier1` for
    every promotion (not just marriage-triggered ones), so accumulated Trust/Fear/rivalry carries
    over regardless of which path promoted them.
  - **Tier2 death left dangling rival edges** — a Tier2 rival that dies of old age (Tier2's own
    lifecycle, unrelated to promotion) never had its `IsRival` edges cleared, unlike
    `CharacterBehaviorPhase.KillCharacter`'s existing Tier1 cleanup — added the same cleanup to
    `Tier2BehaviorPhase.UpdateLifecycle`'s death branch so a dead Tier2 doesn't permanently inflate
    the surviving Tier1's `CountRivals` cap.
  - **Balance fallout:** `CharacterMarried`'s Tier1-population bottleneck lifted substantially now
    that Tier2 (the bulk population) is eligible — re-observed 32-67 over 300 years (was 0-10).
    `CharacterGrieved` rose too (114 in one seed, was 23-50) from the resulting population growth.
    `M13RelationshipEventBalanceTests.cs` bands widened accordingly; whether this rate itself needs
    a brake is left to 13.8.2/13.8.3 rather than decided here.
  - New tests: `Tier2RivalryAndMarriageTests.cs` (5 tests) — Rivalry/Feud/Placate against a Tier2,
    and the full promote-then-marry path including the age-floor and relationship-rekey fixes.

- **13.8.2 — SHIPPED 2026-08-03 — Notability: relationship-exposure tracking that feeds
  crystallization.**
  Added `Tier2Character.Notability` (float, mirrors the existing `LastCreateCompletedTick`/
  `LastNotableWorkTick` per-character-field pattern — touched only when relevant, never scanned
  population-wide), bumped by `Tier2NotabilityGainPerEvent` (0.15, config-driven) whenever the
  character is the target of a Bond-goal formation (`GoalManager.FindHighTrustCompanion`'s Tier2
  shortcut), `DeclareRivalry`/Feud-escalation, `Placate`, `GrantAid`, or `ForgiveDebt`
  (`CivTracker.cs`) — added via a new `Tier2Character.GainNotability(amount)` helper (clamps to
  [0,1]). Decays by `Tier2NotabilityDecayRate` (0.01/tick) in `Tier2BehaviorPhase.UpdateNeeds`,
  alongside the existing Needs decay.
  Resolved the open implementation question (additive roll bonus vs. OR'd threshold) as **both**:
  `TryCrystallize`'s Status gate becomes `Needs.Status >= threshold || Notability >=
  Tier2CrystalNotabilityThreshold` (0.6) — a drama-touched Tier2 doesn't need high settlement
  Status too — and the roll chance itself becomes `Tier2CrystalChance + Notability ×
  Tier2CrystalNotabilityChanceBonus` (0.01), a modest boost once a Tier2 clears whichever gate.
  Ambition remains a hard gate either way — Notability is an opportunity, not a replacement for
  personal drive.
  New tests: `Tier2NotabilityTests.cs` (7 tests) — a bump test per action type (Rivalry, Placate,
  GrantAid, ForgiveDebt, Bond-formation; Feud-escalation shares `ResolveRivalry`'s bump code path),
  decay-per-tick, and a dedicated test confirming high-Notability-low-Status alone can still
  crystallize (the OR-gate's whole point).

- **13.8.3 — SHIPPED 2026-08-03 — Balance & performance validation.**
  Added `Tier2CrystallizationBalanceTests.cs` (same 42/777/9999 × 300-year convention as
  `M13RelationshipEventBalanceTests`), asserting all three items scoped above in one pass:
  (a) `CharacterCrystallized` band [10, 100] — calibration run observed 31-59 (Tier1 population
  9-15, Tier2 population 50-85 by year 300, growing from 0 since Tier2 only spawns once
  settlements exist via `PopulationDynamicsPhase`); (b) a per-seed wall-clock ceiling of 180s via
  `Stopwatch` — calibration observed ~50-60s/seed; (c) `RelationshipEdge.EdgeCount` bounded to
  `tier1Count × 20` rather than an absolute number (Tier1 population varies by seed) — calibration
  observed a ratio of 2.9-4.8×, comfortably below a naive O(Tier1×Tier2) scan (which would put
  edge count in the 500-1000+ range for these populations). All three would catch a structural
  regression (Tier2 gaining its own scorer, or a new mechanic iterating full Tier2 population)
  without being a tight/flaky perf assertion. Full suite green after: 750/750 fast tests
  (`Category!=Balance`), 4/4 balance tests (`Category=Balance`).

## Open design notes carried into implementation

- **Resolved 2026-08-03 by 13.8.0 above:** whether Tier2 rivalry needs special-casing to stay out
  of civ-level war/alliance/territory effects. It doesn't need new isolation work — every consumer
  already filters to `Tier1Character` incidentally — but that incidental protection needs to become
  an explicit, tested invariant before 13.8.1 makes Tier2 a valid rivalry target, so a future
  refactor can't quietly remove a type filter that's secretly load-bearing.
- **Resolved 2026-08-03 by 13.8.1:** whether a Tier2-targeted Rivalry needs to resolve more
  passively than Tier1-Tier1. It does, and no special-casing was needed to make it so — Feud
  escalation and Reconciliation both require the *acting* character (whichever side calls
  `DeclareRivalry`/`Placate`) to have a scorer, and only Tier1 does. So a Tier2 rival can be
  escalated to Feud or Placated toward Reconciliation, but only ever by the Tier1 side choosing to;
  the Tier2 itself can never initiate either transition, which is exactly the intended asymmetry
  ("Tier1-initiated only" throughout this doc) and falls out of the existing scorer/no-scorer split
  for free.
- 13.8.2's Notability field is intentionally NOT the same as `Needs.Status` (already an existing,
  decaying, settlement/trade-driven need feeding `TryCrystallize` today) — kept separate so
  "currently prominent in the community" and "was recently touched by Tier1 drama" stay legible as
  distinct signals, and implemented as an OR'd gate (either satisfies the Status check) plus a
  small additive roll bonus, rather than folding Notability into Status directly.
