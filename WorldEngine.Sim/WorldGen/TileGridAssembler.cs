using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Assembles all layer results into a fully populated WorldState.
/// Runs Parallel.For over Y rows for throughput on large worlds.
/// </summary>
public static class TileGridAssembler
{
    public static WorldState Assemble(WorldGenContext ctx)
    {
        var tec     = ctx.Tectonic!;
        var elev    = ctx.Elevation!;
        var ocean   = ctx.Ocean!;
        var climate = ctx.Climate!;
        var biome   = ctx.Biome!;
        var magic   = ctx.Magic!;
        var poi     = ctx.Poi!;

        int w = ctx.TileWidth, h = ctx.TileHeight;
        var grid = new TileGrid(w, h);

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = ctx.IndexOf(x, y);
                var coord = new TileCoord(x, y);

                // Compose static flags from layer results
                TileStaticFlags flags = TileStaticFlags.None;
                if (tec.IsVolcanic[i])          flags |= TileStaticFlags.IsVolcanic;
                if (tec.IsFaultLine[i])         flags |= TileStaticFlags.IsFaultLine;
                if (ocean.IsCoastal[i])         flags |= TileStaticFlags.IsCoastal;
                if (climate.IsStormCorridor[i]) flags |= TileStaticFlags.IsStormCorridor;
                if (poi.IsPOICandidate[i])      flags |= TileStaticFlags.IsPOICandidate;

                if (ctx.River is { } river)
                {
                    if (river.HasRiver[i]) flags |= TileStaticFlags.HasRiver;
                    if (river.IsLake[i])   flags |= TileStaticFlags.IsLake;
                }

                if (ctx.Resource is { } resource)
                {
                    if (resource.Deposits.ContainsKey(coord))
                    {
                        flags |= TileStaticFlags.HasDeposit;
                        // Rare resources: Obsidian and Gold flagged in ResourceLayer
                        if (resource.Deposits[coord].Any(d => d.DepositType is "Obsidian" or "Gold"))
                            flags |= TileStaticFlags.HasRareResource;
                    }
                }

                var tile = new TileData
                {
                    Elevation       = elev.Elevation[i],
                    Fertility       = biome.Fertility[i],
                    BaseTemperature = climate.BaseTemperature[i],
                    BaseMoisture    = climate.BaseMoisture[i],
                    MagicIntensity  = magic.MagicIntensity[i],
                    BiomeType       = (byte)biome.Biomes[i],
                    PlateId         = (byte)(tec.PlateId[i] & 0xFF),
                    StaticFlags     = flags,
                    CurrentMoisture = climate.BaseMoisture[i],
                    DynFlags        = TileDynFlags.None,
                    RoadLevel       = 0,
                    CivControl      = 0,
                };

                grid.SetTile(coord, tile);
            }
        });

        // Build SeasonalProfiles array from climate results
        var profiles = new SeasonalProfile[ctx.TileCount];
        for (int i = 0; i < ctx.TileCount; i++)
            profiles[i] = climate.SeasonalProfiles[i];

        // Carry resource registry from ResourceLayer if available
        var registry = ctx.Resource?.Deposits
            ?? new Dictionary<TileCoord, List<ResourceDeposit>>();

        float stormLat = ctx.SimConfig.Climate.StormCorridorNormalizedLat;

        return new WorldState(ctx.Config, ctx.SimConfig, grid, profiles, registry, stormLat);
    }
}
