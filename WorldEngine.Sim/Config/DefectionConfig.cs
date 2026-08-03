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

    /// <summary>
    /// Ticks after a defection before the same character is eligible to defect again. Without
    /// this, a character stuck in a chronic Wellbeing crisis re-selects Defect on every tick a
    /// different-civ confidant is available, bouncing between civs indefinitely over a long life.
    /// 64 ticks = 4 years at 16 ticks/year.
    /// </summary>
    public int DefectionCooldownTicks { get; set; } = 64;
}
