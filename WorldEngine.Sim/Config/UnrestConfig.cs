namespace WorldEngine.Sim.Config;

/// <summary>
/// Configuration for the settlement unrest / secession mechanic (S2 splinter).
/// All tunable constants for unrest accumulation, decay, and the secession trigger.
/// Bound from the <c>[unrest]</c> section of <c>sim_config.toml</c>.
/// </summary>
public sealed class UnrestConfig
{
    // ── Distance driver ───────────────────────────────────────────────────────
    /// <summary>
    /// Tile radius from the capital within which settlements are "comfortable" — no
    /// distance-driven unrest. Beyond this radius, unrest accumulates proportionally
    /// to the excess distance. Reuses the succession_stable_radius concept.
    /// </summary>
    public int   UnrestComfortRadius      { get; set; } = 15;

    /// <summary>
    /// Unrest accrued per tile of distance beyond UnrestComfortRadius per year.
    /// A settlement 30 tiles beyond the comfort radius accumulates
    /// 30 × UnrestDistancePerTile each year.
    /// </summary>
    public float UnrestDistancePerTile    { get; set; } = 0.005f;

    // ── Size driver ───────────────────────────────────────────────────────────
    /// <summary>
    /// Number of cities below which no size-driven unrest fires.
    /// Above this threshold, each additional city adds UnrestPerExcessCity per year
    /// to ALL distant settlements (the empire is becoming unwieldy).
    /// </summary>
    public int   UnrestSoftCityThreshold  { get; set; } = 4;

    /// <summary>
    /// Unrest added per excess city (above UnrestSoftCityThreshold) per year.
    /// At 8 cities (3 excess) and this=0.02: +0.06/yr to all distant settlements.
    /// </summary>
    public float UnrestPerExcessCity      { get; set; } = 0.05f;

    // ── Famine driver ─────────────────────────────────────────────────────────
    /// <summary>
    /// Unrest added per year when a settlement is in food crisis (food ratio &lt; crisis threshold).
    /// Represents popular discontent with a ruler who cannot feed them.
    /// </summary>
    public float UnrestFamineBonus        { get; set; } = 0.15f;

    // ── Succession crisis multiplier ──────────────────────────────────────────
    /// <summary>
    /// Multiplier applied to all unrest sources when the civ is in a succession crisis.
    /// Integrates with the existing SuccessionCrisisEndYear mechanic.
    /// </summary>
    public float UnrestSuccessionMult     { get; set; } = 1.5f;

    // ── Decay ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Fraction of unrest that decays each year when no drivers apply.
    /// 0.1 = 10% decay/year; half-life ≈ 7 years of stability.
    /// </summary>
    public float UnrestDecayRate          { get; set; } = 0.10f;

    // ── Secession trigger ─────────────────────────────────────────────────────
    /// <summary>
    /// Unrest threshold at which a settlement becomes eligible to secede.
    /// Annual probabilistic roll (not deterministic) to avoid all settlements
    /// seceding in the same tick.
    /// </summary>
    public float UnrestSecessionThreshold { get; set; } = 0.70f;

    /// <summary>
    /// Annual probability of secession roll succeeding when unrest ≥ threshold.
    /// 0.4 = ~40% chance per year above threshold; expected delay ~2-3 years.
    /// </summary>
    public float UnrestSecessionChance    { get; set; } = 0.40f;

    /// <summary>
    /// Maximum tile radius from the seceding settlement within which other
    /// same-civ high-unrest settlements can be swept into the new civ cluster.
    /// Limits cluster size — only nearby discontented settlements join.
    /// </summary>
    public int   UnrestClusterRadius      { get; set; } = 25;

    /// <summary>
    /// Minimum unrest for a neighbour settlement to join the seceding cluster
    /// (must also be closer to the secessionist settlement than to the parent capital).
    /// </summary>
    public float UnrestClusterMinUnrest   { get; set; } = 0.30f;

    /// <summary>
    /// Initial diplomatic tension imposed on the seceded civ toward its parent.
    /// Feeds into the existing BorderTension system used for war triggers.
    /// </summary>
    public float SplinterInitialTension   { get; set; } = 0.60f;

    /// <summary>
    /// Population floor below which secession probability is zero.
    /// </summary>
    public int SecessionMinCivPop { get; set; } = 500;

    /// <summary>
    /// Population range over which secession probability ramps from 0 to full.
    /// At SecessionMinCivPop the chance is 0; at SecessionMinCivPop + this value it is fully unlocked.
    /// </summary>
    public int SecessionPopRampRange { get; set; } = 500;
}
