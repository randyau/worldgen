namespace WorldEngine.Sim.Config;

public class EventsConfig
{
    public string MinimumTierToRecord { get; set; } = "Background";
    public string MinimumPopulationImpact { get; set; } = "None";
    public int CacheSize { get; set; } = 400;
    public int RetentionYears { get; set; } = 500;
    public float HeadlineThreshold { get; set; } = 0.55f;
}
