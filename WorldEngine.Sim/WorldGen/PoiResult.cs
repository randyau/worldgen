namespace WorldEngine.Sim.WorldGen;

/// <summary>POI candidate flags produced by PoiCandidateLayer.</summary>
public sealed class PoiResult
{
    public bool[] IsPOICandidate { get; }

    public PoiResult(int tileCount)
    {
        IsPOICandidate = new bool[tileCount];
    }
}
