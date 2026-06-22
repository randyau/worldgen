using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Accumulates layer results as world generation progresses.
/// Layers read only from completed predecessors — never from layers that haven't run yet.
/// </summary>
public sealed class WorldGenContext
{
    public WorldConfig Config { get; }
    public SimConfig SimConfig { get; }

    public int TileWidth  => Config.TileWidth;
    public int TileHeight => Config.TileHeight;
    public int TileCount  => Config.TileWidth * Config.TileHeight;

    /// <summary>Flat index for tile at (x, y). X is cylinder-wrapped by callers.</summary>
    public int IndexOf(int x, int y) => x + y * Config.TileWidth;
    public int IndexOf(TileCoord c)  => c.X + c.Y * Config.TileWidth;

    // Results accumulated as layers complete — null until that layer has run
    public TectonicResult? Tectonic { get; set; }
    public ElevationResult? Elevation { get; set; }
    public OceanResult? Ocean { get; set; }
    public RiverResult? River { get; set; }
    public ClimateResult? Climate { get; set; }
    public BiomeResult? Biome { get; set; }
    public MagicResult? Magic { get; set; }
    public ResourceResult? Resource { get; set; }
    public PoiResult? Poi { get; set; }

    public WorldGenContext(WorldConfig config, SimConfig simConfig)
    {
        Config    = config;
        SimConfig = simConfig;
    }
}
