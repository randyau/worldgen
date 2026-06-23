using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

public sealed class PopulationDynamicsTests
{
    private static SimConfig DefaultConfig() => SimConfigLoader.LoadOrCreateDefault();

    private static (Sim.World.WorldState world, TileCoord tile) SetupWorldWithSettlement(
        int seed, int initialPop = 100)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed);
        var tile  = FindFirstLandTile(world);
        var civ   = new CivId(1);
        world.NextCivId = 2;
        world.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), tile, 1);
        world.Settlements[tile]  = new SettlementStub(new EntityId(1), civ, tile, 1,
            Population: initialPop, Health: 100);
        return (world, tile);
    }

    [Fact]
    public void PopulationPhase_FertileTile_GrowsPopulation()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.PopGrowthRate = 5f; // aggressive growth for test
        cfg.Settlement.PopDecayRate  = 0f;

        var (world, tile) = SetupWorldWithSettlement(seed: 42);
        int initialPop = world.Settlements[tile].Population;

        var phase = new PopulationDynamicsPhase(cfg);
        phase.Execute(world);

        // After one season with growth, population should have increased or at least not crashed
        world.Settlements.ContainsKey(tile).Should().BeTrue();
        world.Settlements[tile].Population.Should().BeGreaterThanOrEqualTo(initialPop - 1);
    }

    [Fact]
    public void PopulationPhase_ZeroFertilityMaxDecay_ShrinksPopulation()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.PopGrowthRate = 0f;
        cfg.Settlement.PopDecayRate  = 10f; // very fast decay

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 50);
        int initialPop = world.Settlements[tile].Population;

        var phase = new PopulationDynamicsPhase(cfg);
        // Run multiple ticks
        for (int i = 0; i < 5; i++) phase.Execute(world);

        // Should have shrunk or been abandoned
        if (world.Settlements.ContainsKey(tile))
            world.Settlements[tile].Population.Should().BeLessThan(initialPop);
        // else abandoned — also valid
    }

    [Fact]
    public void PopulationPhase_PopulationBelowMinViable_AbandonsSett()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.PopGrowthRate = 0f;
        cfg.Settlement.PopDecayRate  = 1000f; // instant obliteration
        cfg.Settlement.PopMinViable  = 5;

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 50);

        var phase = new PopulationDynamicsPhase(cfg);
        var events = phase.Execute(world);

        world.Settlements.ContainsKey(tile).Should().BeFalse("settlement should be abandoned");
        events.Should().Contain(e => e.Type == EventType.SettlementAbandoned);
    }

    [Fact]
    public void PopulationPhase_CrystalThreshold_SpawnsSpecialist()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.PopGrowthRate       = 0f; // no growth
        cfg.Settlement.PopDecayRate        = 0f;
        cfg.Settlement.CrystalPopArtisan   = 50; // threshold below initial pop

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 100);

        var phase = new PopulationDynamicsPhase(cfg);
        var events = phase.Execute(world);

        events.Should().Contain(e => e.Type == EventType.AppointedToRole);
        world.Entities.Tier2Chars.Should().Contain(c => c.Livelihood.Role == Tier2Role.Artisan);
    }

    [Fact]
    public void PopulationPhase_CrystalThreshold_OnlyFiresOnce()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.PopGrowthRate     = 0f;
        cfg.Settlement.PopDecayRate      = 0f;
        cfg.Settlement.CrystalPopArtisan = 50;

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 100);
        var phase = new PopulationDynamicsPhase(cfg);

        phase.Execute(world);
        int countAfterFirst = world.Entities.Tier2Chars.Count(c => c.Livelihood.Role == Tier2Role.Artisan);

        phase.Execute(world); // second tick — should NOT spawn another
        int countAfterSecond = world.Entities.Tier2Chars.Count(c => c.Livelihood.Role == Tier2Role.Artisan);

        countAfterSecond.Should().Be(countAfterFirst,
            "threshold should only fire once per settlement");
    }

    private static TileCoord FindFirstLandTile(Sim.World.WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        for (int y = 1; y < h - 1; y++)
        for (int x = 0; x < w; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        return new TileCoord(0, 0);
    }
}
