namespace WorldEngine.Sim.Config;

public class WorldGenConfig
{
    public int DefaultTileSizeKm { get; set; } = 10;
    public int DefaultWidthKm { get; set; } = 4000;
    public int DefaultHeightKm { get; set; } = 3000;
    public int ChunkSize { get; set; } = 16;

    /// <summary>Multiplier on magic intensity peaks (V2 stub).</summary>
    public float MagicIntensityScale { get; set; } = 1.0f;

    public TectonicsConfig Tectonics { get; set; } = new();
    public ElevationConfig Elevation { get; set; } = new();
    public OceanConfig Ocean { get; set; } = new();
    public RiversConfig Rivers { get; set; } = new();
    public BiomeThresholdConfig BiomeThresholds { get; set; } = new();
}
