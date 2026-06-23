namespace WorldEngine.Sim.Entities.Beasts;

/// <summary>
/// Combat resolution parameters from the [combat] section of config/beasts.toml.
/// </summary>
public sealed class CombatConfig
{
    public int MaxRoundsPerTick { get; set; } = 8;
    public int MaxGangSize { get; set; } = 3;
    public float RetreatHealthFraction { get; set; } = 0.25f;
}
