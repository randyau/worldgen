namespace WorldEngine.Sim.Config;

public class OceanConfig
{
    /// <summary>
    /// Fraction of tiles (by elevation rank) that become ocean.
    /// 0.35 = 35% ocean, 65% land.
    /// </summary>
    public float DefaultSeaLevel { get; set; } = 0.35f;
}
