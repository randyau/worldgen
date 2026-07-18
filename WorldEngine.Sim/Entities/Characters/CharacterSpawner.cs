using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Populates the world with initial Tier 1 characters at world start.
/// Characters are placed on fertile land tiles, one per tile.
/// </summary>
public static class CharacterSpawner
{
    private const int SaltCharTile = 500;

    public static List<PendingEvent> SpawnAll(WorldState world, SimConfig config)
    {
        var pending = new List<PendingEvent>();
        int count = config.Character.InitialCount;
        int minFertility = config.Character.MinFertilityToSettle;

        // Collect candidate tiles: land, adequate fertility, no existing character
        var candidates = CollectCandidateTiles(world, minFertility);
        if (candidates.Count == 0) return pending;

        // Shuffle deterministically using worldSeed
        ShuffleByKey(candidates, world.WorldSeed);

        long entitySeq = 10_000; // start well above beast range
        int placed = 0;
        // Two passes: pass 0 applies biome-weighted acceptance (S3 — harsh biomes are
        // rejected proportionally to their spawn weight); pass 1 relaxes the filter so
        // the full count is always placed even on weight-starved maps.
        for (int pass = 0; pass < 2 && placed < count; pass++)
        foreach (var tile in candidates)
        {
            if (placed >= count) break;

            var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;

            if (pass == 0)
            {
                // Weighted acceptance: roll deterministic RNG against the biome weight
                // (normalized to the max configured weight so top biomes always pass).
                float weight = config.Character.BiomeSpawnWeight(biome);
                float accept = weight / MaxSpawnWeight(config.Character);
                float roll = WorldRng.FloatAt(world.WorldSeed, 1, tile.X, tile.Y, SaltCharTile);
                if (roll >= accept) continue;
            }
            else if (world.GetEntitiesAt(tile).Any())
            {
                continue; // pass 1: skip tiles that already received a spawn
            }
            var character = CharacterFactory.Spawn(
                location:  tile,
                biome:     biome,
                worldSeed: world.WorldSeed,
                entitySeq: entitySeq,
                config:    config,
                birthYear: world.CurrentYear);

            int nameOrdinal = world.ClaimNameOrdinal(character.Identity.Name);
            if (nameOrdinal > 0)
                character.Identity = character.Identity with { NameOrdinal = nameOrdinal };

            world.Entities.Add(character);
            pending.Add(MakeBornEvent(character, world));

            entitySeq++;
            placed++;
        }

        return pending;
    }

    /// <summary>Largest configured biome spawn weight — used to normalize acceptance rolls.</summary>
    private static float MaxSpawnWeight(Config.CharacterSimConfig cfg)
    {
        float max = cfg.SpawnWeightDefault;
        foreach (BiomeType b in Enum.GetValues<BiomeType>())
            max = Math.Max(max, cfg.BiomeSpawnWeight(b));
        return Math.Max(max, 1e-6f);
    }

    private static List<TileCoord> CollectCandidateTiles(WorldState world, int minFertility)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var candidates = new List<TileCoord>();
        for (int y = 1; y < h - 1; y++)
        for (int x = 0; x < w; x++)
        {
            var coord = new TileCoord(x, y);
            if (!world.IsLand(coord)) continue;
            var tile = world.TileGrid.GetTile(coord);
            if ((BiomeType)tile.BiomeType == BiomeType.HighMountain) continue;
            if (tile.Fertility < minFertility) continue;
            candidates.Add(coord);
        }
        return candidates;
    }

    private static void ShuffleByKey(List<TileCoord> list, int seed)
    {
        // Fisher-Yates with WorldRng-derived keys for determinism
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(WorldRng.FloatAt(seed, 0, i, 0, SaltCharTile) * (i + 1));
            j = Math.Clamp(j, 0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static PendingEvent MakeBornEvent(Tier1Character c, WorldState world)
    {
        var payload = JsonSerializer.Serialize(new CharacterBornPayload(
            c.Id.Value, c.Identity.Name, c.Identity.Epithet,
            c.Personality.Ambition, c.Personality.Aggression));
        return new PendingEvent(EventType.CharacterBorn, c.Location, null, payload,
            new[] { c.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name);
    }
}
