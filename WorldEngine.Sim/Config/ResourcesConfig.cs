namespace WorldEngine.Sim.Config;

public class ResourcesConfig
{
    public float IronDensity          { get; set; } = 0.08f;
    public float CopperDensity        { get; set; } = 0.04f;
    public float TinDensity           { get; set; } = 0.015f;
    public float PreciousMetalDensity { get; set; } = 0.005f;
    public float RareResourceDensity  { get; set; } = 0.003f;

    // Phase 5 — resource dynamics
    public byte FertilityRecoveryPerYear           { get; set; } = 3;   // was 1; faster recovery reduces drought's long tail
    public byte PostFireFertilityBoost             { get; set; } = 30;
    public byte DroughtFertilityPenaltyPerSeason   { get; set; } = 3;   // was 5; penalty:recovery ratio was 5:1, now ~1:1
    // Hard floor: drought cannot reduce tile fertility below this value.
    // Prevents marginal tiles from reaching 0 (which collapses food even after recovery).
    public byte DroughtFertilityFloor              { get; set; } = 5;
}
