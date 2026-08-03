# M13.8 — Tier2 Relationship Exposure

**Status:** PLANNED — not started.

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

- **13.8.0 — Harden Tier1-only civ-level isolation (regression guard, built and merged before
  13.8.1 enables Tier2 targeting).**
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

- **13.8.1 — Tier2 as an eligible target for more Tier1 relationship actions.**
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

- **13.8.2 — Notability: relationship-exposure tracking that feeds crystallization.**
  Add a lightweight counter on `Tier2Character` (mirrors the existing `LastCreateCompletedTick`/
  `LastNotableWorkTick` per-character-field pattern — touched only when relevant, never scanned
  population-wide) bumped whenever the character is the target of a Bond/Rivalry/Placate/GrantAid/
  ForgiveDebt resolution. Feed it into `TryCrystallize`'s gate alongside the existing Ambition/
  Status check — a drama-touched Tier2 should crystallize meaningfully more readily than a random
  high-Ambition/high-Status roll alone. Open implementation question to resolve at build time:
  additive bonus to the crystallization roll vs. a separate OR'd threshold path — decide by
  looking at what keeps the *existing* Status-only crystallization rate (already balance-tested)
  from being swamped.

- **13.8.3 — Balance & performance validation.**
  Multi-seed sweep (same 42/777/9999 convention as the M13 lifespan-fix pass) confirming: (a)
  `CharacterCrystallized` rate increases moderately, not to a runaway "everyone gets promoted"
  regime that would balloon Tier1 population back toward Tier2 scale; (b) wall-clock time for a
  300-year run doesn't regress measurably versus pre-13.8 baseline (the actual test of the scale
  constraint above, not just an assertion); (c) `RelationshipEdge` table growth stays bounded
  (row count should scale with Tier1 count × contacts, not with Tier2 population). New/updated
  balance tests: extend `M13RelationshipEventBalanceTests.cs` (or a new
  `Tier2CrystallizationBalanceTests.cs`) with a `CharacterCrystallized` band and a wall-clock
  regression guard if one doesn't already exist.

## Open design notes carried into implementation

- **Resolved 2026-08-03 by 13.8.0 above:** whether Tier2 rivalry needs special-casing to stay out
  of civ-level war/alliance/territory effects. It doesn't need new isolation work — every consumer
  already filters to `Tier1Character` incidentally — but that incidental protection needs to become
  an explicit, tested invariant before 13.8.1 makes Tier2 a valid rivalry target, so a future
  refactor can't quietly remove a type filter that's secretly load-bearing.
- Which exact actions besides Bond/Rivalry/Placate are worth extending to Tier2 targets is a
  judgment call at 13.8.1 build time — `DeclareRivalry` and `Placate` in particular raise the
  question of whether a Tier2 "loses" a rivalry the way a Tier1 would (they have no goals/utility
  scoring to respond with a counter-action), so a Tier2-targeted Rivalry may need to resolve more
  passively than a Tier1-Tier1 one (e.g. it can be Placated by the Tier1 side but never
  Tier2-initiated-Feud-escalated, since escalation today is driven by the rival re-declaring, which
  requires a scorer Tier2 doesn't have).
- 13.8.2's Notability field is intentionally NOT the same as `Needs.Status` (already an existing,
  decaying, settlement/trade-driven need feeding `TryCrystallize` today) — keep them separate so
  "currently prominent in the community" and "was recently touched by Tier1 drama" stay legible as
  distinct signals in telemetry, even though both currently gate the same promotion roll.
