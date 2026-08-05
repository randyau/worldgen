using WorldEngine.Sim.Entities.Artifacts;

namespace WorldEngine.Sim.Config;

/// <summary>
/// M14 14.0 — Wealth substrate: seeded commodity pricing, the global per-capita price index, and
/// the personal-Wealth sink. See docs/phases/m14_economy_independent_wealth.md (decisions 4, 7, 8,
/// 10) for the full design rationale. All values here are first-pass placeholders explicitly
/// flagged for 14.5's calibration pass — nothing here has been balance-tested yet.
/// </summary>
public sealed class EconomyConfig
{
    /// <summary>
    /// Decision 7 — one designer-authored value-per-unit for every tradeable resource key that
    /// already exists in the resource ledger/stores system (see ResourcePressurePhase's ledger
    /// keys: "food", "timber", and the lowercased ResourceDeposit.DepositType strings emitted by
    /// ResourceLayer — "iron", "copper", "stone", "obsidian", "sulfur", "gold", "coal", "herbs",
    /// "wild_game", "clay", "flint"). "silver"/"gems" do not exist as deposit types generated
    /// anywhere in WorldGen today (ResourcePressurePhase's spoilage switch already special-cases
    /// them for forward compatibility even though they never populate) — seeded here anyway so a
    /// future deposit type or discovery bonus can plug in without a config change. Static relative-
    /// scarcity ranking (gems > gold > silver > obsidian > iron/copper > coal > stone/flint/clay >
    /// timber > herbs/wild_game > food > water), not something the sim ever updates.
    /// 14.5 calibrates the actual numbers against real trade volume.
    /// TOML section: [economy.base_value_per_unit]
    /// </summary>
    public Dictionary<string, float> BaseValuePerUnit { get; set; } = new();
    public float DefaultBaseValuePerUnit { get; set; } = 1f;

    /// <summary>Looks up a resource's seeded base value, falling back to <see cref="DefaultBaseValuePerUnit"/>
    /// for any tradeable resource key not explicitly listed (e.g. a future deposit type).</summary>
    public float GetBaseValue(string resourceKey) =>
        BaseValuePerUnit.TryGetValue(resourceKey, out float v) ? v : DefaultBaseValuePerUnit;

    // ─── Local scarcity (decision 7) ──────────────────────────────────────────
    // Clamp band for LocalScarcityMultiplier — derived from SettlementStub.ResourceLedger's
    // per-capita supply/demand ratio, so no single settlement's price can run away.
    public float LocalScarcityMultiplierMin { get; set; } = 0.5f;
    public float LocalScarcityMultiplierMax { get; set; } = 2.0f;

    // ─── Global price index (decision 8) ──────────────────────────────────────
    // Anchor for "what a fair per-capita money supply looks like" — tuned during 14.5, not derived.
    // 14.5 CALIBRATION: the original placeholder (50f) was ~2500-5000x too high — a 3000-year,
    // 3-seed long-run instrument (EconomyBalanceInstrumentationTests.LongRun_...) observed
    // MoneySupplyPerCapita settling in the 0.01-0.02 range post-warm-up (year 300 onward), which
    // pinned GlobalPriceIndex at PriceIndexMin for the entire run — the exact failure mode decision
    // 8 warns about ("any finite clamp eventually saturates" if the reference anchor is wrong).
    // Re-anchored to the observed long-run equilibrium (not year-300 data, per the phase doc) so the
    // index actually tracks supply instead of sitting pinned at the floor.
    public float ReferenceMoneySupplyPerCapita { get; set; } = 0.015f;
    public float PriceIndexMin { get; set; } = 0.25f;
    public float PriceIndexMax { get; set; } = 4.0f;
    // EMA smoothing factor, same shape as SettlementStub.SpecializationStrength's EMA
    // (ResourcePressurePhase.UpdateSpecialization / SpecializationSmoothingAlpha).
    public float PriceIndexEmaAlpha { get; set; } = 0.05f;

