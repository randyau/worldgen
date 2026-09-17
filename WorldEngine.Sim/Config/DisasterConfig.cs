namespace WorldEngine.Sim.Config;

/// <summary>Disaster probability and damage constants (wildfire, flood, eruption, earthquake, drought).</summary>
public class DisasterConfig
{
    public float WildfireIgnitionProbabilityPerTick { get; set; } = 0.0003f;
    public float WildfireSpreadProbabilityPerTick { get; set; } = 0.20f;
    public int WildfireMaxTicks { get; set; } = 16;
    public float FloodIgnitionProbabilityPerTick { get; set; } = 0.0002f;

    public float VolcanicEruptionProbabilityPerTick { get; set; } = 0.0002f;
    public float VolcanicAshIntensity { get; set; } = 1.0f;
    public float VolcanicActivityBoost { get; set; } = 0.5f;
    public float VolcanicActivityMultiplierCap { get; set; } = 10.0f;
    public float EarthquakeProbabilityPerTick { get; set; } = 0.0005f;
    public float EarthquakeIntensity { get; set; } = 0.8f;
    public int EarthquakeDecayTicks { get; set; } = 8;
    public float WildfireIgnitionDryMultiplier   { get; set; } = 3.0f;
    public byte  WildfireDryMoistureThreshold    { get; set; } = 60;
    public byte  WildfireHotTemperatureThreshold { get; set; } = 170; // effective temp above which heat boosts ignition
    public float WildfireIgnitionHotMultiplier   { get; set; } = 2.0f; // stacks multiplicatively with dry multiplier
    public float WildfireIntensity { get; set; } = 1.0f;
    public byte FloodWetMoistureThreshold { get; set; } = 200;
    public float FloodWetMultiplier { get; set; } = 2.0f;
    public int FloodSpreadRadius { get; set; } = 1;
    public float FloodOriginIntensity { get; set; } = 0.7f;
    public float FloodSpreadIntensity { get; set; } = 0.5f;
    public int FloodOriginTicks { get; set; } = 6;
    public int FloodSpreadTicks { get; set; } = 4;
    public float DroughtProbabilityPerYear { get; set; } = 0.05f;
    public float DroughtDroughtMultiplier { get; set; } = 2.0f;
    public float DroughtPrecipitationThreshold { get; set; } = 0.7f;
    public int DroughtMinSeasons { get; set; } = 2;
    public int DroughtMaxSeasons { get; set; } = 8;

    // M16 16.0 — settlement/improvement consequences, applied once when a disaster newly
    // occupies a tile (ignition/spread-onset, not per burning tick).
    public int WildfireSettlementDamage        { get; set; } = 8;
    public int FloodSettlementDamage           { get; set; } = 6;
    public int EarthquakeSettlementDamage      { get; set; } = 15;
    public int VolcanicEruptionSettlementDamage { get; set; } = 35;
    public float ImprovementDestructionChance  { get; set; } = 0.15f;
    public byte  VolcanicAshFertilityPenalty   { get; set; } = 60;
    public byte  VolcanicAshFertilityFloor     { get; set; } = 20;
    public int  VolcanicAshDurationTicks       { get; set; } = 64; // ~4 years at 16 ticks/year

    // M16 16.1 — Blight (crop disease). Settlement-scoped: rolled per settlement per year,
    // reuses ActiveTileDisasters keyed by the settlement's own tile rather than a new state track.
    public float BlightProbabilityPerYear            { get; set; } = 0.01f;
    public float BlightIntensity                     { get; set; } = 0.6f;
    public int   BlightDurationTicks                 { get; set; } = 8; // half a year
    public float BlightFoodStoreDestructionFraction  { get; set; } = 0.4f;
    public byte  BlightFertilityPenalty              { get; set; } = 50;
    public byte  BlightFertilityFloor                { get; set; } = 30;

    // M16 16.1b — Harsh winter. Global (not tile-or-settlement-scoped): rolled once per year,
    // active for a bounded number of ticks across the whole world.
    public float HarshWinterProbabilityPerYear    { get; set; } = 0.03f;
    public int   HarshWinterDurationTicks         { get; set; } = 4; // one season
    public float HarshWinterSettlementDecayBonus  { get; set; } = 0.15f; // added to settlement decayF
    public int   HarshWinterCharacterHealthDrain  { get; set; } = 4;
}
