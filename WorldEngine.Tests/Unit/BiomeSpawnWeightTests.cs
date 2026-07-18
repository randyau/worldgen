using FluentAssertions;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// S3 biome-weighted spawn placement tests: weight lookup and spawn distribution.
/// </summary>
public class BiomeSpawnWeightTests
{
    [Fact]
    public void BiomeSpawnWeight_ReturnsConfiguredValues()
    {
        var cfg = new CharacterSimConfig();
        cfg.BiomeSpawnWeight(BiomeType.Tundra).Should().Be(cfg.SpawnWeightTundra);
        cfg.BiomeSpawnWeight(BiomeType.Grassland).Should().Be(cfg.SpawnWeightGrassland);
        cfg.BiomeSpawnWeight(BiomeType.Desert).Should().Be(cfg.SpawnWeightDesert);
        cfg.BiomeSpawnWeight(BiomeType.Volcanic).Should().Be(cfg.SpawnWeightDefault);
    }

    [Fact]
    public void HarshBiomes_AreHeavilyDownWeighted()
    {
        var cfg = new CharacterSimConfig();
        cfg.SpawnWeightTundra.Should().BeLessThan(cfg.SpawnWeightGrassland * 0.1f,
            "tundra must be at least 10× less likely than grassland");
        cfg.SpawnWeightDesert.Should().BeLessThan(cfg.SpawnWeightPlains * 0.1f);
        cfg.SpawnWeightMountain.Should().BeLessThan(cfg.SpawnWeightTemperateForest);
    }

    /// <summary>
    /// Spawn-distribution test: on a real generated world, the fraction of initial spawns
    /// landing in harsh biomes (tundra/desert/mountain) must be well below the harsh-biome
    /// fraction of the eligible candidate tiles.
    /// </summary>
    [Fact]
    public void InitialSpawns_UnderRepresentHarshBiomes()
    {
        // Medium world: 1000×800 km at 10 km/tile = 100×80 tiles — big enough for
        // latitude-banded biomes (tundra at the poles).
        var worldConfig = new WorldConfig { Seed = 4242, WidthKm = 1000, HeightKm = 800, TileWidthKm = 10 };
        var simConfig   = Helpers.TestSimConfig.Default();
        simConfig.Character.InitialCount = 60;                 // many samples
        simConfig.Character.MinFertilityToSettle = 10;         // widen eligibility so tundra tiles qualify

        var pipeline = new WorldGenPipeline();
        var world = pipeline.RunFullAsync(worldConfig, simConfig).GetAwaiter().GetResult();

        // Candidate-pool biome census (mirror the spawner's eligibility rules)
        static bool IsHarsh(BiomeType b) =>
            b is BiomeType.Tundra or BiomeType.Desert or BiomeType.Mountain;

        int eligible = 0, harshEligible = 0;
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth;  x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c)) continue;
            var t = world.TileGrid.GetTile(c);
            var b = (BiomeType)t.BiomeType;
            if (b == BiomeType.HighMountain) continue;
            if (t.Fertility < simConfig.Character.MinFertilityToSettle) continue;
            eligible++;
            if (IsHarsh(b)) harshEligible++;
        }

        eligible.Should().BeGreaterThan(100, "world must have enough candidate tiles for the test");
        float harshTileFraction = (float)harshEligible / eligible;

        // If this seed produces no harsh candidate tiles, the test is vacuous — regenerate
        // with a different seed rather than silently passing.
        harshTileFraction.Should().BeGreaterThan(0.02f,
            "seed must produce a non-trivial harsh-biome candidate fraction for a meaningful test");

        var pending = CharacterSpawner.SpawnAll(world, simConfig);
        var spawned = world.Entities.Characters;
        spawned.Count.Should().BeGreaterThan(30, "most of the requested spawns should be placed");

        int harshSpawns = spawned.Count(c =>
            IsHarsh((BiomeType)world.TileGrid.GetTile(c.Location).BiomeType));
        float harshSpawnFraction = (float)harshSpawns / spawned.Count;

        harshSpawnFraction.Should().BeLessThan(harshTileFraction * 0.5f,
            $"harsh-biome spawn fraction ({harshSpawnFraction:P1}) must be well below the " +
            $"harsh candidate-tile fraction ({harshTileFraction:P1})");
    }

    [Fact]
    public void InitialSpawns_AreReproducible()
    {
        var worldConfig = new WorldConfig { Seed = 4242, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };

        var sim1 = Helpers.TestSimConfig.Default();
        var w1 = new WorldGenPipeline().RunFullAsync(worldConfig, sim1).GetAwaiter().GetResult();
        CharacterSpawner.SpawnAll(w1, sim1);

        var sim2 = Helpers.TestSimConfig.Default();
        var w2 = new WorldGenPipeline().RunFullAsync(worldConfig, sim2).GetAwaiter().GetResult();
        CharacterSpawner.SpawnAll(w2, sim2);

        var locs1 = w1.Entities.Characters.Select(c => c.Location).ToList();
        var locs2 = w2.Entities.Characters.Select(c => c.Location).ToList();
        locs1.Should().Equal(locs2, "same seed must produce identical spawn placement");
    }
}
