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
}
