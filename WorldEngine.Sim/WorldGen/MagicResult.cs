namespace WorldEngine.Sim.WorldGen;

/// <summary>Per-tile magic intensity data produced by MagicLayer.</summary>
public sealed class MagicResult
{
    /// <summary>Magic intensity byte (0–255) for each tile.</summary>
    public byte[] MagicIntensity { get; }

    /// <summary>True if tile is a high-magic POI candidate.</summary>
    public bool[] IsPOICandidate { get; }

    public MagicResult(int tileCount)
    {
        MagicIntensity = new byte[tileCount];
        IsPOICandidate = new bool[tileCount];
    }
}
