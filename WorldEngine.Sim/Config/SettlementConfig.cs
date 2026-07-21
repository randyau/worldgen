namespace WorldEngine.Sim.Config;

/// <summary>Population growth rates, carrying capacity, and crystallisation threshold constants.</summary>
public sealed class SettlementConfig
{
    // Starting population for every newly founded settlement (capital or colony).
    // Represents the founding tribe/clan migrating with the leader.
    // At 100 km²/tile, 500 people on 13 starting tiles = ~0.4/km² — pre-agricultural subsistence level,
    // which puts tundra/desert in food shortage immediately (intended: marginal biomes are risky) while
    // good farmland has plenty of room to grow.
    public int   SettlementStartPop       { get; set; } = 500;
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
    // Minimum carrying capacity regardless of territory conditions (prevents instant abandonment on
    // newly-founded settlements with tiny territory).
    // DECISION: Only the minimum floor is kept here; biome differentiation lives in the food model
    // (BiomeFoodMultiplier × PeoplePerTilePeak). See D1 in docs/tuning_balance_review_2026-07-18.md.
    public int CarryCapMinimum            { get; set; } = 100;
    // EMA smoothing alpha for the food-derived carrying capacity (0 = perfectly smooth / frozen,
    // 1 = no smoothing / raw each tick). Lower values damp population-territory feedback oscillation
    // at the cost of slower response to real land-use changes (clearing forest, drought, conquest).
    // Calibrated: alpha = 0.05 → ~20-tick half-life (≈1.25 years at 16 ticks/year).
    public float CapacitySmoothingAlpha   { get; set; } = 0.05f;
    // ─── Disease ──────────────────────────────────────────────────────────────
    // DECISION (D4): Outbreak probability is now structural — three multiplicative factors:
    //   outbreakChance = base × (1 + density × DensityMult) × contactFactor × famineFactor
    //   kept in [settlement] because they extend existing disease knobs; no new TOML section.
    //
    // Annual base outbreak probability per uninfected settlement.
    public float DiseaseBaseChance       { get; set; } = 0.02f;
    // Density factor: multiplied by (1 + density × DensityMult) where density = Pop/CarryingCapacity.
    // At pop/cap=1.0: factor = 1 + 3.0 = 4×; at pop/cap=0.3: factor = 1.9×.
    public float DiseaseDensityMult      { get; set; } = 3.0f;
    // Contact factor: multiplier applied when the settlement's civ has active emissary contact
    // (any KnownCivs entry with BestSource >= EmissaryExchange) OR is at war. Models disease
    // spreading along trade routes and through military campaigns.
    public float DiseaseContactMult      { get; set; } = 1.5f;
    // Famine factor: multiplier applied when FoodPressureRatio < DiseaseFamineThreshold.
    // Malnourished populations have suppressed immune response — double outbreak risk.
    public float DiseaseFamineMult       { get; set; } = 2.0f;
    // FoodPressureRatio floor below which the famine factor fires.
    public float DiseaseFamineThreshold  { get; set; } = 0.7f;
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

    // ─── Ruin recording & decay ───────────────────────────────────────────────
    // Only settlements above this population leave a persistent ruin record.
    public int   RuinMinPopThreshold        { get; set; } = 75;
    // Ruins older than this many years become eligible for weathering.
    public int   RuinDecayStartYears        { get; set; } = 300;
    // Annual probability of removing a ruin once it's past RuinDecayStartYears.
    public float RuinDecayChancePerYear     { get; set; } = 0.02f;

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
