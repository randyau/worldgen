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
    public float ReferenceMoneySupplyPerCapita { get; set; } = 50f;
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
    public float ArtifactValueMultiplier { get; set; } = 3f;

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
}
