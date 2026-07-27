namespace WorldEngine.Sim.Config;

/// <summary>
/// Constants governing character sea voyages (M11 — character water crossings).
/// Loaded from the [seafaring] section of sim_config.toml.
/// </summary>
public sealed class SeafaringConfig
{
    /// <summary>Master toggle. When false, no SeaVoyage goals are ever seeded or scored.</summary>
    public bool OceanCrossingEnabled { get; set; } = true;
}
