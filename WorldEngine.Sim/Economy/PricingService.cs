using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Entities.Artifacts;

namespace WorldEngine.Sim.Economy;

/// <summary>
/// M14 14.0 — seeded, formulaic pricing (decision 7). No order book, no price history, no
/// transaction-volume dependency: every price is computed fresh at the moment of use from
/// EconomyConfig.BaseValuePerUnit and the settlement's existing (already-balanced) M9
/// SettlementStub.ResourceLedger ratio. See docs/phases/m14_economy_independent_wealth.md.
/// </summary>
public static class PricingService
{
    /// <summary>
    /// Derives a per-settlement price multiplier from the settlement's existing ResourceLedger
    /// per-capita supply/demand ratio for <paramref name="resourceKey"/> (ratio &gt; 1 = surplus,
    /// pushes price down; ratio &lt; 1 = deficit, pushes price up) — reusing the existing M9 demand
    /// signal rather than building a new one. The relationship is inverse (scarcity raises price),
    /// clamped to EconomyConfig's configured band so no single settlement's price can run away.
    /// </summary>
    public static float LocalScarcityMultiplier(SettlementStub settlement, string resourceKey, EconomyConfig cfg)
    {
        float ratio = settlement.ResourceLedger is { } ledger && ledger.TryGetValue(resourceKey, out float r)
            ? r
            : 1f;
        // ratio <= 0 would invert to +infinity; floor it so a totally-absent resource just clamps
        // to the max multiplier instead of blowing up.
        float multiplier = 1f / Math.Max(0.01f, ratio);
        return Math.Clamp(multiplier, cfg.LocalScarcityMultiplierMin, cfg.LocalScarcityMultiplierMax);
    }

    /// <summary>
    /// Full effective price for a commodity trade at a given settlement: BaseValuePerUnit ×
    /// LocalScarcityMultiplier × GlobalPriceIndex (decision 8's global drift correction).
    /// </summary>
    public static float EffectivePrice(SettlementStub settlement, string resourceKey, EconomyConfig cfg, float globalPriceIndex)
        => cfg.GetBaseValue(resourceKey) * LocalScarcityMultiplier(settlement, resourceKey, cfg) * globalPriceIndex;

    /// <summary>
    /// M14 14.3 — an artifact's base value: EconomyConfig.GetArtifactCategoryBaseValue(Category) ×
    /// Quality (decision 7). No scarcity multiplier — artifacts are unique 1-of-1 by construction,
    /// so "scarcity" is trivially 1 and carries no market signal the way a commodity's ledger ratio does.
    /// </summary>
    public static float ArtifactBaseValue(Artifact artifact, EconomyConfig cfg)
        => cfg.GetArtifactCategoryBaseValue(artifact.Category) * artifact.Quality;

    /// <summary>
    /// Full effective price for an artifact purchase (decision 8): ArtifactBaseValue ×
    /// ArtifactValueMultiplier × GlobalPriceIndex.
    /// </summary>
    public static float ArtifactEffectivePrice(Artifact artifact, EconomyConfig cfg, float globalPriceIndex)
        => ArtifactBaseValue(artifact, cfg) * cfg.ArtifactValueMultiplier * globalPriceIndex;
}
