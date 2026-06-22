namespace WorldEngine.Sim.Config;

public class WorldGenConfig
{
    public int DefaultTileSizeKm { get; set; } = 10;
    public int DefaultWidthKm { get; set; } = 4000;
    public int DefaultHeightKm { get; set; } = 3000;
    public int ChunkSize { get; set; } = 16;

    public TectonicsConfig Tectonics { get; set; } = new();
    public RiversConfig Rivers { get; set; } = new();
    public BiomeThresholdConfig BiomeThresholds { get; set; } = new();
}
