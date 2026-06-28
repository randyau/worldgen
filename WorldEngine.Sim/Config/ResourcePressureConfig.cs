namespace WorldEngine.Sim.Config;

public sealed class ResourcePressureConfig
{
    public float ShortageThreshold     { get; set; } = 0.6f;  // food ratio below this = shortage
    public float CrisisThreshold       { get; set; } = 0.3f;  // food ratio below this = crisis → flee goals
    public float AcquireGoalIntensity  { get; set; } = 0.7f;
    public float FleeGoalIntensity     { get; set; } = 0.5f;
    public int   StrainEventCooldown   { get; set; } = 8;     // ticks between SettlementStraining events
    public float PopulationCapPerTile  { get; set; } = 100f;  // population count that equals "full demand" for one tile
    // Proportional moisture floor: 25% of BaseMoisture always available regardless of season.
    public float FoodMoistureFloor         { get; set; } = 0.25f;
    // Absolute moisture floor applied after the proportional one. Ensures tiles with very low
    // BaseMoisture (desert fringe, etc.) still produce a small amount of food from groundwater/rivers,
    // preventing permanent food=0 on marginal-but-habitable land during drought.
    public float FoodMoistureAbsoluteFloor { get; set; } = 0.20f;

    // ─── Temperature → food production ───────────────────────────────────────
    // Growing-season factor: multiplies food contribution per hinterland tile.
    // Below frost: cold-hardy floor (herding/fishing/cold crops). Then ramp 0→1 to optimal low,
    // flat 1.0 through optimal high, then ramp down to HeatStressFactor at 255.
    public byte  FrostTemperatureThreshold   { get; set; } = 45;   // below this → cold-hardy floor applies
    // Small food fraction available in permanently-frozen tiles (tundra, arctic).
    // Represents herding, fishing, cold-adapted crops — marginal but non-zero.
    public float ColdHardyFoodFloor          { get; set; } = 0.12f;
    public byte  OptimalTemperatureLow       { get; set; } = 100;  // ramp cold-floor→1 between frost and this
    public byte  OptimalTemperatureHigh      { get; set; } = 200;  // ramp 1→heat_floor between this and 255
    public float HeatStressFactor            { get; set; } = 0.7f; // multiplier at extreme heat (255)

    // ─── Biome farming bonus ─────────────────────────────────────────────────
    // Global scale applied to all per-biome food multipliers. 1.0 = full effect.
    // Increase to make biome differences matter more; 0 = all biomes produce equally.
    public float BiomeFoodBonusScale            { get; set; } = 1.0f;

    // ─── Resource stores ─────────────────────────────────────────────────────
    // Fraction of per-tick surplus that goes into stores (rest consumed immediately).
    public float StoreAccumulateRate            { get; set; } = 0.6f;

    // Vital resources (food/water): max store depth in "seasons of supply" scaled with population.
    public float StoreMaxSeasonsPerKPop         { get; set; } = 4.0f;
    public float StoreMinSeasons                { get; set; } = 2.0f;

    // Per-resource spoilage rates (fraction lost per tick regardless of draws).
    public float FoodSpoilageRate               { get; set; } = 0.002f;  // ~500 ticks to fully spoil
    public float WaterSpoilageRate              { get; set; } = 0.010f;  // cisterns evaporate faster
    public float WealthSpoilageRate             { get; set; } = 0.0001f; // gold/gems essentially permanent
    public float StockpileSpoilageRate          { get; set; } = 0.0005f; // iron/timber decay slowly

    // Wealth resources accumulate at a fraction of the raw ledger supply per tick.
    // For non-vital resources (minerals, gold, timber), the ledger value is absolute supply;
    // this rate controls how fast it banks into persistent stores.
    public float WealthAccumulateRate           { get; set; } = 0.2f;

    // Fraction of ALL stores destroyed per point of raid damage (granaries/vaults burn).
    public float StoreRaidDestructionPerDamage  { get; set; } = 0.008f;
}
