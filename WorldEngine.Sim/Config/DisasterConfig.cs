namespace WorldEngine.Sim.Config;

public class DisasterConfig
{
    public float WildfireIgnitionProbabilityPerTick { get; set; } = 0.0001f;
    public float WildfireSpreadProbabilityPerTick { get; set; } = 0.15f;
    public int WildfireMaxTicks { get; set; } = 12;
    public float FloodIgnitionProbabilityPerTick { get; set; } = 0.00005f;
}
