using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// A point where a river's flow passes from one world tile into an adjacent one, expressed in
/// the shared coordinate system of the edge between them (so both tiles' border manifests can be
/// filled from the same crossing without re-deriving or mirroring it).
/// </summary>
/// <param name="FromTile">Upstream tile the flow leaves.</param>
/// <param name="ToTile">Downstream tile the flow enters.</param>
/// <param name="Edge">Edge of <paramref name="FromTile"/> the flow crosses (opposite edge on <paramref name="ToTile"/>).</param>
/// <param name="Position">0..1 position along the edge.</param>
/// <param name="Width">0..1 fraction of the edge's length the river occupies.</param>
/// <param name="FlowVolume">Flow accumulation at the source tile.</param>
public readonly record struct RiverCrossing(
    TileCoord FromTile,
    TileCoord ToTile,
    EdgeDirection Edge,
    float Position,
    float Width,
    int FlowVolume);
