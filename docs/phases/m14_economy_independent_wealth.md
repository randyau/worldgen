# M14 — Economy & Independent Wealth

**Status:** IN PROGRESS. Kickoff design pass 2026-08-04. Phase 14.0 (wealth substrate) shipped
2026-08-05 — see the phase-sequence entry below for what landed. Phase 14.1 (wire Wealth into
existing trade) also shipped 2026-08-05 — `Tier2BehaviorPhase.RunMerchant` now prices and pays for
its existing physical-goods transfer via a new `CompleteMerchantTrade` command resolved in the same
phase, crediting the merchant's `Wealth` net of the new `EconomyConfig.MerchantHomeCutFraction`
(0.3) recirculation cut back to their home settlement. New `EventType.TradePaid = 3500` (first use
of the fresh 3500 range). Phase 14.2 (persistent trade routes / caravan transit — the largest phase
in the milestone) also shipped 2026-08-05 — see its phase-sequence entry below for the full
`TradeRoute`/`Caravan` data shape, formation/severance/reopening rules, and the interception/
disaster/piracy roll model. New `EventType`s `TradeRouteFormed = 3501`, `TradeRouteSevered = 3502`,
`CaravanRaided = 3503`. Phase 14.3 (goal fulfillment via trade) also shipped 2026-08-05 — see its
phase-sequence entry below (`PurchaseArtifact`/`ArtifactPurchaseResolver`, `EventType.ArtifactPurchased
= 3504`). Phase 14.4 (Guild organizations, real stored treasuries, civ-level economic ruin, war
reparations) shipped 2026-08-05 — `OrganizationKind.Guild` populated for the first time
(Wealth-threshold formation/join in `Tier2BehaviorPhase.FormOrJoinGuild`, reusing
`SuccessionResolver.SelectSuccessor` unmodified for leader succession), `ContributeToTreasury`/
`WithdrawFromTreasury` commands, Guild-member trade routing to `Organization.Treasury`, civ-level
`TreasuryInsolvent` (edge-triggered) folded into the existing `CivSplintered`/instability scoring,
and one-time war reparations on war resolution. New `EventType`s `GuildFormed = 3505`,
`TreasuryContribution = 3506`, `TreasuryWithdrawal = 3507`, `TreasuryInsolvent = 3508`,
`WarReparationsPaid = 3509`, `GuildLeadershipTransferred = 3510`. Also fixed a latent
`Tier2BehaviorPhase.PromoteToTier1` bug (shared by crystallization/marriage-promotion since M13.8):
the promoted Tier1 reused the dead Tier2's exact `EntityId`, so `EntityRegistry`'s shared
`Dictionary<EntityId, IEntity>` aliased the two and the same-or-next-tick dead-Tier2 sweep deleted
the *promoted Tier1* instead — see `SimEntity.Id`'s doc comment and
`M13RelationshipEventBalanceTests`' updated bands (fixing this let promoted characters actually
survive, correctly raising several M13-era event-count ceilings). 14.5 (balance pass + economic
ledger UI) not yet started.

See `docs/roadmap.md` § "M14" for the one-line scope statement and § the M12-era audit notes
(lines ~350-361) for why guild/merchant succession must reuse the M12 `SuccessionResolver`
kernel rather than hand-rolling a new one. This doc tracks phase sequencing and the concrete
design decisions made at kickoff; do not duplicate rationale that belongs in the roadmap.

Prerequisite: M12 Organization Model (COMPLETE) — `OrganizationKind.Guild` already exists as an
unpopulated enum case reserved for this milestone. Builds on M9's economic-depth foundation
(per-capita demand, settlement specialization) and explicitly supersedes M9's deliberately-scoped-out
trade-network topology.

## Kickoff design decisions (2026-08-04)

Four design forks were resolved with the user before phase planning, because each one changes the
size and shape of the milestone substantially:

1. **Wealth is a real fungible currency, not an abstract score — but it is *not* a separately
   minted resource.** Revised 2026-08-04 after a sources/sinks review: an explicit `"coin"`
   resource that settlements must actively mint would require a new character motivation
   (a `MintCoin`-style action competing in the utility scorer against everything else a
   character can do) that, per the M13.5-era OathBroken/Estranged lesson, may simply never win
   often enough to fire at current population scale — "unless characters are motivated to make
   coin, it won't exist." Instead, `Wealth` (new scalar field on `Tier1Character`/
   `Tier2Character`, structurally like `Tier2Character.Notability`) is a portable *value*
   denominated against a config-driven conversion table over the precious commodities the economy
   **already produces via existing, already-motivated mining/production** — gold, silver, gems.
   No new production chain, no new character behavior required. See decision 4 for exactly how
   this stays physically conserved rather than becoming free money.
2. **Trade routes get the full build: travel-time/caravan simulation**, not a lightweight
   persistent-link shortcut. Goods spend real transit ticks between settlements and are exposed to
   interception/raid/disaster while in transit. This is explicitly the largest phase in the
   milestone (14.2) and the direct realization of the `TradeCaravan` concept that appeared in
   `implementation_decisions_v0.3.md`'s aspirational architecture tree but was never built.
