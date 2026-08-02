# M13 — Generational & Domestic Drama

**Status:** COMPLETE — 2026-08-02. All phases 13.0–13.5 shipped.

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
- **13.3 — Consequence-weight IsFamily/IsMarried. DONE.** `GoalManager.ApplyGriefToMourners` now
  multiplies Bond intensity by `GriefSpouseMultiplier`/`GriefFamilyMultiplier`/`GriefStrangerMultiplier`
  (1.6/1.3/1.0) based on the mourner-deceased `RelationshipEdge`'s `IsMarried`/`IsFamily` flags before
  it becomes Grieve goal Intensity/Priority, immediate Wellbeing shock, and the Avenge-goal gate —
  previously the Bond→Grieve pipeline was the *only* behavioral consequence of any bond and wasn't
  gated by relationship type at all (a spouse and a co-located trusted stranger grieved identically).
  `EmitGriefEvent` now reads the Grieve goal's (post-multiplier) Intensity instead of the stale Bond
  value. Ruler cross-civ marriage is now a real diplomatic lever: `ResolveMarriage` forms the same
  Organization-to-Organization alliance fact `ResolveAlly` does (`FormOrgAlliance`) when both spouses
  are their civ's current ruler and the civs aren't at war — reuses the M12 design decision 1
  alliance-survives-succession mechanism rather than a bespoke marriage-alliance path. Roadmap
  proposal #3.
- **13.4 — Non-ruler bonds reach the wider world. DONE.** Before this, only the *ruler's* personal
  RelationshipEdge ever escaped the character layer (reused verbatim as civ diplomacy). Three
  mechanisms, all reusing the existing emissary/tension systems per the roadmap:
  `CivTracker.ConfidantTrustCredit` scans a civ's non-ruler members for the strongest Trust edge to
  a living member of a target civ and credits a fraction of it (`EmissaryConfig.ConfidantTrustCredit`,
  0.7) toward the ruler-trust figure `SelectEmissaryPurpose` uses — a strong civilian back channel
  can open Diplomacy dispatch even when the rulers barely trust each other. `CivTracker.
  FriendshipDampening` — same scan shape, applied to the annual border-tension accrual instead —
  dampens tension buildup between two civs proportional to their strongest cross-civ friendship
  (`WarConfig.FriendshipTrustThreshold`/`FriendshipWarDampenMin`). New `Defect` command: a non-ruler
  character whose Wellbeing has spiraled (`DefectionConfig.WellbeingCrisisThreshold`) and who holds
  a co-located, sufficiently-trusted foreign confidant (`ConfidantTrustThreshold`) abandons their
  civ for the confidant's via the same `SetCharacterCiv` write path civ founding/childbirth/
  succession already use; rulers can't defect, and asylum is refused into a civ already at war with
  the defector's own. Roadmap proposal #4.
