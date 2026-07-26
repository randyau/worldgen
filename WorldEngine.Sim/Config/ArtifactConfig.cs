namespace WorldEngine.Sim.Config;

/// <summary>
/// Artifact generation and ownership constants.
/// Loaded from the [artifacts] section of sim_config.toml.
/// </summary>
public sealed class ArtifactConfig
{
    /// <summary>At max skill, probability per crafting task that a legendary artifact is produced.</summary>
    public float BaseGenerationProbability    { get; set; } = 0.05f;

    /// <summary>Quality score above which a crafting performance is considered notable for artifact rolls.</summary>
    public float NotablePerformanceThreshold  { get; set; } = 0.75f;

    /// <summary>Artifact quality score above which ambitious characters will covet the item.</summary>
    public float CovetThreshold               { get; set; } = 0.6f;

    /// <summary>Chance a decisive battle forges a legendary artifact.</summary>
    public float BattleForgeProbability       { get; set; } = 0.03f;

    /// <summary>Chance a legendary character's combat death forges a legacy artifact.</summary>
    public float HeroicDeathForgeProbability  { get; set; } = 0.10f;

    /// <summary>
    /// Probability that an artifact becomes Lost (vs. inherited by the settlement)
    /// when its character owner dies.
    /// </summary>
    public float LostOnDeathProbability       { get; set; } = 0.35f;

    /// <summary>
    /// Minimum Ambition personality score for a character to form covet-artifact goals.
    /// Characters below this threshold don't care enough about power/prestige to pursue artifacts.
    /// </summary>
    public float CovetAmbitionThreshold       { get; set; } = 0.55f;

    /// <summary>
    /// Maximum number of covet goals a character can hold simultaneously.
    /// Prevents obsessively coveting every artifact in the world.
    /// </summary>
    public int   CovetMaxGoals               { get; set; } = 2;

    /// <summary>
    /// Annual probability that a Lost (ownerless) artifact is destroyed — lost to history,
    /// crumbled to dust, buried in a forgotten ruin. This is the primary destruction sink that
    /// bounds long-term artifact accumulation; Lost items decay fastest as no one safeguards them.
    /// </summary>
    public float LostArtifactAnnualDecay      { get; set; } = 0.008f;

    /// <summary>
    /// Annual probability that an owned (character- or settlement-held) artifact is destroyed
    /// by accident, disaster, or war. Much lower than the Lost rate — owners protect their relics.
    /// </summary>
    public float OwnedArtifactAnnualDecay     { get; set; } = 0.001f;

    /// <summary>
    /// M9 G-2: weighted category roll for battle-forged artifacts (decisive campaign victory).
    /// No CreatedGoodType context exists for combat-triggered forging, so these are independent
    /// tunable weights (must sum to ~1.0) rather than derived from CreatedGoodTaxonomy.
    /// </summary>
    public float BattleCategoryWeightWeapon   { get; set; } = 0.5f;
    public float BattleCategoryWeightArmor    { get; set; } = 0.35f;
    public float BattleCategoryWeightRegalia  { get; set; } = 0.15f;

    /// <summary>M9 G-2: weighted category roll for heroic-death artifacts (must sum to ~1.0).</summary>
    public float HeroicDeathCategoryWeightWeapon  { get; set; } = 0.5f;
    public float HeroicDeathCategoryWeightRelic   { get; set; } = 0.3f;
    public float HeroicDeathCategoryWeightRegalia { get; set; } = 0.2f;
}
