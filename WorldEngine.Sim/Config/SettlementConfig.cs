namespace WorldEngine.Sim.Config;

public sealed class SettlementConfig
{
    public float PopGrowthRate            { get; set; } = 0.5f;
    public float PopDecayRate             { get; set; } = 0.05f;
    // Decay multiplier applied per unit of food deficit (foodRatio < 1.0)
    // At full shortage (ratio=0.6) this adds 0.4 × StarvationDecayRate to per-tick decay
    public float StarvationDecayRate      { get; set; } = 0.3f;
    // Decay multiplier applied per unit of food crisis (ratio < CrisisThreshold)
    public float FamineDecayRate          { get; set; } = 0.8f;
    public int   PopMinViable             { get; set; } = 5;
    public int   PopMax                   { get; set; } = 50_000;
    // Per-settlement variance drawn at founding: effective fertility = fertility × [1 ± FertilityVariance]
    public float FertilityVariance        { get; set; } = 0.15f;
    // Effective fertility multiplier applied to tiles already in a same-civ settlement's hinterland
    public float HinterlandDrainFactor    { get; set; } = 0.15f;
    // Biome carrying capacity: max population supported per reach tile, by biome.
    // Sums across all land tiles in reach radius to give a soft population ceiling.
    public int CarryCapGrassland          { get; set; } = 200;
    public int CarryCapPlains             { get; set; } = 160;
    public int CarryCapTropicalRainforest { get; set; } = 180;
    public int CarryCapSavanna            { get; set; } = 80;
    public int CarryCapTemperateForest    { get; set; } = 120;
    public int CarryCapBorealForest       { get; set; } = 50;
    public int CarryCapSwamp              { get; set; } = 60;
    public int CarryCapBeach              { get; set; } = 40;
    public int CarryCapMountain           { get; set; } = 30;
    public int CarryCapHighMountain       { get; set; } = 10;
    public int CarryCapDesert             { get; set; } = 15;
    public int CarryCapVolcanic           { get; set; } = 50;
    public int CarryCapDefault            { get; set; } = 40;
    // Minimum carrying capacity regardless of biome tiles (prevents instant abandonment on poor land)
    public int CarryCapMinimum            { get; set; } = 50;
    public int   CrystalPopArtisan        { get; set; } = 200;
    public int   CrystalPopScholar        { get; set; } = 300;
    public int   CrystalPopPhysician      { get; set; } = 500;
    public int   CrystalPopMerchant       { get; set; } = 1_000;
}
