namespace WorldEngine.Sim.Config;

public class TectonicsConfig
{
    public int PlateCount { get; set; } = 15;
    public float MinPlateSeparationFraction { get; set; } = 0.12f;
    public float ContinentalPlateFraction { get; set; } = 0.45f;
}