    // ─── Death disposition (decision 5) ───────────────────────────────────────
    // Fraction of a dying character's Wealth passed to their heir; the remainder (or all of it, if
    // no eligible heir exists) becomes an unclaimed WealthDrop pool at the death tile.
    public float WealthInheritanceShare { get; set; } = 0.7f;

    // ─── Personal Wealth sink (decision 10) ───────────────────────────────────
    // "Cost of living" annual bleed on every living character's personal Wealth and on standing
    // WealthDrop pools. Must be much larger than ResourcePressureConfig.WealthSpoilageRate (gold/
    // gems in ResourceStores are "essentially permanent" at 0.0001) — without a real sink here,
    // personal Wealth is an unbounded accumulator and GlobalPriceIndex's clamp never means
    // anything (see decision 10's fix rationale). Gives per-capita Wealth a finite equilibrium
    // ceiling (income ÷ rate) instead of unbounded growth. Tuned during 14.5 alongside
    // WealthInheritanceShare so a lifetime of earnings isn't erased faster than it can be spent.
    public float PersonalWealthSpoilageRate { get; set; } = 0.02f;

    // ─── Artifacts (decision 7, used from 14.3 onward) ────────────────────────
    // Scalar reflecting that an exceptional creation is worth more than raw commodity value.
    // Declared here because it's a pricing-formula constant, even though nothing spends it yet.
    // 14.5 CALIBRATION: re-tuned 1.5 -> 0.35. Fixing GlobalPriceIndex's pinned-at-floor bug (see
    // ReferenceMoneySupplyPerCapita above) moved the index from a constant ~0.25 to floating near
    // ~1.0 in the short run — a ~4x jump in EffectivePrice for every artifact purchase, which
    // dropped ArtifactPurchased's observed fire rate from 3/5 to 1/5 seeds. Lowered proportionally
    // so the purchase mechanic's reachability isn't an accidental casualty of an unrelated pricing
    // fix.
    public float ArtifactValueMultiplier { get; set; } = 0.35f;

    // DECISION (14.3): decision 7's text claims "the G-1 taxonomy already weights [artifact
    // categories] for persistence/rarity — reuse that weighting." Checked directly:
    // CreatedGoodTaxonomy.CategoryWeights is a CreatedGoodType → (Category, probability) table for
    // *which category a crafted good becomes*, not a value/rarity ranking keyed by category — and
    // Artifact itself only stores the resolved Category, not the originating CreatedGoodType, so
    // there is nothing of that shape to "reuse." No other per-category value/rarity table exists
    // anywhere in the codebase (grepped for Rarity/CategoryValue — no hits). Simplest reasonable
    // choice per CLAUDE.md's ambiguity rule: a new seeded per-category base-value table, authored
    // the same way as BaseValuePerUnit's static relative-scarcity ranking (decision 7) — Relic and
    // Regalia (legendary/rulership-coded) rank highest, ordinary Artwork lowest.
    public Dictionary<string, float> ArtifactCategoryBaseValue { get; set; } = new();
    public float DefaultArtifactCategoryBaseValue { get; set; } = 10f;

    /// <summary>Looks up a category's seeded base value, falling back to
    /// <see cref="DefaultArtifactCategoryBaseValue"/> for any category not explicitly listed.</summary>
    public float GetArtifactCategoryBaseValue(ArtifactCategory category) =>
        ArtifactCategoryBaseValue.TryGetValue(category.ToString(), out float v) ? v : DefaultArtifactCategoryBaseValue;

