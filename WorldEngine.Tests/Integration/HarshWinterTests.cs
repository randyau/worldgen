using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>M16 16.1b — harsh winter drains character Health and increases settlement decay.</summary>
public class HarshWinterTests
{
    [Fact]
    public void HarshWinter_DrainsCharacterHealth()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 11);
        var tile  = WorldGenTestHelpers.FindLandTile(world);
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var c = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(c);
        int startHealth = c.Health;

        world.HarshWinterTicksRemaining = 4;
        new CharacterBehaviorPhase(world.SimConfig).Execute(world, world.CurrentTick, isAnnualTick: true);

        c.Health.Should().Be(startHealth - world.SimConfig.Disasters.HarshWinterCharacterHealthDrain,
            "a living character should lose Health each year a harsh winter is active");
    }

    [Fact]
    public void HarshWinter_IncreasesSettlementDecay()
    {
        float scoreNormal = Run(harshWinter: false);
        float scoreWinter = Run(harshWinter: true);

        scoreWinter.Should().BeLessThan(scoreNormal,
            "a settlement should decay faster during a harsh winter than under identical otherwise conditions");

        static float Run(bool harshWinter)
        {
            var world = WorldTestHelper.CreateSmallWorld(seed: 12);
            var tile = WorldGenTestHelpers.FindLandTile(world);
            var founder = CharacterFactory.Spawn(tile, (BiomeType)world.TileGrid.GetTile(tile).BiomeType,
                world.WorldSeed, 1L, world.SimConfig, world.CurrentYear, startAsAdult: true);
            world.Entities.Add(founder);
            var pending = new List<PendingEvent>();
            CivTracker.Resolve(new EstablishSettlement(founder.Id, tile), world, pending, world.SimConfig.SettlementNames);

            // Isolate the decay-side effect: zero growth so the only per-tick delta is decay.
            world.SimConfig.Settlement.PopGrowthRate = 0f;
            var phase = new PopulationDynamicsPhase(world.SimConfig);
            for (int i = 0; i < 10; i++)
            {
                if (harshWinter) world.HarshWinterTicksRemaining = 4; // keep active for the whole run
                phase.Execute(world);
            }
            var stub = world.Settlements[tile];
            return stub.Population + stub.PopulationF;
        }
    }
}
