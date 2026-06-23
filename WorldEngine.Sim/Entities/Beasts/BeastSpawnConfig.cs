namespace WorldEngine.Sim.Entities.Beasts;

/// <summary>
/// Global beast spawn settings from the [beast_spawn] section of config/beasts.toml.
/// </summary>
public sealed class BeastSpawnConfig
{
    public float TargetDensityPer10kTiles { get; set; } = 20f;
    public float MythStartFraction { get; set; } = 0.20f;
    public int MythEmergenceYears { get; set; } = 200;
    /// <summary>
    /// Food restored per season from ambient prey (rodents, fish, insects) — the untracked lower food web.
    /// Keeps apex predators viable until Phase 2.4 adds real population dynamics.
    /// </summary>
    public float PassiveFoodRecovery { get; set; } = 0.04f;
}
