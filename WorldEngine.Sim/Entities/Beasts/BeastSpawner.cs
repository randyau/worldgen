using System.Text.Json;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Beasts;

/// <summary>
/// Populates EntityRegistry with initial beasts and builds the BeastEmergenceSchedule
/// for deferred mythological creature spawns.
/// Called once, after world gen, before the first sim tick.
/// </summary>
public static class BeastSpawner
{
    private const int SaltSpawnTile  = 200;
    private const int SaltEmergYear  = 201;

    // Biome name → BiomeType mapping for catalog lookup
    private static readonly Dictionary<string, BiomeType> BiomeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ocean"]               = BiomeType.Ocean,
            ["coastal_water"]       = BiomeType.CoastalWater,
            ["beach"]               = BiomeType.Beach,
            ["tundra"]              = BiomeType.Tundra,
            ["boreal_forest"]       = BiomeType.BorealForest,
            ["temperate_forest"]    = BiomeType.TemperateForest,
            ["tropical_rainforest"] = BiomeType.TropicalRainforest,
            ["grassland"]           = BiomeType.Grassland,
            ["savanna"]             = BiomeType.Savanna,
            ["desert"]              = BiomeType.Desert,
            ["swamp"]               = BiomeType.Swamp,
            ["mountain"]            = BiomeType.Mountain,
            ["high_mountain"]       = BiomeType.HighMountain,
            ["plains"]              = BiomeType.Plains,
            ["volcanic"]            = BiomeType.Volcanic,
        };

    public static List<PendingEvent> SpawnAll(WorldState world, BeastCatalog catalog)
    {
        var pending = new List<PendingEvent>();
        var cfg = catalog.SpawnConfig;
        int tileCount = world.TileGrid.TileWidth * world.TileGrid.TileHeight;
        int totalBudget = (int)(tileCount / 10000f * cfg.TargetDensityPer10kTiles);
        long entitySeq = 1;

        foreach (var species in catalog.AllSpecies)
        {
            if (species.IsMythological)
            {
                SpawnMythological(world, catalog, species, cfg, ref entitySeq, ref totalBudget, pending);
            }
            else
            {
                SpawnPredator(world, species, ref entitySeq, ref totalBudget, pending);
            }
        }

        return pending;
    }

    private static void SpawnPredator(
        WorldState world,
        BeastSpeciesConfig species,
        ref long entitySeq,
        ref int totalBudget,
        List<PendingEvent> pending)
    {
        if (totalBudget <= 0) return;

        var validTiles = CollectValidTiles(world, species);
        if (validTiles.Count == 0) return;

        // Spawn one pack (or solitary beast) — cap at world budget and max_per_world
        int packSize = species.PackSizeMin
            + (int)(WorldRng.FloatAt(world.WorldSeed, 0, (int)(entitySeq & 0x7FFFFFFF), 0, SaltSpawnTile)
                    * (species.PackSizeMax - species.PackSizeMin + 1));
        packSize = Math.Min(packSize, Math.Min(species.MaxPerWorld, totalBudget));

        // Pick one home tile for the pack
        int tileIdx = (int)(WorldRng.FloatAt(world.WorldSeed, 0, (int)(entitySeq & 0x7FFFFFFF), 1, SaltSpawnTile)
                      * validTiles.Count);
        var home = validTiles[Math.Clamp(tileIdx, 0, validTiles.Count - 1)];

        for (int i = 0; i < packSize; i++, entitySeq++)
        {
            var beast = BeastFactory.Spawn(species, home, world.WorldSeed, entitySeq);
            world.Entities.Add(beast);
            totalBudget--;
            pending.Add(MakeSpawnedEvent(beast, world));
        }
    }

    private static void SpawnMythological(
        WorldState world,
        BeastCatalog catalog,
        BeastSpeciesConfig species,
        BeastSpawnConfig cfg,
        ref long entitySeq,
        ref int totalBudget,
        List<PendingEvent> pending)
    {
        if (totalBudget <= 0) return;

        int startCount = Math.Max(0, (int)MathF.Round(species.MaxPerWorld * cfg.MythStartFraction));
        int deferCount = species.MaxPerWorld - startCount;

        var validTiles = CollectValidTiles(world, species);

        for (int i = 0; i < startCount && totalBudget > 0; i++, entitySeq++)
        {
            if (validTiles.Count == 0) break;
            int tileIdx = (int)(WorldRng.FloatAt(world.WorldSeed, 0, (int)(entitySeq & 0x7FFFFFFF), 0, SaltSpawnTile)
                          * validTiles.Count);
            var tile = validTiles[Math.Clamp(tileIdx, 0, validTiles.Count - 1)];
            var beast = BeastFactory.Spawn(species, tile, world.WorldSeed, entitySeq, forceLegendary: true);
            world.Entities.Add(beast);
            totalBudget--;
            pending.Add(MakeSpawnedEvent(beast, world));
        }

        for (int i = 0; i < deferCount; i++, entitySeq++)
        {
            // Deterministic emergence year within [1, myth_emergence_years]
            int year = 1 + (int)(WorldRng.FloatAt(world.WorldSeed, 0, (int)(entitySeq & 0x7FFFFFFF), 0, SaltEmergYear)
                          * cfg.MythEmergenceYears);
            world.BeastEmergenceSchedule.Add((year, species.Id));
        }
    }

    private static List<TileCoord> CollectValidTiles(WorldState world, BeastSpeciesConfig species)
    {
        var validBiomes = new HashSet<BiomeType>();
        foreach (var b in species.Biomes)
            if (BiomeMap.TryGetValue(b, out var bt)) validBiomes.Add(bt);

        bool anyBiome = species.Biomes.Any(b => b.Equals("any", StringComparison.OrdinalIgnoreCase));

        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var valid = new List<TileCoord>();
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var coord = new TileCoord(x, y);
            var tile = world.TileGrid.GetTile(coord);
            var biome = (BiomeType)tile.BiomeType;
            if (biome == BiomeType.Ocean) continue;
            if (!anyBiome && !validBiomes.Contains(biome)) continue;
            valid.Add(coord);
        }
        return valid;
    }

    private static PendingEvent MakeSpawnedEvent(LegendaryBeast beast, WorldState world)
    {
        var payload = JsonSerializer.Serialize(new
        {
            beastId    = beast.Id.Value,
            name       = beast.Name,
            speciesId  = beast.SpeciesId,
            isLegendary = beast.IsLegendary,
            location   = new[] { beast.Location.X, beast.Location.Y }
        });
        return new PendingEvent(EventType.BeastSpawned, beast.Location, null, payload);
    }
}