    // ─── Goal fulfillment via trade (14.3) ─────────────────────────────────────
    // Willingness gate on top of the price itself (decision 3/14.3): an artifact's Character owner
    // must be "willing to sell" independent of whether the buyer can afford it. Reuses the existing
    // Compassion-driven willingness pattern already established for GrantAid/Placate (
    // UtilityScorer.ActionType.GrantAid scores on Personality.Compassion; CivTracker.ResolveGrantAid
    // gates on RelationshipEdge.Trust) rather than inventing a new shape — combined here as
    // Compassion (always populated, no relationship prerequisite) plus any existing Trust bonus
    // (defaults to 0 for strangers), so the check is structurally reachable even between two
    // characters with no prior relationship edge — deliberately avoiding the M13.5-era
    // Estrangement/OathBroken failure mode where a threshold gated on relationship Trust alone
    // (which realistically never clears ~0.4 between unrelated pairs) made a mechanic
    // unreachable. Settlement-held artifacts have no personality to gate on, so a
    // settlement-owned artifact is always willing to sell (a settlement's "collection" is public
    // civic property, not a personal attachment) — see ArtifactPurchaseResolver.
    public float PurchaseWillingnessThreshold { get; set; } = 0.5f;

    // DECISION (14.3 instrument-first finding): the money-equivalent commodity list a destination
    // pays from (Tier2BehaviorPhase.ResolveMerchantTrade) was originally a hardcoded
    // { "gold", "silver", "gems" } array — a violation of CLAUDE.md's "no hardcoded simulation
    // constant" rule in its own right, but also the actual root cause of 14.3's purchase mechanic
    // never firing: a full-worldgen instrument run (5 seeds, 300 years, TestSimConfig.Default())
    // showed TradePaid fired 0 times in every seed despite MerchantTradeCompleted (the pre-14.1
    // status-gain marker) firing 17-246 times — gold/silver/gems deposits exist (1-6 tiles/world)
    // but never land inside any settlement's owned territory at this world size, so no settlement
    // ever has a nonzero gold/silver/gems store to pay from, and Wealth accordingly never reaches
    // any Tier2 merchant (livingT2WithWealth was 0 in all 5 seeds) or, by extension, any Tier1
    // CovetArtifact buyer. iron/copper are far more commonly deposited/mined (MerchantTradeCompleted's
    // high counts confirm plenty of iron/copper physically trades hands) and already have seeded
    // BaseValuePerUnit entries, so adding them to the money-equivalent list is what actually makes
    // the entire M14 Wealth substrate reachable in organic play — same fix category as the M13.5
    // Estrangement/OathBroken threshold rebalance (an existing gate was structurally unreachable at
    // realistic population/geography scale, not logically wrong). Order is priority: a payment
    // draws down the most commonly-available commodity first.
    public List<string> MoneyEquivalentCommodities { get; set; } = new() { "gold", "silver", "gems", "iron", "copper" };

    // ─── Trade payment (14.1, Opus-review addition) ───────────────────────────
    // Paying the merchant 100% of a sale's value strictly drains the destination settlement and
    // gives the merchant's home settlement nothing for having supplied the goods — over a long
    // run this both self-terminates trade (destination precious-commodity reserves exhaust, so
    // "can't pay" stops being occasional and becomes permanent) and makes hosting a merchant a
    // net loss for their home settlement, at odds with the "wealthy merchant dynasties and their
    // settlements" narrative goal. This fraction of the paid value instead routes back into the
    // home settlement's own ResourceStores (in the same commodities the destination paid with)
    // rather than the merchant's personal Wealth, so precious-commodity gold recirculates between
    // settlements instead of draining one-way into a sink-free personal pool (also directly
    // softens decision 10's one-way-ratchet risk). Tuned during 14.5 alongside the other transfer
    // constants; 0.3 is a first-pass placeholder that leaves the merchant with the majority share
    // while still returning a meaningful cut home.
    public float MerchantHomeCutFraction { get; set; } = 0.3f;

    // ─── Persistent trade routes / caravans (14.2) ────────────────────────────
    // Number of successful one-shot RunMerchant trades between the same settlement pair before
    // they graduate into a persistent TradeRoute (Tier2BehaviorPhase.MaybeFormTradeRoute).
    public int TradeRouteFormationThreshold { get; set; } = 3;

