using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

public static partial class CivTracker
{
    private const int SaltSettlementPrefix  = 5001;
    private const int SaltSettlementSuffix  = 5002;
    private const int SaltFertilityVariance = 5003;

    private static void FireCivFounded(
        Civilization civ, Tier1Character founder, WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new CivFoundedPayload(
            civ.Id.Value, civ.Name, founder.Id.Value, founder.Identity.Name));
        pending.Add(new PendingEvent(EventType.CivilizationFounded, civ.CapitalTile, null, payload,
            new[] { founder.Id.Value },
            ActorId: founder.Id.Value, ActorName: founder.Identity.Name, CivId: civ.Id.Value));
    }

    private static void FireSettlementFounded(
        SettlementStub stub, Tier1Character founder, WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new SettlementFoundedPayload(
            founder.Id.Value, founder.Identity.Name,
            stub.CivId.Value, world.Civilizations.TryGetValue(stub.CivId, out var c) ? c.Name : "",
            50)); // SettlementStartPop
        pending.Add(new PendingEvent(EventType.SettlementFounded, stub.Tile, null, payload,
            new[] { founder.Id.Value },
            ActorId: founder.Id.Value, ActorName: founder.Identity.Name,
            CivId: stub.CivId.Value, SettlementName: stub.Name));
    }

    private static string GenerateSettlementName(
        TileCoord tile, WorldState world, SettlementNamesConfig cfg)
    {
        if (cfg.Prefixes.Length == 0 || cfg.Suffixes.Length == 0)
            return $"Settlement ({tile.X},{tile.Y})";

        float pf = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltSettlementPrefix);
        float sf = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltSettlementSuffix);
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;

        int pi = BiasedIndex(pf, biome, cfg.Prefixes.Length);
        int si = (int)(sf * cfg.Suffixes.Length);
        return cfg.Prefixes[pi] + cfg.Suffixes[si];
    }

    // Deterministic founding-time fertility variance: maps [0,1] → [1-variance, 1+variance]
    private static float GenerateFertilityMultiplier(TileCoord tile, WorldState world)
    {
        float r = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltFertilityVariance);
        // DECISION: variance range is hardcoded here; SettlementConfig.FertilityVariance is the
        // intended range, but injecting SimConfig into CivTracker adds coupling we avoid for now.
        const float variance = 0.15f;
        return 1f - variance + r * (variance * 2f);
    }

    // Slightly bias prefix selection so rocky biomes lean toward hard-sounding names — cosmetic only.
    private static int BiasedIndex(float raw, BiomeType biome, int count)
    {
        float shift = biome switch
        {
            BiomeType.Mountain or BiomeType.Hills or BiomeType.Volcanic => 0.3f,
            BiomeType.Grassland or BiomeType.Savanna or BiomeType.TemperateForest => -0.15f,
            BiomeType.Tundra or BiomeType.BorealForest => 0.15f,
            _ => 0f
        };
        return (int)(((raw + shift + 1f) % 1f) * count);
    }
}
