namespace WorldEngine.Sim.Config;

public class OceanConfig
{
    /// <summary>
    /// Fraction of tiles (by elevation rank) that become ocean.
    /// 0.35 = 35% ocean, 65% land.
    /// </summary>
    public float DefaultSeaLevel { get; set; } = 0.35f;

    /// <summary>
    /// Number of erosion passes after elevation thresholding.
    /// Each pass converts non-ocean tiles with >= MinOcean8Neighbors ocean
    /// 8-neighbors to ocean. Removes narrow ridges (1–2 tiles wide) that
    /// protrude above sea level due to tectonic fault boosts.
    /// </summary>
    public int ErosionPasses { get; set; } = 2;

    /// <summary>
    /// How many of a tile's 8 neighbors must be ocean for that tile to be
    /// eroded to ocean in each pass. 5 catches 1-tile-wide ridges (6 ocean
    /// neighbors) and 2-tile-wide ridges (5 ocean neighbors at corners).
    /// Solid coast tiles have ≤4 ocean neighbors and are untouched.
    /// </summary>
    public int MinOcean8Neighbors { get; set; } = 5;
}