- **13.5 — New relationship-transition events. DONE.** Cheap, reusing existing substrates rather
  than new commands/systems (roadmap proposal #5): **Reconciliation** — `ResolvePlacate` now checks
  whether Fear has cooled to/below `FearConfig.ReconciliationFearThreshold` and Trust warmed to/above
  `ReconciliationTrustThreshold`; if so the rivalry (and any Feud) ends outright, firing
  `RivalsReconciled` — 13.1 deliberately left this to 13.5 rather than having Placate itself end the
  rivalry. **Feud** — `UtilityScorer`'s rivalry candidate gate now also fires against an existing
  (non-Feud) rival instead of excluding all rivals; `CivTracker.ResolveRivalry` treats a
  re-declaration against an already-active rival as escalation (`RelationshipFlags.IsFeud`, extra
  Trust/Fear penalty, `RivalryEscalatedToFeud`) rather than a no-op. **Estrangement** — new annual
  `CharacterBehaviorPhase.CheckMarriageEstrangement` (same discovery method as 13.0's
  `TrySpawnFamilyBirths`: scan `IsMarried` edges) clears `IsMarried|IsFamily` and fires
  `CharacterEstranged` once a married edge's Trust decays to/below
  `FamilyConfig.EstrangementTrustThreshold`; the household Family Organization/membership is left
  intact. **Oath-breaking** — new `CivTracker.CheckOathBreaking`, called from both `ResolveWar` and
  `ResolveRaid`, mirrors `UtilityScorer.DebtDampening`'s scan but as the consequence when a debtor
  wars/raids their own creditor's civ anyway despite the dampener: the debt is wiped to 0, the
  specific edge takes a `DebtConfig.OathBreakTrustPenalty` hit, and `OathBroken` fires.

Phases are additive; each should ship with its own tests and be committed independently.

## Post-completion balance pass (2026-08-02)

After 13.5 shipped, a multi-seed 300-year calibration run (`eventStore.CountEventsOfType` per M13
`EventType`) found most non-marriage M13 mechanics fired **zero times ever**, across all seeds.
Root causes found and fixed, in the order uncovered:

1. **`Tier2Character.Needs.Food`/`Safety` never actually decayed** — `Tier2AmbientFoodRecovery`
   (0.07) exceeded `Tier2NeedsDecayFood` (0.06), and `Tier2AmbientSafetyRecovery` (0.05) exceeded
   `Tier2NeedsDecaySafety` (0.04), so both needs monotonically rose and pinned at 1.0 forever.
   `GrantAid`'s "recipient in need" check could never trigger. Fixed by dropping both recovery
   rates below their decay rates (mirrors Tier1's own Food/Safety net-decay balance in
   `NeedsUpdater`) — see `CharacterSimConfig.Tier2AmbientFoodRecovery`/`Tier2AmbientSafetyRecovery`.
2. **Debt (`GrantAid`/`ForgiveDebt`) required a pre-existing Tier1-Tier1 `RelationshipEdge` Trust
   ≥ threshold** — but nothing in the sim ever raises ordinary same-civ Trust from its 0 default
   (the one exception, `AllyWith`, is explicitly cross-civ-only). Fixed by letting a co-located
   Tier2 townsperson of the granter's own settlement qualify via the same "shared homeland is
   enough" shortcut `GoalManager.FindHighTrustCompanion` already uses for Bond formation — no
   registry threshold needed. See `CivTracker.TryGetNeeds`/`RestoreNeeds`/`DisplayName` and the
   Tier2 branch in `UtilityScorer`'s GrantAid/ForgiveDebt candidate loop.
3. **Personal-interaction candidate loops (`GrantAid`/`ForgiveDebt`/`Placate`/`Defect`) required
   landing on the exact same tile** (`world.GetEntitiesAt`), unlike `DeclareRivalry` which already
   used a `PerceptionRadius` scan — widened to match, since Tier1 characters are rare and mostly
   stationary.
4. **The critical one: `GrantAid`/`ForgiveDebt`/`Placate`/`Defect` were never wired into
   `CharacterBehaviorPhase.ResolveCommand`'s dispatch switch.** `UtilityScorer.SelectAction` was
   selecting them constantly (confirmed via temporary instrumentation: `GrantAid` alone was chosen
   2113 times in one 300-year run) but `ResolveCommand`'s switch had no matching case, so they
   silently no-op'd on every single tick since 13.1/13.2/13.4 shipped. Only the CivTracker-level
   unit tests (which call `CivTracker.Resolve` directly, bypassing `ResolveCommand`) ever exercised
   these mechanics — the full-sim path was dead. Fixed by adding the four missing cases (mirrors
   the existing `AllyWith`/`DeclareRivalry`/etc. cases, all of which route to
   `CivTracker.Resolve`).
5. **Goal-stacking left little idle/opportunistic time.** Every discretionary goal type (Dominance,
   Alliance, Bond×N, Create, BuildImprovement, SlayBeast, CovetArtifact×N) formed independently
   with no shared ceiling — a character could hold ~6-10 simultaneously, so `GoalAdvancement`
   scoring was almost always dominated by *something*, crowding out Rest/GrantAid/Placate/Ally/
   Negotiate. Added `CharacterSimConfig.MaxConcurrentGoals` (2) as a shared ceiling on
   Dominance/Alliance/Bond/Create/BuildImprovement/SlayBeast/CovetArtifact formation — Survive/
   Grieve/Avenge/FoundCity/SeaVoyage are excluded (existential or externally imposed, not
   discretionary "wants").

**Result:** `DebtIncurred` went from 0 to 918-2113 per 300-year run (3 seeds); `DebtForgiven` 0 to
308-689; `CharacterDefected` 0 to 0-7. `RivalryFormed` and everything downstream of it
(`RivalryPlacated`, `RivalsReconciled`, `RivalryEscalatedToFeud`, `CharacterEstranged`,
`OathBroken`) remained at 0 across all 3 seeds — this is a *different*, still-open constraint:
these require two named Tier1 characters (rare, single digits to ~15 alive at once) to personally
interact cross-civ, which is much rarer than the same-civ community-aid path Debt now uses. Not
fixed this pass — flagged in `M13RelationshipEventBalanceTests` as a ceiling-only (no floor)
assertion pending a dedicated Tier1 cross-civ contact-frequency pass.

New balance test: `WorldEngine.Tests/Balance/M13RelationshipEventBalanceTests.cs` (3 seeds × 300
years, cumulative `EventType` counts per the project's observed-healthy-±-margin philosophy — see
`docs/balance_invariants.md`).

The `MaxConcurrentGoals` cap (fix 5 above, final value 2) legitimately shifted two existing pre-M13
invariants in `config/balance_invariants.toml`:
- `goals_formed_cumulative_min` was calibrated at 2241–3145 (pre-cap); most `GoalFormed` events
  come from exactly the discretionary goal types now capped, so cumulative goals-formed dropped
  ~5x to 402–657 post-cap. Re-calibrated the floor from 500 to 250 (~40% below new observed floor).
- `goals_formed_ytd` (a ONE-YEAR snapshot at year 300, min was 1) started failing for seed 42 —
  at the new lower/burstier formation cadence, a single specific year can legitimately land on 0
  new goals by chance, without indicating the "formation silently drops to 0" regression this
  guard exists to catch (a value of 3 for the cap didn't fix this either — it's inherent to
  per-year sampling at low volume, not a cap-value tuning problem). Lowered min to 0; the
  cumulative floor above is the robust version of the same regression guard now.

Both are legitimate re-calibrations per the project's own procedure (a mechanic change
deliberately shifting a metric → re-run the sweep, update the band, document why), not band
weakening to dodge a real regression — see each band's TOML rationale field.
