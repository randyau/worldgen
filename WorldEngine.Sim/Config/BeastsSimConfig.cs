namespace WorldEngine.Sim.Config;

/// <summary>
/// Beast lifecycle constants from the [beasts] section of sim_config.toml.
/// Species-specific values (health, strength, etc.) live in config/beasts.toml.
/// </summary>
public sealed class BeastsSimConfig
{
    public int StarvationHealthLoss { get; set; } = 5;
}
