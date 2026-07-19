using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Assigns mineral and rare resource deposits to tiles based on tectonic and biome context.
/// Writes to ResourceResult.Deposits; HasDeposit/HasRareResource flags applied during assembly.
/// </summary>
public sealed class ResourceLayer : IWorldGenLayer<ResourceResult>
{
    private const int SaltDeposit = LayerSeeds.Resource;

    public ResourceResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        var tec  = ctx.Tectonic!;
        var elev = ctx.Elevation!;
        var biome = ctx.Biome!;
        var ocean = ctx.Ocean!;
        var cfg  = ctx.SimConfig.WorldGen;
        int seed = ctx.Config.Seed ^ SaltDeposit;
        int w = ctx.TileWidth, h = ctx.TileHeight;

        var result = new ResourceResult();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (ocean.IsOcean[idx]) continue;

                var coord = new TileCoord(x, y);

                // --- Continental fault-line tiles: Iron, Copper, Stone deposits ---
                if (tec.IsFaultLine[idx] && tec.IsContinentalTile[idx])
                {
                    float roll = WorldRng.FloatAt(seed, 0, x, y, 1);
                    float potential = tec.DepositPotential[idx];

                    if (roll < cfg.Resources.IronDensity * (1f + potential))
                        AddDeposit(result, coord, "Iron", seed, x, y, 2);
                    else if (roll < cfg.Resources.IronDensity * (1f + potential) + cfg.Resources.CopperDensity)
                        AddDeposit(result, coord, "Copper", seed, x, y, 3);
                    else if (roll < 0.35f)
                        AddDeposit(result, coord, "Stone", seed, x, y, 4);
                }

                // --- Volcanic tiles: Sulfur, sometimes rare Obsidian ---
                if (tec.IsVolcanic[idx])
                {
                    float roll = WorldRng.FloatAt(seed, 0, x, y, 5);
                    if (roll < cfg.Resources.RareResourceDensity * 2f)
                        AddDeposit(result, coord, "Obsidian", seed, x, y, 6);
                    else if (roll < 0.25f)
                        AddDeposit(result, coord, "Sulfur", seed, x, y, 7);
                }

                // --- Hill/Mountain tiles: Stone, sometimes Coal ---
                if (biome.Biomes[idx] is BiomeType.Mountain or BiomeType.Hills)
                {
                    float roll = WorldRng.FloatAt(seed, 0, x, y, 8);
                    if (roll < cfg.Resources.PreciousMetalDensity)
                        AddDeposit(result, coord, "Gold", seed, x, y, 9);
                    else if (roll < 0.15f)
                        AddDeposit(result, coord, "Coal", seed, x, y, 10);
                    else if (roll < 0.3f)
                        AddDeposit(result, coord, "Stone", seed, x, y, 11);
                }

                // --- Surface organic resources: per-tile variation within biome patches ---
                // Herbs — medicinal plants; common in fertile, moist biomes
                {
                    float herbDensity = biome.Biomes[idx] switch
                    {
                        BiomeType.Swamp             => cfg.Resources.HerbDensity * 1.6f,
                        BiomeType.TropicalRainforest => cfg.Resources.HerbDensity * 1.4f,
                        BiomeType.TemperateForest   => cfg.Resources.HerbDensity * 1.2f,
                        BiomeType.BorealForest      => cfg.Resources.HerbDensity * 0.6f,
                        BiomeType.Grassland         => cfg.Resources.HerbDensity * 0.8f,
                        BiomeType.Plains            => cfg.Resources.HerbDensity * 0.7f,
                        BiomeType.Hills             => cfg.Resources.HerbDensity * 0.5f,
                        _                           => 0f,
                    };
                    if (herbDensity > 0f && WorldRng.FloatAt(seed, 3, x, y, 12) < herbDensity)
                        AddDeposit(result, coord, "Herbs", seed, x, y, 13);
                }

                // Wild Game — huntable wildlife; tied to open and forested terrain
                {
                    float gameDensity = biome.Biomes[idx] switch
                    {
                        BiomeType.TemperateForest   => cfg.Resources.WildGameDensity * 1.3f,
                        BiomeType.BorealForest      => cfg.Resources.WildGameDensity * 1.1f,
                        BiomeType.Grassland         => cfg.Resources.WildGameDensity * 1.2f,
                        BiomeType.Savanna           => cfg.Resources.WildGameDensity * 1.0f,
                        BiomeType.Plains            => cfg.Resources.WildGameDensity * 0.9f,
                        BiomeType.Tundra            => cfg.Resources.WildGameDensity * 0.5f,
                        BiomeType.Swamp             => cfg.Resources.WildGameDensity * 0.4f,
                        _                           => 0f,
                    };
                    if (gameDensity > 0f && WorldRng.FloatAt(seed, 3, x, y, 14) < gameDensity)
                        AddDeposit(result, coord, "Wild_Game", seed, x, y, 15);
                }

                // Clay — ceramics and construction; swamps, river edges, hills
                {
                    bool hasRiver = ctx.River is { } r && r.HasRiver[idx];
                    float clayDensity = biome.Biomes[idx] switch
                    {
                        BiomeType.Swamp  => cfg.Resources.ClayDensity * 1.8f,
                        BiomeType.Hills  => cfg.Resources.ClayDensity * 0.8f,
                        BiomeType.Plains => cfg.Resources.ClayDensity * 0.5f,
                        BiomeType.Grassland => cfg.Resources.ClayDensity * 0.4f,
                        _                => 0f,
                    };
                    if (hasRiver) clayDensity += cfg.Resources.ClayDensity * 1.2f;
                    if (clayDensity > 0f && WorldRng.FloatAt(seed, 3, x, y, 16) < clayDensity)
                        AddDeposit(result, coord, "Clay", seed, x, y, 17);
                }

                // Flint — tools and early weapons; arid and rocky terrain
                {
                    float flintDensity = biome.Biomes[idx] switch
                    {
                        BiomeType.Desert   => cfg.Resources.FlintDensity * 1.3f,
                        BiomeType.Hills    => cfg.Resources.FlintDensity * 1.2f,
                        BiomeType.Mountain => cfg.Resources.FlintDensity * 0.9f,
                        BiomeType.Beach    => cfg.Resources.FlintDensity * 0.6f,
                        BiomeType.Tundra   => cfg.Resources.FlintDensity * 0.4f,
                        _                  => 0f,
                    };
                    if (flintDensity > 0f && WorldRng.FloatAt(seed, 3, x, y, 18) < flintDensity)
                        AddDeposit(result, coord, "Flint", seed, x, y, 19);
                }
            }

            if (y % 50 == 0)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report((float)y / h);
            }
        }

        progress?.Report(1.0f);
        return result;
    }

    private static void AddDeposit(
        ResourceResult result, TileCoord coord,
        string type, int seed, int x, int y, int salt)
    {
        byte quality = (byte)(WorldRng.FloatAt(seed, 1, x, y, salt) * 255f);
        byte depth   = (byte)(WorldRng.FloatAt(seed, 2, x, y, salt) * 200f);
        if (!result.Deposits.TryGetValue(coord, out var list))
        {
            list = new List<ResourceDeposit>();
            result.Deposits[coord] = list;
        }
        list.Add(new ResourceDeposit(type, quality, depth));
    }
}
