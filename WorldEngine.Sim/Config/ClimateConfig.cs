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

    // Phase 5 — climate drift (annual)
    public float AnnualTempDriftRate { get; set; } = 0.0f;
    public float MaxWarmingAnomaly { get; set; } = 5.0f;
    public float MaxCoolingAnomaly { get; set; } = 3.0f;
    public float StormCorridorShiftPerDegree { get; set; } = 0.005f;
    public float MonsoonAnomalySensitivity { get; set; } = 0.01f;
    public float MonsoonMultiplierMin { get; set; } = 0.5f;
    public float MonsoonMultiplierMax { get; set; } = 3.0f;
    public float LatTemperatureAnomalyScale { get; set; } = 1.4f;

    // Phase 5 — sea level (annual)
    public float AnnualSeaLevelDriftRate { get; set; } = 0.0f;
    public float SeaLevelEventThreshold { get; set; } = 0.1f;
    public float VolcanicDecayRate { get; set; } = 0.05f;
}
