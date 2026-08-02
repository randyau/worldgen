# M13 — Generational & Domestic Drama

**Status:** IN PROGRESS — started 2026-08-01.

See `docs/roadmap.md` § "M12–M18 — Narrative Depth Expansion" for the full design rationale and
the 2026-07-30 relationship-system audit this milestone is built on. This doc tracks phase
sequencing and story-level implementation notes; do not duplicate the rationale here.

Prerequisite: M12 Organization Model (COMPLETE) — Family is an `OrganizationKind`, and the
generalized `SuccessionResolver` backs family-head succession.

**Scope note found at kickoff (2026-08-01):** audited actual reproduction — `IdentityData` has
had `MotherId`/`FatherId` fields since early milestones, but nothing ever wrote a non-null value.
`CharacterBehaviorPhase`'s only birth path is abstract settlement-level population growth
(`CharacterFactory.Spawn` with `MotherId`/`FatherId` both `null`) — there has never been a
mechanic linking two specific named characters as parents of a third. Likewise `IsMarried` is a
`RelationshipFlags` bit that nothing ever sets. "Generational drama" has no generations to work
with yet — phase 13.0 below builds the actual family-formation substrate (marriage, real
parent-child linkage, trait inheritance) before any grudge/mentorship/rivalry mechanic can have
real family data to act on.

## Phase sequence

- **13.0 — Marriage, Family Organization, and real childbirth with inheritance.** New `ProposeMarriage`
  command (mirrors `AllyWith`'s shape) upgrades a high-trust `Bond` goal into `RelationshipFlags.IsMarried
  | IsFamily` and creates a new `Organization` (Kind: Family) — a household — with both spouses as
  `Members`, `LeaderId` set by comparison (see implementation). Married couples periodically roll
  for childbirth; a child is `CharacterFactory`-spawned with real `MotherId`/`FatherId`, personality/
  aptitude traits blended from both parents (not just ancestry bias), and joins the household Family
  org plus (denormalized, mirroring `Membership.CivId`) whichever parent's civ membership exists.
  First real consumer of M12 design decision 2 (weighted-loyalty conflict scoring): `UtilityScorer`'s
  War/Raid candidate generation dampens score when the acting character has a Family-org relative
  living in the target civ, weighted by Family Loyalty vs. Civ Loyalty.
- **13.1 — Activate Fear as a submission/appeasement axis. DONE.** New `Placate` command (mirrors
  `AllyWith`'s shape): a low-Aggression character facing an existing, sufficiently feared rival
  (`RelationshipEdge.Fear`, previously written once as a flat +0.1 on rivalry formation and never
  read by anything) appeases them instead of escalating — reduces Fear, nudges Trust up, but does
  not itself end the rivalry (that's 13.5's Reconciliation). `ResolveRivalry` now scales the Fear
  increment by how much more formidable the target is than the declarer (Combat/Aggression), not a
  flat bump. `UtilityScorer.FearDampening` — mirrors 13.0's `KinDampening`/13.2's `DebtDampening` —
  is the passive "avoided" half: dampens War/Raid desirability when the acting character fears a
  rival resident in the target civ, scaled by Fear magnitude; stacks with the other two dampeners.
  Roadmap proposal #1.
- **13.2 — Activate Debt as an obligation mechanic. DONE.** New `GrantAid`/`ForgiveDebt` commands
  (mirror `AllyWith`'s shape): a trusted character materially aids a co-located companion whose
  Food or Safety need is critical, creating a signed `RelationshipEdge.Debt` obligation (`DebtorId`/
  `CreditorId` helpers added to interpret the sign against the canonical From/To pair); a creditor
  can later forgive it, boosting Trust. First behavioral consequence: `UtilityScorer.DebtDampening`
  — mirrors 13.0's `KinDampening` — dampens War/Raid desirability when the acting character owes a
  living creditor resident in the target civ, scaled by how much of the edge's Debt range is owed.
  Inheritable: `CharacterBehaviorPhase.TransferDebtOnDeath` re-points a dead character's Debt edges
  at their household Family-org heir (spouse preferred) rather than the obligation vanishing with
  them — ties into the Family Organization built in 13.0, not the civ ruler succession seat.
  Roadmap proposal #2.
- **13.3 — Consequence-weight IsFamily/IsMarried.** Grief severity/probability scaled by relationship
  type; ruler cross-civ marriage as a real diplomatic lever (arranged marriage / alliance-cement).
  Roadmap proposal #3 (13.0 lands the marriage mechanic itself; this phase lands the *consequences*
  being genuinely differentiated by relationship type rather than uniform).
- **13.4 — Non-ruler bonds reach the wider world.** Trusted confidant → emissary candidate; cross-civ
  friendship dampens war tension / triggers asylum-defection. Roadmap proposal #4.
- **13.5 — New relationship-transition events.** Reconciliation, Feud, Estrangement, Oath-breaking
  (violated Debt). Roadmap proposal #5.

Phases are additive; each should ship with its own tests and be committed independently.
