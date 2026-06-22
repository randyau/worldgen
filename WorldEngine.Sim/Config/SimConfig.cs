namespace WorldEngine.Sim.Config;

public class SimConfig
{
    public WorldGenConfig WorldGen { get; set; } = new();
    public DisasterConfig Disasters { get; set; } = new();
    public EventsConfig Events { get; set; } = new();
    public ClimateConfig Climate { get; set; } = new();
    public SimLoopConfig SimLoop { get; set; } = new();

    public static SimConfig Default() => new();
}
