namespace WorldEngine.Sim.WorldGen;

/// <summary>Per-tile tectonic plate data produced by TectonicLayer.</summary>
public sealed class TectonicResult
{
    /// <summary>Plate index for each tile (0-based). Length = TileWidth * TileHeight.</summary>
    public byte[] PlateId { get; }

    /// <summary>True if tile is in a volcanic subduction zone.</summary>
    public bool[] IsVolcanic { get; }

    /// <summary>True if tile sits on a plate boundary.</summary>
    public bool[] IsFaultLine { get; }

    /// <summary>Metal deposit potential at continental fault-line tiles (0–1).</summary>
    public float[] DepositPotential { get; }

    /// <summary>True if this tile's plate is continental (false = oceanic).</summary>
    public bool[] IsContinentalTile { get; }

    public TectonicResult(int tileCount)
    {
        PlateId            = new byte[tileCount];
        IsVolcanic         = new bool[tileCount];
        IsFaultLine        = new bool[tileCount];
        DepositPotential   = new float[tileCount];
        IsContinentalTile  = new bool[tileCount];
    }
}
