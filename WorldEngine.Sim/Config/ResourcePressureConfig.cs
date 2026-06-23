namespace WorldEngine.Sim.Config;

public sealed class ResourcePressureConfig
{
    public float ShortageThreshold     { get; set; } = 0.6f;  // food ratio below this = shortage
    public float CrisisThreshold       { get; set; } = 0.3f;  // food ratio below this = crisis → flee goals
    public float AcquireGoalIntensity  { get; set; } = 0.7f;
    public float FleeGoalIntensity     { get; set; } = 0.5f;
    public int   StrainEventCooldown   { get; set; } = 8;     // ticks between SettlementStraining events
    public float PopulationCapPerTile  { get; set; } = 100f;  // population count that equals "full demand" for one tile
}
