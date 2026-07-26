namespace WorldEngine.Sim.Config;

/// <summary>World generation parameters: tile size, world dimensions, chunk size, and per-subsystem generation configs.</summary>
public class WorldGenConfig
{
    public int DefaultTileSizeKm { get; set; } = 10;
    public int DefaultWidthKm { get; set; } = 4000;
    public int DefaultHeightKm { get; set; } = 3000;
    public int ChunkSize { get; set; } = 16;

    /// <summary>Multiplier on magic intensity peaks (V2 stub).</summary>
    public float MagicIntensityScale { get; set; } = 1.0f;

    /// <summary>
    /// Half-amplitude of the per-tile high-frequency noise applied to fertility during world gen.
    /// Creates organic variation within biome patches so characters don't all pick the same tile.
    /// </summary>
    public int FertilityMicroVariance { get; set; } = 20;

    /// <summary>FastNoiseLite frequency for the fertility micro-variation noise.</summary>
    public float FertilityMicroFrequency { get; set; } = 0.07f;
    /// <summary>FastNoiseLite fractal octave count for the fertility micro-variation noise.</summary>
    public int   FertilityMicroOctaves   { get; set; } = 3;

    public TectonicsConfig Tectonics { get; set; } = new();
    public ElevationConfig Elevation { get; set; } = new();
    public OceanConfig Ocean { get; set; } = new();
    public RiversConfig Rivers { get; set; } = new();
    public ResourcesConfig Resources { get; set; } = new();
    public BiomeThresholdConfig BiomeThresholds { get; set; } = new();
}
