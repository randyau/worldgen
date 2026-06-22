using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.WorldGen;

/// <summary>Resource deposit registry produced by ResourceLayer.</summary>
public sealed class ResourceResult
{
    /// <summary>
    /// Deposit registry keyed by tile coordinate.
    /// Only tiles with deposits have entries.
    /// Multiple deposits can stack (surface-first ordering).
    /// </summary>
    public Dictionary<TileCoord, List<ResourceDeposit>> Deposits { get; } = new();
}
