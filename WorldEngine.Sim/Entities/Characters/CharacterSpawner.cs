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
        foreach (var tile in candidates)
        {
            if (placed >= count) break;

            var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
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
