namespace WorldEngine.Sim.Config;

/// <summary>Per-resource deposit density fractions used during world generation (iron, copper, tin, precious metals).</summary>
public class ResourcesConfig
{
    public float IronDensity          { get; set; } = 0.08f;
    public float CopperDensity        { get; set; } = 0.04f;
    public float TinDensity           { get; set; } = 0.015f;
    public float PreciousMetalDensity { get; set; } = 0.005f;
    public float RareResourceDensity  { get; set; } = 0.003f;

    // Absolute roll cutoffs (not summed with density above — see ResourceLayer). Expressed as
    // absolute thresholds rather than relative to the preceding density sum, so tuning
    // Iron/CopperDensity upward can push past these and silently starve the Stone/Coal branch —
    // keep StoneOnFaultThreshold > IronDensity + CopperDensity and similarly for the others.
    public float StoneOnFaultThreshold      { get; set; } = 0.35f;
    public float VolcanicSulfurThreshold    { get; set; } = 0.25f;
    public float HillCoalThreshold          { get; set; } = 0.15f;
    public float HillStoneThreshold         { get; set; } = 0.3f;

    // Surface organic resources — create per-tile variation within biome patches
    public float HerbDensity          { get; set; } = 0.35f;
    public float WildGameDensity      { get; set; } = 0.30f;
    public float ClayDensity          { get; set; } = 0.25f;
    public float FlintDensity         { get; set; } = 0.20f;

    // Phase 5 — resource dynamics
    public byte FertilityRecoveryPerYear           { get; set; } = 3;   // was 1; faster recovery reduces drought's long tail
    public byte PostFireFertilityBoost             { get; set; } = 30;
    public byte DroughtFertilityPenaltyPerSeason   { get; set; } = 3;   // was 5; penalty:recovery ratio was 5:1, now ~1:1
    // Hard floor: drought cannot reduce tile fertility below this value.
    // Prevents marginal tiles from reaching 0 (which collapses food even after recovery).
    public byte DroughtFertilityFloor              { get; set; } = 5;
}
