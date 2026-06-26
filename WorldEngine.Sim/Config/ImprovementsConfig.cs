namespace WorldEngine.Sim.Config;

public sealed class ImprovementsConfig
{
    public float FarmFoodMultiplier     { get; set; } = 2.0f;
    public float MineYieldMultiplier    { get; set; } = 3.0f;
    public float LoggingYieldMultiplier { get; set; } = 2.5f;
    public float PastureMultiplier      { get; set; } = 1.5f;
    public float FisheryMultiplier      { get; set; } = 2.0f;
    /// <summary>Ticks a character must remain on a tile to complete an improvement (= 8; half a year).</summary>
    public int   ImprovementBuildTicks  { get; set; } = 8;
}
