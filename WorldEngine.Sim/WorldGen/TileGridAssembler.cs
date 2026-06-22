using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Assembles all layer results into a fully populated WorldState.
/// Runs in parallel over Y rows. Also writes manifests.bin sidecar.
/// </summary>
public static class TileGridAssembler
{
    public static WorldState Assemble(WorldGenContext ctx)
    {
        // DECISION: Stub — real implementation in story 1.3.8
        var tileGrid = new TileGrid(ctx.TileWidth, ctx.TileHeight);
        var profiles = new SeasonalProfile[ctx.TileCount];
        var registry = new Dictionary<TileCoord, List<ResourceDeposit>>();

        float stormLat = ctx.SimConfig.Climate.StormCorridorNormalizedLat;

        return new WorldState(ctx.Config, ctx.SimConfig, tileGrid, profiles, registry, stormLat);
    }
}