    // Caravan travel speed, tiles/year — comparable magnitude to EmissaryConfig
    // .EmissaryTravelSpeedTilesPerYear (8.0), since both represent overland travel between
    // settlements/civs; a caravan hauling goods is modeled slightly slower than a lone emissary.
    public float CaravanSpeedTilesPerYear { get; set; } = 6f;

    // Per-arrival-check roll (WorldRng, named salts — see SimRngSalts.CaravanInterception/
    // Disaster/Piracy) that a caravan is lost in transit instead of completing its trade. Rolled
    // once at arrival resolution (representing the whole transit's cumulative risk), not stacked
    // per-tick during travel — simpler, and keeps the distributional balance test's math a
    // straightforward per-caravan Bernoulli trial. Interception only applies when the route's two
    // endpoint civs are at war; disaster and piracy roll regardless of war state.
    public float CaravanInterceptionChance { get; set; } = 0.2f;
    public float CaravanDisasterChance     { get; set; } = 0.03f;
    public float CaravanPiracyChance       { get; set; } = 0.02f;

    // Consecutive lost caravans (any cause) on the same route before it severs from sustained
    // losses (independent of the immediate war/lost-settlement severance checks).
    public int TradeRouteSeverThreshold { get; set; } = 3;

    // Ticks a Severed route must wait, once its severing condition (war / missing endpoint) has
    // cleared, before automatically reopening. ~2 years at the default 16 ticks/year.
    public int TradeRouteReopenCooldownTicks { get; set; } = 32;

    // ─── Guild organizations, treasuries, civ-level economic ruin (14.4) ─────
    // See docs/phases/m14_economy_independent_wealth.md decision 9/10, phase-sequence "14.4" entry.

    // Personal Wealth threshold at which a Tier2 Merchant forms (or joins, if one already exists
    // at their home settlement) a Guild organization — the "sustained trade volume" proxy: Wealth
    // already accumulates only from completed, paid trades (Tier2BehaviorPhase.ResolveMerchantTrade),
    // so a Wealth threshold is a direct measure of sustained trading rather than a second counter.
    // First-pass placeholder; 14.5 calibrates against observed merchant Wealth distributions.
    public float GuildFormationWealthThreshold { get; set; } = 40f;

    // Personal Wealth threshold above which a Tier1 Organization member is willing to voluntarily
    // deposit into their Organization's Treasury (ContributeToTreasury) — mirrors GrantAid's
    // need-threshold shape but on the giving side. No authority check (decision 9): any member.
    public float ContributeToTreasuryWealthThreshold { get; set; } = 20f;

    // Flat amount moved per ContributeToTreasury/WithdrawFromTreasury action (capped at the
    // available balance on both sides — contribution can't exceed the contributor's Wealth,
    // withdrawal can't exceed the Organization's Treasury). First-pass placeholder.
    public float ContributeToTreasuryAmount { get; set; } = 10f;
    public float WithdrawFromTreasuryAmount { get; set; } = 10f;

    // Annual unrest accrual bonus (RunUnrestAndSecession Driver 4) applied to every settlement of
    // a civ whose Organization.Treasury is negative — extends the *existing* CivSplintered/
    // instability scoring rather than a parallel collapse pathway (decision: reuse the pathway).
    public float TreasuryInsolvencyUnrestBonus { get; set; } = 0.05f;

    // War reparations (deep-review finding folded into 14.4): amount transferred from the losing
    // civ's Treasury to the winner's on war resolution, scaled by the same battle-win advantage
    // CivTracker.EndWarBetween already computes for territory transfer (TilesPerBattleWin) — a
    // flat per-battle-win-advantage amount rather than a fraction of the loser's Treasury, since a
    // fraction of an already-near-zero or negative Treasury couldn't meaningfully drive it further
    // negative, which the design explicitly wants reparations to be able to do.
    public float WarReparationsPerBattleWinAdvantage { get; set; } = 15f;
}