3. **Faction-funding ("wealth buys influence") is explicitly deferred**, not part of M14. Instead,
   Wealth's spend-side MVP is **goal fulfillment via trade** — a character with an unmet want
   (starting with `CovetArtifact`, M14's only integration point into the existing goal system)
   can attempt to buy what they want instead of the goal's only current resolution paths
   (claim-if-Lost, or the sketched-but-unbuilt conflict/raid escalation). This is scoped in 14.3.
4. **Wealth is physically conserved, not printed from nothing — the "gold/silver-as-money-
   equivalent" abstraction.** Revised 2026-08-04, replacing the original "mint a coin resource"
   idea (see decision 1); the conversion anchor itself was broadened further by decision 7 below
   into `EconomyConfig.BaseValuePerUnit` (covers every tradeable resource, not just the precious
   metals — see decision 7 for the full pricing mechanism). Every Wealth transfer is a two-sided
   physical exchange, never a spawn-from-nothing credit:
   - **Trade (14.1/14.2):** when a merchant sells to a destination settlement, the destination's
     own precious-commodity `ResourceStores` are debited by the equivalent value (at that
     settlement's current local price, decision 7) and the seller's personal `Wealth` is credited
     by the same amount — a real transfer, exactly like `GrantAid` moves `Debt` between two
     parties rather than creating it from nothing.
   - **Spend (14.3) / vault deposit (14.4):** reverses the same conversion — a buyer's `Wealth`
     decreases and the seller's/vault's destination settlement `ResourceStores` gain the
     equivalent precious-commodity value.
   - **REVISED 2026-08-05 (Opus review — both retracted):** the original text here claimed a
     guild/civ treasury is "not a separate data type … a designated claim on its home settlement's
     `ResourceStores`, not a stored ledger balance," and that this exposed treasuries to the
     existing raid/spoilage sinks "with zero new code." Both claims are wrong and are retracted —
     see decision 10 for why (`Organization` has no settlement-anchor field to define "home" on,
     `ResourceStores` is floored at zero everywhere it's written so a live-computed claim can
     never go negative, and an undifferentiated claim on shared settlement gold can't tell one
     org's money from another's or from the settlement's own general reserves). Treasuries get a
     real stored `Organization.Treasury` balance instead — decision 10.
   - **REVISED 2026-08-05 (Opus review — critical gap):** the claim that "M14 adds no new sink"
     is also wrong. Every sink identified above (spoilage, raid destruction) only ever touches
     settlement-held `ResourceStores` — **personal `Wealth` is explicitly excluded from
     `ResourceStores`** (see 14.0) and, with interpersonal theft out of scope (decision 6), has
     *no sink whatsoever*. Every trade (14.1/14.2) converts sink-exposed settlement gold into
     sink-free personal Wealth and never converts back (14.3 only moves Wealth between two
     characters, never destroys it) — a one-way ratchet that, over a 10,000-year run, drains
     settlement reserves and concentrates the entire money supply into an un-shrinkable personal
     pool, defeating the very inflation `GlobalPriceIndex` (decision 8) exists to track. Fixed by
     a genuine new sink — see decision 10.
   - Total money supply grows when the *existing* mining/resource-pressure system (population-
     driven, already balanced) adds new gold/silver/gems into the world, and — once decision 10's
     fixes land — shrinks via the existing settlement-side spoilage/raid sinks *and* a new
     personal-Wealth sink. M14 does still add no new *source* of physical value, only a portable
     accounting layer (`Wealth`) over value that already exists — the "no new source" half of the
     original claim holds; the "sinks are sufficient as-is" half did not.
5. **Death disposition splits inheritance and looting.** A dying character's Wealth partially
   transfers to their heir (mirrors `TransferDebtOnDeath`, M13.2) and partially becomes an
   unclaimed pool any co-located character can claim (mirrors the `Artifact.Owner.Lost` +
   `GoalManager` claim-on-co-location pattern from M5). See 14.0.
   **REVISED 2026-08-05 (Opus review — measurement leak):** the unclaimed `WealthDrop` pool sits
   outside both `Tier1Character.Wealth`/`Tier2Character.Wealth` and `ResourceStores`, but decision
   8's `TotalMoneySupply` only sums those two things — so every death silently removes the dropped
   fraction from the money-supply measurement until (if ever) a co-located character claims it.
   Claims require co-location and may never happen, so a standing reservoir of un-measured,
   un-spoiling orphan Wealth can accumulate on tiles indefinitely, causing `GlobalPriceIndex` to
   systematically under-read the true supply. Fixed by: (a) including the live `WealthDrop` pool
   total in `TotalMoneySupply`'s computation, and (b) giving drop pools the same new
   `PersonalWealthSpoilageRate` sink from decision 10 (or an explicit claim-deadline expiry) so
   they can't stand forever unclaimed. Also define the no-eligible-heir edge case explicitly: it
   drops in full (100% to the `WealthDrop` pool, 0% inherited), not left undefined.
6. **Interpersonal theft is explicitly out of scope for M14.** No `Steal`/`Rob`/crime command
   exists anywhere in the codebase today, and building one implies an adjacent crime/law/consequence
   system (detection, Trust penalties, guard response) that isn't scoped anywhere in the roadmap.
   Flagged as a plausible M14.x/M16+ follow-up, not built speculatively here.
7. **Pricing is seeded and formulaic, not discovered.** Added 2026-08-04 after flagging that a
   real market (order books, bid/ask, price convergence from transaction volume) is both hard to
   bootstrap with authored parameters and almost certainly unsupportable at this population's
   transaction volume — there aren't enough trades per settlement per year for prices to converge
   on anything meaningful. **No price-discovery/market-clearing system is built in M14.** Instead:
   - `EconomyConfig.BaseValuePerUnit` (renames/broadens decision 4's `CommodityValuePerUnit`):
     one designer-authored value-per-unit for *every* tradeable resource key (not just
     gold/silver/gems — also food, timber, minerals, tools, etc.), same pattern as any other
     `SimConfig` constant table. This is the "seed base values" the user asked for — a static
     relative-scarcity ranking (gems > gold > silver > tools > minerals > timber > food), not
     something the sim ever updates.
   - A trade's *actual* price is `BaseValuePerUnit[resource] × clamp(LocalScarcityMultiplier, min,
     max)`, where `LocalScarcityMultiplier` is derived directly from the **existing** M9
     `SettlementStub.ResourceLedger` per-capita supply/demand ratio (a settlement already-deficient
     in a resource has a ledger ratio that pushes its local price up; a settlement in surplus pushes
     it down) — reusing infrastructure that already exists and is already balanced, rather than
     building a new demand signal. The multiplier is clamped (config band, e.g. 0.5x-2x) so no
     single settlement's price can run away.
   - Critically, **price is computed fresh at the moment of each trade, not accumulated or
     learned** — there is no persistent "current market price" state, no history, no
     transaction-volume dependency. This is what sidesteps the "not enough transactions for real
     price-seeking" problem: nothing needs to converge because nothing is being discovered, only
     evaluated from a formula. A settlement's price can still meaningfully vary tick-to-tick as its
     ledger ratio shifts, giving merchants a genuine (if simplified) reason to route goods toward
     deficient markets — see 14.2.
   - Artifacts (14.3) aren't resource-keyed, so they get a parallel formula rather than reuse
     `BaseValuePerUnit` directly: `ArtifactBaseValue` derived from the artifact's existing quality/
     `CreatedGoodType` category (the G-1 taxonomy already weights these for persistence/rarity —
     reuse that weighting, don't invent a second one), times a single `ArtifactValueMultiplier`
     config scalar reflecting that an exceptional creation is worth more than raw commodity by
     narrative design. No scarcity multiplier for artifacts — they're already unique by
     construction, so "scarcity" is trivially 1 of 1 and doesn't need a market signal.
   - Explicitly flagged as a simplification, not a placeholder for later real market work — if
     transaction volume ever grows enough (e.g. after several more milestones of population/
     scale growth) to justify true price discovery, that would be new, separate scope, not
     something M14 needs to leave hooks for.
8. **A global, per-capita price index keeps fixed base prices from drifting away from the money
   supply over a 10,000-year run.** Added 2026-08-04 after a sources/sinks review flagged the real
   failure mode: `WealthSpoilageRate` is `0.0001`/tick ("gold/gems essentially permanent",
   `ResourcePressurePhase.cs:110`) against continuous per-capita accretion
   (`ResourcePressurePhase.cs:116-119`) — the steady-state stock for any spoiling quantity is
   `supply ÷ spoilage`, and at a spoilage rate this close to zero, no realistic run length lets
   per-settlement precious-metal stores approach equilibrium; they climb close to monotonically for
   the life of the world. Settlement count itself also grows over 10k years (colonization). Both
   effects mean total money supply (decision 4) inflates steadily while decision 7's
   `BaseValuePerUnit` never moves — the "real" cost of everything falls monotonically, and
   eventually every character is rich and every resource is trivial to acquire, exactly the drift
   flagged. Not treated as a source/sink problem to fix by throttling production or increasing
   spoilage (the user explicitly does not consider raw money-supply growth itself a problem, and
   `WealthSpoilageRate`/mining output are already-calibrated M9 knobs this milestone shouldn't
   re-tune for an unrelated reason) — treated as a **pricing** problem instead, per the direct
   instruction that "prices will need to follow the overall money supply at least loosely":
   - New `WorldState.GlobalPriceIndex` (float, starts at `1.0`) — a single **world-wide** scalar,
     deliberately not per-civ/per-settlement (matches "the *overall* money supply"; per-civ tracking
     would be real complexity for a correction that only needs to be loose).
   - On the existing annual-tick cadence (`isAnnualTick`, the same gate `CheckMarriageEstrangement`
     already uses — no new tick-cadence concept), a new step computes:
     `TotalMoneySupply` = sum of every living `Tier1Character`/`Tier2Character`'s `Wealth` +
     sum over all `Organization.Treasury` balances (decision 10) +
     sum over all standing `WealthDrop` pools (decision 5's revision) +
     sum over all settlements of `(gold + silver + gems) × BaseValuePerUnit` in `ResourceStores`.
     **REVISED 2026-08-05 (Opus review):** the original formula here omitted treasuries (which
     didn't exist as a separate balance yet) and unclaimed `WealthDrop` pools — both are real
     money and their omission would make the index systematically under-read the true supply.
     This is exactly decision 4's conserved quantity (every term is the same money, just currently
     held in a different form), so the *only* things that move it are the existing mining
     production (source) and the existing + new spoilage/raid/consumption sinks from decision 10
     — no new source is introduced by measuring it.
     `TotalPopulation` = living `Tier1Character` count + Σ `SettlementStub.Population`.
     `MoneySupplyPerCapita = TotalMoneySupply / max(1, TotalPopulation)`.
   - `GlobalPriceIndex` EMA-tracks `clamp(MoneySupplyPerCapita / EconomyConfig.
     ReferenceMoneySupplyPerCapita, PriceIndexMin, PriceIndexMax)` — same EMA-toward-a-clamped-
     target shape as `SettlementStub.SpecializationStrength` (M9 9.2), reused for consistency
     rather than inventing a new smoothing pattern. `ReferenceMoneySupplyPerCapita` is an authored
     `EconomyConfig` constant (the "this is what a fair per-capita money supply looks like"
     anchor), tuned during 14.5's balance pass like every other calibration constant in this
     codebase — not something the sim derives on its own, since there's no principled zero point to
     derive it from. **REVISED 2026-08-05 (Opus review — the clamp only means something once
     decision 10's personal-Wealth sink exists):** without a sink on personal `Wealth`,
     per-capita money supply is unbounded, so *any* finite `PriceIndexMax` eventually saturates —
     the index pins at its ceiling and the exact price-vs-supply drift this decision exists to
     prevent silently resumes above that point. With decision 10's `PersonalWealthSpoilageRate` in
     place, per-capita Wealth has a finite equilibrium ceiling and a bounded clamp is legitimate.
     Also watch the warm-up transient: the index starts at `1.0` but year-0 money supply is near
     zero, so the EMA drags it toward `PriceIndexMin` for the first few centuries — 14.5 should
     calibrate `ReferenceMoneySupplyPerCapita` from long-run equilibrium data (not year-300 data)
     and consider seeding the index below `1.0` to avoid a floor-pinned early game.
   - **Every price everywhere in this milestone gets one more multiplicative term:**
     `EffectivePrice = BaseValuePerUnit × LocalScarcityMultiplier × GlobalPriceIndex` for
     commodities (14.1/14.2), and `EffectivePrice = ArtifactBaseValue × ArtifactValueMultiplier ×
     GlobalPriceIndex` for artifacts (14.3). As per-capita money supply climbs over the run,
     `GlobalPriceIndex` climbs with it and nominal prices float upward — keeping the *real*
     (money-supply-relative) cost of goods roughly stable across the full 10,000-year run instead
     of trending toward zero. Local scarcity (decision 7) still drives spatial price variation
     between settlements at any given moment; the global index only corrects the drift *over time*.
9. **Personal vs. organizational Wealth is distinguished by transaction *type*, not by a
   permission/authorization system.** Added 2026-08-05, prompted by the observation that
   "who spent whose money" is a genuinely thorny bookkeeping question even in real organizations,
   and this milestone should not attempt a real one. Reframed from an authority question ("is this
   member allowed to spend the treasury") to an attribution question ("which pool does this kind
   of transaction hit"), which turns out to have a clean answer given what's already built:
   - **Trade income/expense (14.1/14.2) is organizational by default once the acting Merchant has
     joined a Guild** — trading *is* the guild's business, so a guild-member merchant's trade runs
     debit/credit their Guild's treasury automatically, not their personal `Wealth`. A merchant who
     hasn't joined a Guild keeps 100% of their trade earnings personally, exactly as 14.1 already
     specifies. No new command or check is needed to decide this — it falls directly out of
     whether `Membership.OrganizationId` resolves to a Guild for that character at the moment
     `RunMerchant` executes.
   - **Every other Wealth-touching action stays unconditionally personal** — 14.3's
     `PurchaseArtifact`, and any future gift/aid-style transfer, always draws the acting
     character's own `Wealth`. These are inherently transactions between individuals; there is no
     ambiguity to resolve because they never touch an org treasury at all.
   - **Deposits (personal → org treasury) are open to any member, no authority check** — a
     `ContributeToTreasury` command mirroring `GrantAid`'s shape, available to anyone with a
     `Membership` in the target `Organization`. Voluntary dues/tithing, not gated.
   - **Withdrawals (org treasury → a specific member's personal `Wealth`) are Leader-only** — a
     `WithdrawFromTreasury` command gated on `c.Id == org.LeaderId`, reusing `OrganizationRole`'s
     existing binary `Member`/`Leader` split (confirmed: no richer role — no `Treasurer` — exists
     to design around, so this is the only authority distinction available and it's already
     sufficient). This is the *only* place an actual authority check exists in the whole model,
     and it applies uniformly to every `OrganizationKind` for free, since `Civilization` already
     mirrors `civ.RulerId` onto `Organization.LeaderId` (`CivTracker.cs:54`) — one rule covers
     guild heads and civ rulers identically, no special-casing per kind.
   - **A member who wants to spend organizational money on something personal (e.g. their own
     `CovetArtifact` goal) does it in two ordinary steps, not a new dual-sourced command**: the
     Leader authorizes a `WithdrawFromTreasury` payout to that member first, and the member then
     spends normally out of their now-larger personal `Wealth` via the existing 14.3 path. Keeps
     every command single-pool, which is simpler to implement, persist, and reason about than a
     command that could draw from either pool depending on context.
   - **Deliberate consequence, flagged as a hook rather than solved here**: because only the Leader
     can ever convert treasury funds into someone's personal spending money, a corrupt leader
     siphoning the shared treasury into their own `Wealth` via repeated self-directed
     `WithdrawFromTreasury` calls is *already* representable with zero new mechanism — it's exactly
     the shape of story M18 ("corrupt role-holders") wants. Do not build detection/consequences for
     this in M14; just note that the authority model above hands M18 a ready-made lever.
     **REVISED 2026-08-05 (Opus review):** this hook doesn't actually work under the original
     decision 4 treasury design — see decision 10. It's restored once treasuries get a real
     stored balance.
10. **Two structural fixes from an independent Opus review pass (2026-08-05), both required before
    implementation — the plan's conservation and economic-ruin claims don't hold without them.**
    - **A real, non-teleporting treasury.** `Organization` (checked directly:
      `WorldEngine.Sim/Organizations/Organization.cs`) has `Id, Kind, Name, LeaderId, FoundedYear,
      Members` plus war/tension/alliance dictionaries — **no settlement reference at all**, so
      decision 4's "home settlement" was undefined in the actual model, and "a claim on that
      settlement's `ResourceStores`" can't be implemented as originally written. Worse,
      `ResourceStores` values are floored at zero everywhere they're written
      (`Math.Max(0f, ...)` in `ResourcePressurePhase.cs` and the raid-damage path in
      `CivTracker.War.cs`), so a live-computed, floor-clamped claim can *never* go negative — 14.4's
      entire economic-ruin trigger ("a civ whose aggregate treasury runs persistently negative")
      and the `TreasuryInsolvent` event are unreachable by construction, not merely at risk of
      never firing. And an undifferentiated claim on a shared settlement's gold can't distinguish
      one org's money from another org's sharing the same settlement, or from the settlement's own
      unrelated reserves — which also breaks decision 9's M18 corruption hook (nothing to visibly
      "siphon" if there's no separate balance). **Fix:** give `Organization` two new fields — a
      `HomeSettlementCoord` (or equivalent settlement reference, resolved once at founding/HQ
      designation, not re-derived from the current leader's location every tick, which would make
      the treasury teleport on every succession) and a real stored `float Treasury` balance (with
      the same `WorldStateDto`/`WorldStateMapper` coverage as `Wealth`, per the persistence
      checklist already noted in 14.0). Deposits/withdrawals move between personal `Wealth` and
      `Organization.Treasury` directly; the treasury still traces back to physically-produced
      value (nothing credits it except a real Wealth transfer in), it just now has a number that
      can be checked, drained, and — if reparations exceed it — driven negative, making
      `TreasuryInsolvent` an actual reachable event instead of a structurally-impossible one.
    - **A real sink on personal `Wealth`.** Every existing sink (`WealthSpoilageRate`, raid
      destruction) only touches settlement-held `ResourceStores`; personal `Wealth` is deliberately
      excluded from `ResourceStores` (14.0) and, with theft deferred (decision 6), has no sink at
      all. Every trade (14.1/14.2) is therefore a one-way conversion from sink-exposed settlement
      gold into a sink-free personal pool, and nothing ever converts back (14.3 only moves Wealth
      between two characters). Over a 10,000-year run this is a ratchet: money migrates
      irreversibly out of the only pool that can shrink, settlement reserves bleed out, and
      `MoneySupplyPerCapita` (decision 8) climbs in a way the price index was never modeled to
      expect — the exact drift decision 8 exists to prevent, now caused by a mechanism decision 8
      doesn't account for. **Fix:** add `EconomyConfig.PersonalWealthSpoilageRate` (a "cost of
      living" bleed) applied to every living `Tier1Character`/`Tier2Character`'s `Wealth` on the
      same annual-tick sweep decision 8's `GlobalPriceIndex` update already needs (no new phase or
      tick-cadence concept — piggyback on the existing iteration). This gives per-capita personal
      Wealth a finite equilibrium ceiling (`income ÷ rate`) instead of unbounded growth, which is
      also a precondition for decision 8's `PriceIndexMax` clamp to mean anything: with unbounded
      per-capita money supply, *any* finite clamp eventually saturates and the index pins at its
      ceiling, silently resuming the exact drift it exists to prevent. Tune
      `PersonalWealthSpoilageRate` during 14.5 alongside `WealthInheritanceShare` so a lifetime of
      earnings isn't erased by the sink faster than a character can spend or bequeath it.
    - Both fixes touch 14.0 (add the two `Organization` fields and the spoilage config; the
      `Wealth` DTO/mapper checklist already planned there now also covers `Organization.Treasury`)
      and 14.4 (treasury commands now move a real balance, not a computed claim). See the
      phase-sequence edits below.

## Deep design review, 2026-08-04

Money is the largest single abstraction this codebase has introduced since Organizations (M12) —
it touches persistence, the UI boundary, diplomacy, artifacts, and every future power-track
milestone (M15 religion, M18 corrupt role-holders). A dedicated review pass against the actual
codebase (not just the roadmap's one-line scope) surfaced the following. Items are grouped by how
load-bearing they are, not by phase.

**Must be planned for now, not discovered mid-implementation:**

- **Persistence is two separate layers, and only one is "free."** `world.db` (SQLite) is an
  append-only history/event log (`DatabaseSchema.cs`); the actual save/load path is `state.bin`,
  a `System.Text.Json` serialization of `WorldStateDto` (`WorldStateDto.cs` +
  `WorldStateMapper.cs`). Adding `Wealth` to `Tier1Character`/`Tier2Character` requires a matching
  DTO field *and* explicit map/restore lines — it does not happen automatically from adding the
  property to the live class. **Concretely verified gap to avoid repeating:**
  `Tier2Character.Notability` (M13.8) was never added to `Tier2EntityDto`/`WorldStateMapper` and
  today silently resets to 0 on every load. `Wealth` must not ship with the same hole — 14.0
  should include the DTO/mapper work as an explicit checklist item, and it's worth a one-line fix
  to `Notability`'s persistence while touching that code, since it's the same bug in the same
  file. A new `TradeRoute` entity (14.2) is a bigger lift on this axis: a new `WorldState`
  collection, a new DTO record, and new mapper methods — budget real time for it, it is not a
  one-line addition like a scalar field.
- **`WorldSnapshot`/`StateCache` plumbing for economic data does not exist anywhere yet — there
  is no proven example to copy.** Checked both of the fields that looked like precedents
  (`SettlementStub.Specialization`, `Tier2Character.Notability`) — neither is projected into
  `SettlementSnapshot`/`EntitySnapshot`. 14.5's "economic ledger UI panel" is therefore not a
  matter of wiring up already-projected data; it needs new fields added to `WorldSnapshot.cs`/
  `EntitySnapshot.cs` and populated in `SnapshotBuilder.cs` from scratch. Size 14.5 accordingly —
  it's UI-boundary plumbing work, not just a balance pass.
- **New M14 `EventType`s need their own numeric range.** The existing scheme
  (`Core/Enumerations.cs`) uses 3300s for Tier2-actor events (`MerchantTradeCompleted = 3303`,
  highest currently used `3307`) and reserves 3400 for population — there's no clean room left
  adjacent to 3300 for a growing economy block. Allocate a fresh **3500 range** for M14
  (`ArtifactPurchased`, `TradeRouteFormed`, `TradeRouteSevered`, `CaravanRaided`,
  `TreasuryInsolvent`, etc.) rather than packing more values into 3300. This also matters
  narratively, not just technically: per CLAUDE.md, "the core product is the richness and
  coherence of the generated history" — a purchase, a severed trade route, or a civ going
  bankrupt are exactly the kind of legible history-log events this project is built to produce,
  and the original phase draft didn't enumerate any of them explicitly. Each phase (14.1-14.4)
  should name its event type(s) up front, not leave them implicit.
- **War reparations is a natural, cheap extension of 14.4's economic-ruin work — fold it in.**
  Confirmed: no reparations/indemnity/tribute mechanic exists anywhere today, and
  `EmissaryPurpose` (`Trade, Diplomacy, Spy, Religious`) has no tribute value —
  `CivTracker.Diplomacy.ResolveTrade` is purely a Trust bump with zero resource transfer, exactly
  as suspected. Since 14.4 is already building the civ-treasury-as-claim-on-`ResourceStores`
  mechanism and already extending the collapse pathway with economic-ruin scoring, a one-time
  post-war reparations transfer (loser's treasury pays winner's, using the same conversion as
  every other Wealth transfer in this milestone) is nearly free to add and gives economic ruin a
  second, causally legible trigger ("lost a war, paid reparations, treasury went negative, civ
  splintered") beyond pure trade mismanagement. Scope as a 14.4 sub-item, not a new phase.

**Explicit design tensions, resolved by NOT unifying (documented so it isn't rediscovered as an
oversight later):**

- **`RelationshipEdge.Debt` (M13.2) and `Wealth` (M14) are deliberately not the same system.**
  `Debt` is a signed [-1,1] *social obligation* scalar with no currency unit — it exists to bias
  War/Raid utility scoring and carries Trust/oath-breaking consequences. `Wealth` is literal,
  physically-backed money. They will sit side by side and both flow from the same `GrantAid`-
  shaped command family, which risks reading as redundant. Decision: leave them orthogonal for
  M14 — `Debt`'s magnitude stays a flat `AidDebtIncrement` config constant, not `Wealth`-scaled —
  because collapsing "how much someone owes emotionally" into "how much money changed hands" would
  lose the M13 characterization use case (a small material gift can still create a large social
  obligation between the right two characters, or vice versa). Revisit only if calibration data
  says the two mechanics feel confusingly duplicative in practice.
- **Ruins do not become a Wealth source in M14.** Confirmed `Ruin`/`RuinRecord` is purely a
  settlement-founding deterrent and history marker (decaying founding-penalty, no loot command of
  any kind exists). Adding ruin-looting is a genuinely new, self-contained mechanic (exploration,
  a claim/recovery command, risk/reward tuning) that's a plausible *future* Wealth source but is
  not folded into M14 — flagged as a candidate for a later milestone, not built speculatively here.

**Risks to actively watch during 14.5 calibration, not solved by design today:**

- **The 14.3 purchase mechanic may simply never fire, the same way `CharacterEstranged`/
  `OathBroken` never fired until diagnosed in the M13.8 follow-up session.** `PurchaseArtifact`
  stacks several independent preconditions (goal-holder has a live `CovetArtifact` goal, target
  isn't `Lost`, buyer's `Wealth` clears the price, owner's willingness check passes) — precisely
  the shape of conjunction that turned out to be structurally unreachable twice already in this
  project's history. 14.5 must include the same instrument-first calibration discipline used for
  that fix (a throwaway scratch test printing real counts/distributions across seeds) *before*
  assuming the mechanic works, rather than discovering at balance-sweep time that it's silently
  zero. Apply the same lesson to 14.1/14.2's paid-trade rollout — verify the money actually moves
  before assuming the formula is reachable.
- **Wealth concentration/inequality over a 10,000-year run is a distribution problem the
  `GlobalPriceIndex` (decision 8) does not solve — it only corrects the aggregate average.**
  Inheritance passes wealth to heirs, successful merchants keep winning future trade opportunities
  (nothing in 14.1/14.2 dampens an already-wealthy character's odds), and destruction is
  concentrated in war/raid losers rather than spread evenly — all of which point toward
  runaway dynastic concentration rather than a stable distribution. This may be a desirable
  feature (matches "wealthy merchant dynasties" from the original scope statement) or a
  degenerate outcome (a handful of characters own effectively all the money while
  `GlobalPriceIndex` prices everyone else out) — no way to know without data. 14.5 should
  explicitly measure wealth-distribution skew (e.g. a simple Gini-coefficient-style check across
  living characters) at year 300 and at the long-run checkpoint already planned for decision 8,
  not just the per-capita mean. Do not add a redistribution mechanism (taxation, wealth decay,
  etc.) speculatively — only if the data shows a genuine problem.
- **Tier2 population scale is bigger than "a few named role-holders."** Confirmed
  `Tier2PerPopulation = 10` — Tier2Characters scale at roughly world-population/10, not a small
  bounded dignitary set. The annual `GlobalPriceIndex` sweep (decision 8) is still cheap (annual
  cadence, not per-tick, and it's an O(n) sum with no per-character branching), but size any
  *other* new per-Tier2-character economic logic (14.1's paid-trade reward, in particular, which
  already runs on the existing per-tick `RunMerchant` path) with this real population scale in
  mind, not an assumption of "just a few merchants."
- **New randomness must go through `WorldRng` with a named salt, per existing convention** — 14.2
  in particular introduces several new rolls (interception, disaster-en-route, piracy loss) that
  are easy to accidentally implement with an ad hoc `Random` and silently break the
  reproducibility test. Not a new rule, just worth flagging given how much new roll surface this
  phase adds relative to prior milestones.

## Phase sequence

- **14.0 — Wealth substrate: seeded pricing table, global price index, personal balances, death
  disposition.** Add `EconomyConfig.BaseValuePerUnit` (every tradeable resource key → a seeded
  value-per-unit, per decision 7), the `LocalScarcityMultiplier` helper that reads the existing
  `SettlementStub.ResourceLedger` ratio to derive a per-settlement price from that base value
  (clamped to a configured band), and `WorldState.GlobalPriceIndex` plus its annual-tick update
  step (decision 8) — the whole pricing mechanism ships together in this phase since 14.1 needs a
  real price on day one, not a placeholder to retrofit later. No order book, no price history, no
  per-transaction convergence loop; the only thing that updates over time is the single global
  index correcting for money-supply drift. No new resource key, no mint action, no new production
  chain — reuses the existing commodity set and its existing (already-motivated,
  population-driven) mining output as-is. Add a `Wealth` float
  field to `Tier1Character` and `Tier2Character` (mirrors `Tier2Character.Notability`'s shape:
  internally-set accumulator) — but unlike `Notability`, `Wealth` must get real DTO/mapper
  coverage: add the field to `Tier1EntityDto`/`Tier2EntityDto` (`WorldStateDto.cs`) and wire both
  directions in `WorldStateMapper.cs`. `Notability` was never plumbed through and silently resets
  to 0 on load (deep-review finding) — worth a one-line fix alongside this work, in the same file,
  since it's the same bug. Death handling: extend
  `CharacterBehaviorPhase.KillCharacter`/`TransferDebtOnDeath`-adjacent logic with a
  `TransferWealthOnDeath` step — `WealthInheritanceShare` (config) goes to the heir (100% drops if
  no eligible heir exists, decision 5's revision), the remainder becomes an unclaimed pool — a
  parallel minimal `WealthDrop` concept at the death tile (Wealth is abstract/personal, not a
  physical settlement resource, so this does not touch `ResourceStores`) claimable by any
  co-located character via the same `GoalManager` co-location-claim mechanism M5 already built for
  Lost artifacts, and included in decision 8's `TotalMoneySupply` sum while unclaimed.
  **Also this phase, per decision 10's two fixes:** add `Organization.HomeSettlementCoord`
  (resolved once at founding, never re-derived from the leader's current location) and
  `Organization.Treasury` (a real stored `float`, with the same DTO/mapper coverage as `Wealth`) —
  both needed before 14.4 can build real treasury commands; and add
  `EconomyConfig.PersonalWealthSpoilageRate`, applied to every living character's `Wealth` (and to
  standing `WealthDrop` pools) on the same annual-tick sweep that computes `GlobalPriceIndex` — the
  sink that keeps personal Wealth from being an immortal accumulator. This phase has no *spend* use
  for Wealth yet — it's the substrate 14.1-14.4 build on.
- **14.1 — Wire Wealth into existing trade as its first source/sink.** Before building the new
  caravan system, make `Tier2BehaviorPhase.RunMerchant`'s *existing* one-shot trade actually pay
  the merchant in Wealth (replacing today's `MerchantTradeStatusGain`-only reward) — per decisions
  4, 7, and 8, this is a real transfer priced by the seeded formula: the destination settlement's
  own `ResourceStores["gold"/"silver"/"gems"]` are debited by `BaseValuePerUnit ×
  LocalScarcityMultiplier × GlobalPriceIndex` for the traded good, and the merchant's personal
  `Wealth` is credited by that same amount (unconditionally personal at this point in the sequence
  — no Guild exists yet until 14.4 lands, so decision 9's guild-routing check has nothing to route
  to; 14.4 revisits this exact line to add the Guild-member branch), so a
  settlement with no precious-commodity reserves simply can't pay (a natural, no-new-code scarcity
  constraint) and a settlement genuinely short on the traded good pays more (the existing M9 ledger
  ratio doing double duty as both the merchant's opportunity score, unchanged, and now the price).
  Gives Wealth a real, already-tested source before the more complex 14.2 caravan system exists,
  and de-risks the pricing/value-conversion plumbing (base values, spoilage, per-capita demand
  interaction) against a known-working trade path first. `MerchantTradeCompleted`'s civ-level reuse
  (the emissary-trust-bump path in
  `CivTracker.Diplomacy.ResolveTrade`) is left untouched — it isn't a resource transfer today and
  doesn't need to become one.
  **Opus-review addition:** paying the merchant 100% of the traded value strictly drains the
  destination and gives the home settlement nothing for supplying the goods — over a long run this
  both self-terminates trade (destination reserves exhaust, "can't pay" stops being occasional and
  becomes permanent) and makes hosting a merchant a net loss for their home settlement, at odds
  with the "wealthy merchant dynasties and their settlements" narrative goal. Add
  `EconomyConfig.MerchantHomeCutFraction` — a portion of the sale value routes back into the home
  settlement's own `ResourceStores["gold"/"silver"/"gems"]` (not the merchant's `Wealth`) instead of
  100% going to the merchant, so gold recirculates between settlements rather than draining
  one-way into personal Wealth. Directly softens decision 10's ratchet risk as well.
- **14.2 — Persistent trade routes: caravan travel-time simulation.** The largest phase. New
  `TradeRoute` entity (persistent, keyed by settlement-pair, replaces the current per-tick
  best-candidate scan in `RunMerchant` for civ-to-civ trade once established) and an in-transit
  caravan concept — goods committed to a route spend real ticks traveling (distance-derived from
  existing `TileCoord` geometry, no new pathfinding graph required beyond straight-line/known
  travel-time precedent from `SeaVoyage`, M11), during which they're vulnerable to interception
  (raid/war on the route's path), disaster, and piracy. A route persists across ticks and can be
  **severed** (war between the endpoints' civs, disaster along the path, sustained piracy losses)
  or reopened. This is the phase that actually delivers "persistent trade routes... that can be
  severed by war/disaster/piracy, creating dependency and scarcity stories" from the roadmap.
  `RunMerchant`'s existing one-shot opportunistic scan becomes the *route-formation* trigger
  (successful repeated trades between a pair graduate into a persistent `TradeRoute`) rather than
  being deleted outright.

  **Shipped 2026-08-05.** `Economy/TradeRoute.cs` adds `TradeRouteKey` (canonical, order-
  independent settlement-pair key), `TradeRoute` (mutable, mirrors `Organization` rather than a
  `with`-replaced record since status/loss-streak/in-flight-caravan all change in place every
  tick — `Status` Active/Severed, `FormedYear`, `ConsecutiveCaravanLosses`, `SeveredSinceTick`,
  `InTransit`), and `Caravan` (a plain record: `MerchantId`, `HomeTile`, `DestTile`, `Resource`,
  `Quantity`, `DepartTick`, `ArrivalTick`). **Travel-time precedent note:** M11's `SeaVoyage` turned
  out to be stepwise per-tile character movement with no separate duration/ETA record to reuse —
  the actual existing in-transit/ETA shape in this codebase is `Civilizations.PendingEmissary`
  (`DepartedYear`/`ArrivalYear` precomputed from Euclidean distance ÷ a travel-speed config
  constant, resolved when reached); `Caravan` mirrors that shape at tick granularity instead.
  **Scope decision:** at most one caravan in flight per route at a time — a merchant whose route
  already has a caravan en route simply makes no trade that tick rather than queuing a second one.
  `WorldState` gets `TradeRoutes` (`Dictionary<TradeRouteKey, TradeRoute>`) and
  `TradeRouteFormationProgress` (`Dictionary<TradeRouteKey, int>`, the pre-route trade-count
  tracker, entry removed once a pair graduates). `RunMerchant` now branches: no route yet → the
  existing 14.1 instant path runs unchanged and also increments the formation counter
  (`EconomyConfig.TradeRouteFormationThreshold`, default 3); an Active route with an empty slot →
  `DispatchCaravanOnRoute` debits the home settlement now and computes `ArrivalTick` from
  `EconomyConfig.CaravanSpeedTilesPerYear` (6.0, chosen as a slightly-slower-than-emissary
  magnitude next to `EmissaryTravelSpeedTilesPerYear`'s 8.0) via `SimLoopConfig.TicksPerYear`; a
  new per-tick `Tier2BehaviorPhase.RunTradeRoutes` sweep (called once per `Execute`, not per
  character) resolves any caravan whose `ArrivalTick` has been reached by delivering the goods and
  reusing 14.1's `ResolveMerchantTrade` pricing/home-cut/Wealth-credit path unchanged — the arrival
  *is* the trade completing, so it emits the same `TradePaid` event. **Risk rolls (interception/
  disaster/piracy):** each rolled once at arrival resolution (not stacked per tick during transit,
  which keeps the balance math a plain per-caravan Bernoulli trial) via `SimRngSalts
  .CaravanInterception/CaravanDisaster/CaravanPiracy` (940-942); interception
  (`CaravanInterceptionChance`, 0.2) only applies when the route's endpoint civs are at war,
  disaster (`CaravanDisasterChance`, 0.03) and piracy (`CaravanPiracyChance`, 0.02) roll
  regardless of war state. **Scope-narrowing decision:** all three share one consequence
  (`EventType.CaravanRaided` with a `Cause` field distinguishing "war"/"disaster"/"piracy") and one
  severance counter (`ConsecutiveCaravanLosses`) rather than three independently-tracked loss
  mechanics — narratively they're all "the caravan didn't arrive," and no separate naval/bandit
  infrastructure exists to hang a distinct piracy mechanic off of, matching the phase doc's
  allowance to fold piracy into interception when that's the case. **Severance:** `RunTradeRoutes`
  checks every route every tick (not just when a caravan is in flight) — an Active route severs
  immediately if its endpoint civs are at war or either endpoint settlement no longer exists
  (`"settlement-lost"`, a cheap defensive fold-in of the "disaster along the path" coarser check the
  phase doc allowed, using the existing settlement-destroyed signal rather than new infrastructure),
  or if `ConsecutiveCaravanLosses` reaches `EconomyConfig.TradeRouteSeverThreshold` (3) from any
  cause. **Reopening:** unified into a single rule regardless of severance cause — a Severed route
  reopens once its endpoint civs are no longer at war (vacuously true for a non-war severance) and
  `EconomyConfig.TradeRouteReopenCooldownTicks` (32, ~2 years) have elapsed since `SeveredSinceTick`;
  reopening reuses `EventType.TradeRouteFormed` with `Reopened: true` in the payload rather than a
  separate event type. `FormTradeRoute` is the one new `ICommand` (resolved immediately by
  `Tier2BehaviorPhase`, same non-`CivTracker`/non-`ResolveCommand` exemption as `CompleteMerchantTrade`
  — see its doc comment); caravan dispatch/arrival/severance/reopening are phase-driven system logic
  (no entity emits them), mirroring how `CivTracker.RunAnnualDiplomacy` resolves `PendingEmissary`
  directly without a command. Full DTO/mapper coverage: `TradeRouteDto`/`CaravanDto`
  (`WorldStateDto.cs`), `WorldStateMapper.MapTradeRoutes`/restore step 23. `SignificanceClassifier`/
  `VerbClassification`/UI wiring (`Presenter.cs`, `CharacterProfilePanel.cs`, `EventLogPanel.cs`)
  updated for all three new event types, following 14.1's `TradePaid` pattern. Tests:
  `WorldEngine.Tests/Unit/TradeRouteCaravanTests.cs` — formation-threshold gating, transit-duration
  + arrival-pricing correctness, three distributional roll-rate tests (2000 trials each), war
  severance + cooldown reopening, a full DTO round-trip, and a tick-budget integration test
  confirming a route actually forms and a caravan actually completes transit (same
  "verify the mechanic actually fires" discipline as the M13.8 Estrangement/OathBroken fix).
- **14.3 — Goal fulfillment via trade (Wealth's spend-side MVP).** Extend `GoalManager`'s
  `CovetArtifact` resolution with a purchase path: if the coveted artifact is owned by a living
  character/settlement (not `Lost`) and the goal-holder has sufficient `Wealth`, attempt a
  `PurchaseArtifact`-style command (mirrors `GrantAid`'s command shape) that transfers
  `Wealth ≥ ArtifactBaseValue × ArtifactValueMultiplier × GlobalPriceIndex` (decisions 7 and 8's
  artifact-pricing formula) to the owner and the artifact to the buyer, gated by the owner's
  willingness (a Trust/Compassion check, config-driven, on top of the price itself) — an
  alternative to the existing claim-if-Lost /
  conflict-escalation paths, not a replacement for either. Scoped narrowly to `CovetArtifact`
  because it's the only goal type with an existing "wants a specific unowned thing" shape; other
  goal types are not touched in M14.

  **Shipped 2026-08-05.** `PricingService.ArtifactBaseValue`/`ArtifactEffectivePrice` add the
  artifact-pricing formula; `EconomyConfig.ArtifactCategoryBaseValue`/
  `DefaultArtifactCategoryBaseValue` seed a new per-`ArtifactCategory` value ranking (Relic/Regalia
  highest, Artwork lowest) — no existing rarity/value table was actually keyed by `ArtifactCategory`
  (`CreatedGoodTaxonomy.CategoryWeights` is a category-*selection* probability table, not a value
  ranking, and `Artifact` only stores the resolved `Category`, not the originating
  `CreatedGoodType`), so this is a new seeded table rather than a reuse of an existing one — see
  `EconomyConfig`'s doc comment for the full reasoning. New `PurchaseArtifact(BuyerId, ArtifactId)`
  command (`EntityCommands.cs`) resolved immediately by `Economy/ArtifactPurchaseResolver.TryResolve`
  from within `GoalManager.UpdateGoals`'s `WorldState` overload — the same non-`CivTracker`/
  non-`ResolveCommand` exemption already documented for `CompleteMerchantTrade`/`FormTradeRoute`,
  consistent with that same method's existing claim-if-Lost path already mutating `WorldState`
  directly with no command at all. Willingness gate (`EconomyConfig.PurchaseWillingnessThreshold`,
  0.5) combines the owner's Personality.Compassion (Loyalty for Tier2, which has no Compassion axis)
  with any existing `RelationshipEdge.Trust` toward the buyer (0 for strangers) — deliberately not
  Trust alone, to avoid the M13.5-era Estrangement/OathBroken unreachable-threshold failure mode.
  Settlement-owned artifacts have no personality to gate on and are always willing once the price
  itself is met; a settlement-side sale credits the settlement's `ResourceStores["gold"]` with the
  price's gold-equivalent value (reversing decision 4's conversion), symmetric with a
  Character-owned sale crediting the owner's `Wealth` directly. New `EventType.ArtifactPurchased =
  3504` (continuing the 3500 range from 14.2's `CaravanRaided = 3503`), classified `VerbClass
  .Transfer`/`PopulationImpact.None` (same shape as `TradePaid`), with `Presenter.cs`/
  `CharacterProfilePanel.cs`/`EventLogPanel.cs` wired following the 14.1/14.2 pattern.

  **Instrument-first finding, required a real fix before the mechanic could fire at all (not just a
  config tweak):** a full-worldgen instrument run (5 seeds, 300 years, `TestSimConfig.Default()`)
  showed `ArtifactPurchased` firing 0/5 times — not because the purchase logic was wrong, but
  because **no living `Tier1Character` ever held any `Wealth` at all** in any seed. Root cause:
  14.1's `Tier2BehaviorPhase.ResolveMerchantTrade` only ever debited a destination's gold/silver/gems
  reserves, and those three commodities essentially never populate any settlement's `ResourceStores`
  at this world-generation scale (gold deposit tiles exist — 1-6 per world — but never land inside
  any settlement's owned territory), so `TradePaid` fired 0 times too despite the pre-existing
  `MerchantTradeCompleted` status marker firing 17-246 times per seed — the entire M14 Wealth
  substrate was reachable in hand-built unit tests but dead in organic full-sim play. Two additive
  fixes, both landed in this same session (not deferred, since 14.3 cannot be verified reachable
  without them): (1) `EconomyConfig.MoneyEquivalentCommodities` (new, replaces the hardcoded
  `PreciousCommodities` array — itself a latent CLAUDE.md "no hardcoded constant" violation)
  broadens the payable-currency set to include iron/copper, which are far more commonly mined;
  this in turn required excluding the just-traded `cmd.Resource` from the payable set in
  `ResolveMerchantTrade` (caught by two existing 14.1/M9 regression tests going red the moment the
  broadened list let a destination "pay" using the very units of the resource just physically
  delivered to it in the same trade — fixed, both tests green again). (2) `Tier2BehaviorPhase`'s
  dead-Tier2 handling now drops a dying merchant's `Wealth` as a `WealthDrop` at their home
  settlement tile (decision 5's existing mechanism, previously scoped to `Tier1Character` death
  only — `LivelihoodData.EmployerId`, the only Tier1 reference a Tier2 carries, is never actually
  assigned anywhere in the codebase, so there was no reliable direct link to hand Wealth to
  instead). After both fixes plus lowering `ArtifactValueMultiplier` (3.0 → 1.5, since
  `GlobalPriceIndex` is floor-pinned at 0.25 for the first few centuries per decision 8's documented
  warm-up transient, and the still-small early Wealth pool made even the cheapest artifact category
  unaffordable at 3.0), a re-run fired `ArtifactPurchased` in 3 of 5 seeds (42, 9999, 123 — up to 4
  times in one seed), kept as `ArtifactPurchaseTests.ShortRun_ArtifactPurchased_FiresAcrossA
  PlausibleFractionOfSeeds` (Balance-tagged, asserts ≥2/5 seeds fire at least once) — the same
  partial-seed reachability bar already accepted for `OathBroken`
  (`M13RelationshipEventBalanceTests`: "confirmed firing... in 4 of 8 other seeds sampled" while
  landing at 0 in the three canonical seeds). Full fire-rate calibration is still 14.5's job.
  Tests: `WorldEngine.Tests/Unit/ArtifactPurchaseTests.cs` — pricing formula, real two-sided Wealth
  transfer (Character and Settlement owner cases), blocked-when-unaffordable/unwilling/Lost/
  already-owned gates, `GoalManager` integration (purchase path additive alongside the untouched
  claim-if-Lost path), and the instrument-first integration test above. Full `scripts/test-fast.sh`
  (791 tests) and the Balance suite (5 tests, including this phase's new one) pass with zero
  warnings.
- **14.4 — Guild organizations, treasuries, and civ-level economic ruin.** Populate
  `OrganizationKind.Guild` for the first time (merchant characters with sustained trade
  volume/Wealth form or join a Guild, mirroring how Family orgs form in M13.0). Guild heads use
  the existing `SuccessionResolver.SelectSuccessor` kernel unmodified — no new succession
  mechanism, per the M12 audit note this milestone is explicitly bound by. **Guild/civ treasuries
  are `Organization.Treasury`, a real stored balance (decision 10 — revised from the original
  "live-computed claim" design, which couldn't represent insolvency or distinguish one org's money
  from another's sharing a settlement).** This phase adds decision 9's two treasury commands —
  `ContributeToTreasury` (any member, no authority check, moves personal `Wealth` →
  `Organization.Treasury`) and `WithdrawFromTreasury` (gated on `c.Id == org.LeaderId`, moves
  `Organization.Treasury` → the leader-designated member's personal `Wealth`) — and revisits 14.1's
  `RunMerchant` payment line to add the Guild-member branch: once a Guild exists, a member-
  merchant's trade income/expense routes to their Guild's `Treasury` instead of their personal
  `Wealth` automatically (no command change needed in `RunMerchant` itself, just a `Membership`
  lookup at payment time). Civ-level debt/economic ruin extends the *existing*
  `CivSplintered`/instability scoring (per the confirmed decision to reuse the collapse pathway,
  not build a parallel one) — a civ whose `Organization.Treasury` runs negative (now genuinely
  possible, decision 10) gains an instability contribution alongside the existing unrest/war-loss
  inputs, so "why did this civ fall" stays one legible causal chain instead of two, and
  `TreasuryInsolvent` (the new 3500-range event) is an actually-reachable event rather than
  structurally impossible as originally scoped. Also folds in **war reparations** (deep-review
  finding): on war resolution, a one-time Wealth transfer from the losing civ's `Treasury` to the
  winner's — allowed to drive the loser's `Treasury` negative, which is exactly the insolvency
  trigger above — using the same conversion mechanism as every other transfer in this milestone —
  no new `EmissaryPurpose` needed, this hangs off existing war-resolution, not diplomacy. Sequence
  reparations to resolve *before* any same-tick `CivSplintered` collapse check consumes the losing
  civ's org/treasury state, so a collapsing civ's reparations don't reference state the same tick
  already removed. Gives economic ruin a second legible trigger ("lost a war, paid reparations, treasury
  went negative, civ splintered") alongside pure trade mismanagement. New `EventType`s for this
  phase (`TreasuryInsolvent`, plus 14.1-14.3's `ArtifactPurchased`/`TradeRouteFormed`/
  `TradeRouteSevered`/`CaravanRaided`) get a fresh **3500 range** in `Core/Enumerations.cs` rather
  than extending 3300 — see the deep-review section above.
- **14.5 — Balance pass + economic ledger UI surface.** Full balance sweep (`scripts/test-balance.sh`,
  seeds 42/777/9999, 300-year runs) across the whole wealth lifecycle (`BaseValuePerUnit` seed
  values, `LocalScarcityMultiplier` clamp band, trade income, caravan loss rate, inheritance/
  lost-pool split, guild treasury drift, economic-ruin contribution to splinter rate) — this
  milestone touches more interlocking economic sources/sinks at once than any prior milestone, so
  budget real calibration time, not a single pass. **The standard 300-year window cannot validate
  decision 8** — money-supply drift is a multi-thousand-year phenomenon by construction, so this
  phase needs an additional long-run check (a 3-4k+ year run, at minimum, ideally toward the
  10k-year target the milestone is designed for) specifically watching whether
  `MoneySupplyPerCapita` and `GlobalPriceIndex` track each other and whether typical trade/
  purchase prices stay in a sane range at year 300 *and* year 5000 of the same run — a calibration
  that looks fine at 300 years tells you nothing about whether the index is keeping pace. **Before
  trusting any balance band, confirm each new mechanic actually fires** using the instrument-first
  scratch-test discipline from the M13.8 Estrangement/OathBroken fix (deep-review finding) —
  14.3's `PurchaseArtifact` in particular stacks enough independent preconditions to plausibly
  never fire without diagnosis. Also measure **wealth-distribution skew** across living characters
  (not just the per-capita mean `GlobalPriceIndex` tracks) at both the 300-year and long-run
  checkpoints, to see whether Wealth concentrates into a few dynasties or stays broadly
  distributed — informational only, do not add a redistribution mechanism speculatively; only if
  the data shows a genuine problem. **Opus-review additions to the instrumentation list, all
  needed before trusting any balance band:**
  - **A hard conservation invariant test** (unit + integration, write this before the balance
    sweep, not after) asserting `TotalMoneySupply` (decision 8's full formula, including
    treasuries and `WealthDrop` pools per decisions 5/10's revisions) changes only via the known
    source (mining production) and known sinks (settlement spoilage, raid destruction, the new
    `PersonalWealthSpoilageRate`) across every transfer path — the cheapest, highest-value guard
    against a leak in any of the transfer commands this milestone adds.
  - **Fraction of total money held as personal `Wealth` vs. settlement-held `ResourceStores`
    over time** — the direct diagnostic for whether the pre-decision-10 ratchet is actually fixed;
    if this trends toward ~1.0 despite `PersonalWealthSpoilageRate`, the rate is too low.
  - **Standing `WealthDrop` pool total and claim rate** — confirms decision 5's revision (measured
    + spoiling) rather than silently accumulating unclaimed.
  - **Count of settlements at or near zero precious-metal reserves, and merchant income trend,
    over the run** — detects the 14.1 home-settlement-drain risk even with
    `MerchantHomeCutFraction` in place.
  - **Whether `TreasuryInsolvent` ever actually fires** — this event was structurally impossible
    under the original (pre-decision-10) treasury design, so a balance run that never triggers it
    could mean either "civs are healthy" or "the event is still unreachable"; don't assume the
    former without seeing it fire at least once in a stress scenario.
  - **Whether `GlobalPriceIndex` is pinned at `PriceIndexMin`/`PriceIndexMax`** at either
    checkpoint — pinning means the clamp is fighting unbounded growth rather than tracking a
    genuinely equilibrating quantity (decision 8's revision).

  Also the
  "economic ledger... overlay" UI surface flagged as still-owed in the roadmap's audit notes
  (line ~9) — scope a read-only panel showing a settlement/character/guild's Wealth balance and
  recent trade/
  route activity, following the existing panel-on-kit pattern from M8.

## Open items intentionally deferred, not forgotten

- Interpersonal theft of Wealth (decision 6) — plausible M14.x or M16+ follow-up once a crime/law
  system is scoped; do not build speculatively.
- Faction-funding / "wealth buys political influence" (decision 3) — deferred; revisit once 14.3's
  goal-fulfillment spend path has real calibration data on how fast Wealth actually accumulates.
- `Tier2BehaviorPhase`'s per-role hardcoded routine design (`RunMerchant` et al.) — the roadmap's
  audit notes flag this as needing generalization shared with M18 (corrupt role-holders); M14 does
  not attempt that generalization unless 14.1-14.2 turn out to require it structurally.
