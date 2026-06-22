namespace WorldEngine.Sim.Config;

public class ClimateConfig
{
    // World-state multipliers (used at sim runtime)
    public float StormCorridorMoistureBonus { get; set; } = 1.3f;
    public float MonsoonIntensityMultiplier { get; set; } = 1.5f;

    // World-gen parameters (used during ClimateLayer generation)
    public float TropicalBandHalfWidth { get; set; } = 0.25f;
    public float RainShadowLossFraction { get; set; } = 0.6f;
    public byte MountainElevationThreshold { get; set; } = 180;
    public byte MonsoonMoistureThreshold { get; set; } = 160;
    public float StormCorridorNormalizedLat { get; set; } = 0.35f;
    public float StormCorridorHalfWidth { get; set; } = 0.08f;
    public float StormCorridorMoistureBonusGenesis { get; set; } = 0.3f;
}
