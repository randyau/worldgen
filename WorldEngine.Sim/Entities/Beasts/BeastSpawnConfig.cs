namespace WorldEngine.Sim.Entities.Beasts;

/// <summary>
/// Global beast spawn settings from the [beast_spawn] section of config/beasts.toml.
/// </summary>
public sealed class BeastSpawnConfig
{
    public float TargetDensityPer10kTiles { get; set; } = 5f;
    public float MythStartFraction { get; set; } = 0.20f;
    public int MythEmergenceYears { get; set; } = 200;
}
