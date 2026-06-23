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

    // World-gen: moisture transport
    /// <summary>
    /// Fraction of moisture retained per tile as wind sweeps inland (0–1).
    /// 0.97 = aggressive drying (deep interiors become desert by ~50 tiles).
    /// 0.993 = gentler drying (moisture reaches ~70% at 50 tiles, ~45% at 100 tiles).
    /// </summary>
    public float MoistureCarryDecay { get; set; } = 0.97f;

    // World-gen: temperature variation
    /// <summary>
    /// Amplitude of coherent noise added to the latitude temperature fraction [0–1].
    /// Breaks horizontal biome banding by introducing regional temperature anomalies
    /// (analogous to ocean currents, land mass effects). 0 = pure latitude bands.
    /// 0.12–0.20 produces realistic regional variation without destroying the gradient.
    /// </summary>
    public float TemperatureNoiseScale { get; set; } = 0f;

    /// <summary>
    /// Noise frequency for temperature anomalies. Low values (0.01–0.02) produce
    /// broad regional anomalies; higher values produce finer-grained variation.
    /// </summary>
    public float TemperatureNoiseFrequency { get; set; } = 0.015f;

    /// <summary>
    /// Amplitude of coherent noise (in byte units, 0–255) added to base moisture
    /// after wind sweeps. Breaks horizontal moisture banding — moisture from wind
    /// sweeps is identical across each latitude row, so noise is the only source of
    /// east-west variation. 30–50 produces noticeable region-to-region differences.
    /// </summary>
    public float MoistureNoiseScale { get; set; } = 0f;

    /// <summary>
    /// Noise frequency for moisture anomalies. Should be similar to temperature
    /// frequency so anomaly blobs are roughly the same geographic size.
    /// </summary>
    public float MoistureNoiseFrequency { get; set; } = 0.015f;

    /// <summary>
    /// Fraction of carry that bleeds to adjacent latitude rows at each column step
    /// during the moisture sweep. 0 = pure horizontal sweep (bands strongly).
    /// 0.15–0.30 = angled flow, moisture crosses latitude lines, breaks banding.
    /// Above 0.40 gets unphysical (more N-S flow than horizontal).
    /// </summary>
    public float MoistureAngleBlend { get; set; } = 0f;

    // Continental/maritime differentiation

    /// <summary>
    /// E-folding distance in tiles for maritime influence decay away from ocean/lakes.
    /// At this distance from water, maritime influence is ~37%; at 2× distance, ~14%.
    /// 20–30 tiles works well for a 200-tile-wide world.
    /// </summary>
    public float ContinentalRadiusTiles { get; set; } = 0f;

    /// <summary>
    /// How strongly maritime/continental position modulates temperature around the
    /// latitude baseline. Coasts (high maritime influence) are pulled cooler at tropical
    /// latitudes; interiors (low maritime) are pushed warmer. This breaks horizontal
    /// banding: the same latitude can be temperate forest on the coast and desert inland.
    /// Formula: tempMod = (1 - 2 * maritimeFactor) * amp * (latFrac - 0.5)
    /// 0.10–0.20 produces noticeable differentiation without overriding latitude.
    /// </summary>
    public float ContinentalAmplification { get; set; } = 0f;

    /// <summary>
    /// Moisture carry level that lake tiles recharge the sweep to (0–1).
    /// Lakes act as inland moisture sources; if carry is below this value when the
    /// sweep crosses a lake, it is raised to this level. 0.3–0.5 is realistic.
    /// </summary>
    public float LakeMoistureRecharge { get; set; } = 0f;

    /// <summary>
    /// Flat moisture carry bonus added when the sweep crosses a river tile (0–1).
    /// Stacks additively with existing carry; clamped to 1.0. 0.05–0.12 is subtle.
    /// </summary>
    public float RiverMoistureBonus { get; set; } = 0f;
}
