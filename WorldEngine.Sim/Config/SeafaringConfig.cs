namespace WorldEngine.Sim.Config;

/// <summary>
/// Constants governing character sea voyages (M11 — character water crossings).
/// Loaded from the [seafaring] section of sim_config.toml.
/// </summary>
public sealed class SeafaringConfig
{
    /// <summary>Master toggle. When false, no SeaVoyage goals are ever seeded or scored.</summary>
    public bool OceanCrossingEnabled { get; set; } = true;

    // DECISION: no km-per-tile constant exists elsewhere in the codebase to derive this from
    // precisely (WorldConfig only carries WidthKm/HeightKm/TileWidthKm at the world level, not a
    // fixed per-tile scale used by character logic). 12 shallow-ocean tiles is enough to cross a
    // strait several tiles wide without turning into open-ocean pathfinding; tune once playtested.
    /// <summary>Max shallow-ocean tiles a voyage route may cross from the departure shore to the
    /// nearest reachable far-shore coastal tile.</summary>
    public int MaxVoyageTiles { get; set; } = 12;
}
