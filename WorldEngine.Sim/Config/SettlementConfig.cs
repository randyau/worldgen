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
    // ─── Disease ──────────────────────────────────────────────────────────────
    // Annual outbreak probability per uninfected settlement; multiplied by density factor.
    // Lowered from 0.04: disease should concentrate in dense cities, not plague every hamlet.
    public float DiseaseBaseChance       { get; set; } = 0.02f;
    // Density multiplier raised from 2.0 to 3.0 so large cities are still very vulnerable:
    // at pop/cap=1.0: chance = 0.02*(1+3.0) = 8%; at pop/cap=0.3: chance = 0.02*(1+0.9) = 3.8%
    public float DiseaseDensityMult      { get; set; } = 3.0f;
    // Fraction of population lost per year while a settlement is infected.
    // Applied per-tick as MortalityPerYear / TicksPerYear.
    public float DiseaseMortalityPerYear { get; set; } = 0.05f;
    // Outbreaks cannot start below this population — too few people to sustain endemic disease.
    public int   DiseaseMinPop           { get; set; } = 40;
    // Tile radius within which an infected settlement can spread disease annually.
    public int   DiseaseSpreadRadius     { get; set; } = 12;
    // Annual probability of spreading to each nearby settlement.
    public float DiseaseSpreadChance     { get; set; } = 0.20f;
    // Infection auto-clears after this many years regardless of recovery rolls.
    public int   DiseaseMaxDurationYears { get; set; } = 6;
    // Annual probability of spontaneous recovery before max duration.
    public float DiseaseRecoveryChance   { get; set; } = 0.30f;

    // ─── Settlement health recovery ───────────────────────────────────────────
    // Health drains to 0 under sustained raids and then the settlement is destroyed.
    // Between raids, settlements passively repair at this rate per tick.
    // At 1 HP/tick and 16 ticks/year, a fully razed settlement (0 HP) takes ~6 years to
    // recover to 100 if left unraided — which feels right for rebuilding after a war.
    public int HealthRecoveryPerTick { get; set; } = 1;
    // Maximum health a settlement can reach via passive recovery (always 100 absent modifiers).
    public int MaxHealth { get; set; } = 100;

    // ─── Wildlife raids ───────────────────────────────────────────────────────
    // Annual probability of a wildlife attack on any settlement (before biome modifier).
    public float WildlifeAttackBaseChance { get; set; } = 0.04f;
    // Fraction of population killed when an attack lands (at minimum defense).
    public float WildlifeAttackDamage     { get; set; } = 0.08f;
    // Settlements at this population have 80% reduced attack vulnerability.
    public int   WildlifeDefensePopScale  { get; set; } = 150;

    // ─── Emigration (pressure-driven colonization) ────────────────────────────
    // When population exceeds this fraction of carrying capacity, emigration is triggered.
    public float EmigrationThreshold    { get; set; } = 0.75f;
    // Additional annual character-spawn probability when over the threshold (scaled by pressure).
    public float EmigrationBonusChance  { get; set; } = 0.08f;
    // Population deducted from parent settlement each time an emigrant character spawns.
    public int   EmigrantPopCost        { get; set; } = 20;

    public int   CrystalPopArtisan        { get; set; } = 200;
    public int   CrystalPopScholar        { get; set; } = 300;
    public int   CrystalPopPhysician      { get; set; } = 500;
    public int   CrystalPopMerchant       { get; set; } = 1_000;

    // ─── Population milestone events ──────────────────────────────────────────
    // Fraction of current population that must change in a single tick to emit
    // SettlementGrew / SettlementShrank. Captures famines, raids, and founding bursts
    // while avoiding O(settlements) per-tick noise for steady-state growth.
    public float GrowthEventThresholdPct  { get; set; } = 0.20f;  // 20% growth in one tick
    public float ShrinkEventThresholdPct  { get; set; } = 0.15f;  // 15% shrink in one tick
}
