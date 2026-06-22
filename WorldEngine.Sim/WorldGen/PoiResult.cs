using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.WorldGen;

/// <summary>POI candidate set produced by PoiCandidateLayer.</summary>
public sealed class PoiResult
{
    /// <summary>Tiles flagged as high-interest confluence points.</summary>
    public HashSet<TileCoord> Candidates { get; } = new();
}
