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
}
