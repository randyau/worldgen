namespace WorldEngine.Sim.Config;

public class DisasterConfig
{
    public float WildfireIgnitionProbabilityPerTick { get; set; } = 0.0003f;
    public float WildfireSpreadProbabilityPerTick { get; set; } = 0.20f;
    public int WildfireMaxTicks { get; set; } = 16;
    public float FloodIgnitionProbabilityPerTick { get; set; } = 0.0002f;

    public float VolcanicEruptionProbabilityPerTick { get; set; } = 0.0002f;
    public float EarthquakeProbabilityPerTick { get; set; } = 0.0005f;
    public float WildfireIgnitionDryMultiplier { get; set; } = 3.0f;
    public byte WildfireDryMoistureThreshold { get; set; } = 60;
    public byte FloodWetMoistureThreshold { get; set; } = 200;
    public int FloodSpreadRadius { get; set; } = 1;
    public int EarthquakeDecayTicks { get; set; } = 8;
    public float DroughtProbabilityPerYear { get; set; } = 0.05f;
    public float DroughtDroughtMultiplier { get; set; } = 2.0f;
    public float DroughtPrecipitationThreshold { get; set; } = 0.7f;
    public int DroughtMinSeasons { get; set; } = 2;
    public int DroughtMaxSeasons { get; set; } = 8;
    public float VolcanicActivityBoost { get; set; } = 0.5f;
}
