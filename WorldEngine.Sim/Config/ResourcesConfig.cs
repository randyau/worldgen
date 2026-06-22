namespace WorldEngine.Sim.Config;

public class ResourcesConfig
{
    public float IronDensity          { get; set; } = 0.08f;
    public float CopperDensity        { get; set; } = 0.04f;
    public float TinDensity           { get; set; } = 0.015f;
    public float PreciousMetalDensity { get; set; } = 0.005f;
    public float RareResourceDensity  { get; set; } = 0.003f;

    // Phase 5 — resource dynamics
    public byte FertilityRecoveryPerYear { get; set; } = 1;
    public byte PostFireFertilityBoost { get; set; } = 30;
    public byte DroughtFertilityPenaltyPerSeason { get; set; } = 5;
}
