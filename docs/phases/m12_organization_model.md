# M12 — Organization Model

**Status:** IN PROGRESS — started 2026-07-31.

See `docs/roadmap.md` § "M12–M18 — Narrative Depth Expansion" for the full design rationale,
the 2026-07-30 user-confirmed decisions, and the code audit this milestone is built on
(`Civilization`/`CivTracker*.cs`/`RelationshipEdge` findings). This doc only tracks phase
sequencing and story-level notes; do not duplicate the rationale here.

Hard prerequisite for M13 (family), M14 (guilds), M15 (religion).

## Phase sequence

- **12.0 — Organization core entity + registry.** `OrganizationId`, `OrganizationKind`
  (Civilization/Guild/Religion/Family), `Organization` class (`LeaderId`, `Members` as
  `Dictionary<EntityId, Membership>` with `Role`/`Loyalty`, generalized org-relationship state:
  `WarsAgainst`/`BorderTension`/`PeaceTreaties`/`Allies`). `Organizations` dictionary on
  `WorldState`. Additive only — no existing `Civilization` behavior migrated yet. One
  `Organization` (Kind: Civilization) auto-created per `Civilization` at founding, kept in sync.
- **12.1 — Migrate civ diplomacy onto Organization relationship state. DONE (2026-07-31).**
  Scope note: on inspection, `Civilization.WarsAgainst`/`BorderTension`/`PeaceTreaties` were
  already civ-level facts (never derived from the ruler pair), and are deeply entangled with
  territory transfer/conquest/population — squarely "war mechanics themselves" per the roadmap's
  scope boundary, so they stay on `Civilization`. The actual fragility design decision 1 names
  ("assassinate the ruler, alliance evaporates") lives entirely in the *alliance* fact: it was
  read/written as the current ruler pair's `RelationshipEdge.IsAlly`
  (`CivTracker.Diplomacy.cs` `RunBorderTension`'s peace-check, the annual dissolution loop) —
  so a new ruler with no relationship history to the other side looked unallied even though the
  alliance was never broken. Fixed by adding `Organization.Allies` (a `HashSet<OrganizationId>`,
  landed in 12.0) as the independent org-to-org fact: `ResolveDiplomacy` (emissary path) and
  `ResolveAlly` (ruler-pair `AllyWith` case) now call `FormOrgAlliance` alongside the existing
  ruler-edge `IsAlly` flag; `RunBorderTension`'s peace-check reads `Organization.IsAllyOf`
  instead of the ruler edge; a new `RunOrgAllianceDissolution` (called from
  `RunAnnualDiplomacy`) breaks the org-level alliance only when the *current* rulers on both
  sides exist and their trust has decayed below the floor — a vacant/succeeding seat simply
  skips that year instead of the alliance silently lapsing; `StartWarBetween` also breaks the
  org alliance on war declaration. `Organization.LeaderId` is kept mirrored to
  `Civilization.RulerId` at both succession call sites in `CharacterBehaviorPhase.cs` (a
  narrow sync, not the full vacant-seat/heir-pool machinery — that generalization is 12.3).
  See `WorldEngine.Tests/Unit/OrganizationDiplomacyTests.cs` for the succession-survives-alliance
  regression test. `Organization` (including `Allies`) and `Civilization.OrgId` are now
  persisted via `OrganizationDto`/`WorldStateMapper` — see
  `WorldEngine.Tests/Integration/SaveLoadTests.cs`
  `WorldStateSaver_RoundTrip_Organizations` — since the alliance fact is now load-bearing
  behavior, not just additive state, per CLAUDE.md's "disk as system of record" rule.
- **12.2 — Multi-membership schema. DONE (2026-07-31).** `IdentityData.CivId` is removed;
  `Tier1Character.Memberships: List<Membership>` (mutable, parallel to `Goals`) is now the
  authoritative per-character Organization affiliation set, with `CivTracker.SetCharacterCiv` as
  the sole write path (replaces every `Identity = Identity with { CivId = x }` call site).
  Scoped as the full mechanical replacement per user direction (two rounds of scope-check —
  the true size was ~70 read sites across 10 Sim files, not the ~20 originally estimated), not
  the smaller additive-forward-index alternative.
  Real wrinkle found along the way: `CivId` and `OrganizationId` are independently-counted ID
  spaces (`WorldState.NextCivId` vs `NextOrganizationId`), and several of the ~70 reads have no
  `WorldState` available to do a reverse lookup with (`Tier1Character.ToCharacterSnapshot()` takes
  none at all) — so `Membership` carries a denormalized `CivId` field alongside `OrganizationId`,
  set once at the same call that creates the membership, not independently maintained by a second
  subsystem (the actual "dual source of truth" failure mode the roadmap's audit flagged). This
  keeps the ~70 hot-path reads (`UtilityScorer`, `GoalManager`, `CharacterBehaviorPhase`, etc.,
  now all `c.CivId` — an O(1) property on `Tier1Character` — instead of `c.Identity.CivId`) at
  their original cost.
  `SetCharacterCiv` self-heals a missing `Civilization.OrgId` (creates the backing Organization
  on demand) rather than silently dropping the membership — needed because ~20 test fixtures
  construct `Civilization` directly, bypassing the two production call sites
  (`CivTracker.cs`/`CivTracker.Unrest.cs`) that already create it. `Tier1Character.Memberships`
  is now persisted (`Tier1EntityDto.Memberships`/`CharacterMembershipDto`), alongside
  `Organization.Members`, so both round-trip in sync (`WorldStateSaver_RoundTrip_Organizations`
  asserts the loaded ruler's `Memberships` too). See
  `WorldEngine.Tests/Unit/CharacterMembershipTests.cs` for `SetCharacterCiv`'s own contract
  (self-heal, civ-switching removes the old membership and `Organization.Members` entry, clearing
  via `CivId.None`).
  Per design decision 2, the *weighted-loyalty conflict-resolution scoring logic* in
  `UtilityScorer`/`GoalManager` itself is still out of scope here — genuinely new design work the
  roadmap explicitly defers to land alongside M13 (family is the first real test case, and the
  first time a character can hold two simultaneous Organization memberships at all, since only
  Civilization creates Organizations before M13). 12.2 lands the schema and the single-membership
  read/write paths only.
- **12.3 — Generalize leadership succession.** Vacant-seat/heir-pool/crisis-window pattern
  (`SuccessionCrisisEndYear`, DB `SuccessionChain`/`Dynasties`) moves to hang off
  `Organization.LeaderId` generically, civ rulers migrated onto it as the existing instance. No
  new consumers this milestone (family/guild/religion heads land in M13–M15) — the point is the
  machinery is reusable when they do.

## Scope boundaries (per roadmap)

`Civilization`-specific mechanics that aren't about membership/leadership/relationships
(territory, `CulturalProfile`, war *mechanics* themselves — combat resolution, territory
transfer) stay on `Civilization`. Only membership, leader seat, and org-relationship *state*
move up into `Organization`. `WorldEngine.Sim`-only — no new UI surface required, though
existing panels reading `CivId` need updating to read the membership set (12.2).
