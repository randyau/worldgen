namespace WorldEngine.Sim.WorldGen;

/// <summary>Per-tile elevation data (0–255) produced by ElevationLayer.</summary>
public sealed class ElevationResult
{
    /// <summary>Elevation byte for each tile. Length = TileWidth * TileHeight.</summary>
    public byte[] Elevation { get; }

    public ElevationResult(int tileCount)
    {
        Elevation = new byte[tileCount];
    }
}
