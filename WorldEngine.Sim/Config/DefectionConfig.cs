namespace WorldEngine.Sim.Config;

/// <summary>
/// M13 13.4: non-ruler bonds reaching the wider world — a cross-civ friendship strong enough
/// that a character in personal crisis seeks asylum with their foreign confidant rather than
/// enduring their own civ. Loaded from the [defection] section of sim_config.toml.
/// </summary>
public sealed class DefectionConfig
{
    /// <summary>Minimum Trust with a co-located foreign confidant to be considered for defection.</summary>
    public float ConfidantTrustThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Wellbeing must be at or below this (same scale as CharacterSimConfig.SpiralThreshold) before
    /// asylum-seeking becomes attractive — a character in good standing doesn't abandon their civ.
    /// </summary>
    public float WellbeingCrisisThreshold { get; set; } = -0.3f;

    /// <summary>Trust gained between defector and confidant once the defection succeeds.</summary>
    public float PostDefectionTrustGain { get; set; } = 0.2f;
}
