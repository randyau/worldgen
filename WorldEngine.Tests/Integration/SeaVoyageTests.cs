using FluentAssertions;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// M11 Phase 11.1 — voyage command &amp; resolution: route computation and full multi-tick crossing.
/// Uses a fully synthetic world (not the worldgen pipeline) so land/water layout is exact and
/// deterministic — real worldgen output can't guarantee two hand-picked tiles are on genuinely
/// separate landmasses without incidental land routes elsewhere on the map.
/// </summary>
public class SeaVoyageTests
{
    private static WorldState BuildSyntheticWorld(int widthTiles, int heightTiles, SimConfig? simConfig = null)
    {
        var config = new WorldConfig
        {
            Seed = 1,
            WidthKm = widthTiles * 10,
            HeightKm = heightTiles * 10,
            TileWidthKm = 10,
        };
        var sim = simConfig ?? TestSimConfig.Default();
        var grid = new TileGrid(widthTiles, heightTiles);
        var world = new WorldState(config, sim, grid, new SeasonalProfile[widthTiles * heightTiles],
            new Dictionary<TileCoord, List<ResourceDeposit>>(), stormCorridorNormalizedLat: 0f);

        for (int y = 0; y < heightTiles; y++)
        for (int x = 0; x < widthTiles; x++)
            SetTile(world, new TileCoord(x, y), BiomeType.Ocean);

        return world;
    }

    private static void SetTile(WorldState world, TileCoord coord, BiomeType biome)
    {
        var tile = world.TileGrid.GetTile(coord);
        tile.BiomeType = (byte)biome;
        tile.Fertility = 50;
        world.TileGrid.SetTile(coord, tile);
    }

    private static void SetLandStrip(WorldState world, int xStart, int xEndInclusive, int y)
    {
        for (int x = xStart; x <= xEndInclusive; x++)
            SetTile(world, new TileCoord(x, y), BiomeType.Plains);
    }

    // ─── FindVoyageDestination ──────────────────────────────────────────────

    [Fact]
    public void FindVoyageDestination_FindsFarShoreAcrossNarrowStrait()
    {
        var world = BuildSyntheticWorld(20, 10);
        SetLandStrip(world, 0, 4, 5);   // origin landmass: x0..4
        SetLandStrip(world, 8, 14, 5);  // far landmass: x8..14 (3-tile-wide strait at x5..7)

        var scorer = new UtilityScorer(world.SimConfig);
        var dest = scorer.FindVoyageDestination(new TileCoord(4, 5), world);

        dest.Should().NotBeNull("a 3-tile strait is within the default MaxVoyageTiles and shallow-ocean radius");
        world.IsLand(dest!.Value).Should().BeTrue();
        world.GetLandmassId(dest.Value).Should().NotBe(world.GetLandmassId(new TileCoord(4, 5)),
            "the destination must be a genuinely different landmass, not the origin's own shore");
    }

    [Fact]
    public void FindVoyageDestination_NullWhenNoFarShoreWithinRange()
    {
        var sim = TestSimConfig.With(c => c.Seafaring.MaxVoyageTiles = 1);
        var world = BuildSyntheticWorld(20, 10, sim);
        SetLandStrip(world, 0, 4, 5);
        SetLandStrip(world, 8, 14, 5); // 3-tile strait, but MaxVoyageTiles=1 can't reach it

        var scorer = new UtilityScorer(world.SimConfig);
        var dest = scorer.FindVoyageDestination(new TileCoord(4, 5), world);

        dest.Should().BeNull("the far shore is out of range");
    }

    [Fact]
    public void FindVoyageDestination_NullWhenOriginIsIsolatedOpenOcean()
    {
        var world = BuildSyntheticWorld(20, 10);
        SetLandStrip(world, 0, 4, 5); // only one landmass on the whole map

        var scorer = new UtilityScorer(world.SimConfig);
        var dest = scorer.FindVoyageDestination(new TileCoord(4, 5), world);

        dest.Should().BeNull("there is no second landmass anywhere to reach");
    }

    // ─── Full multi-tick crossing ───────────────────────────────────────────

    [Fact]
    public void SeaVoyage_CharacterCrossesStraitAndEventsFire()
    {
        var world = BuildSyntheticWorld(20, 10);
        SetLandStrip(world, 0, 4, 5);
        SetLandStrip(world, 8, 14, 5);

        var origin = new TileCoord(4, 5);
        var scorer = new UtilityScorer(world.SimConfig);
        var dest = scorer.FindVoyageDestination(origin, world);
        dest.Should().NotBeNull();

        var character = CharacterFactory.Spawn(origin, BiomeType.Plains, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
        character.Goals.Add(new GoalData
        {
            Type       = GoalType.SeaVoyage,
            Priority   = 1f,
            TargetTile = dest,
            FormedTick = 0,
            StaleSince = 0,
        });
        world.Entities.Add(character);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        var allEvents = new List<PendingEvent>();
        bool arrived = false;
        for (long tick = 0; tick < 60 && !arrived; tick++)
        {
            world.CurrentTick = tick;
            var events = phase.Execute(world, tick);
            allEvents.AddRange(events);
            arrived = character.Location == dest!.Value;
            if (!character.IsAlive) break;
        }

        arrived.Should().BeTrue("the character should reach the far shore within a reasonable number of ticks");
        allEvents.Should().Contain(e => e.Type == EventType.SeaVoyageEmbarked,
            "departing the origin shore onto water must emit SeaVoyageEmbarked");
        allEvents.Should().Contain(e => e.Type == EventType.SeaVoyageCompleted,
            "arriving on the far shore must emit SeaVoyageCompleted");

        var voyageGoal = character.Goals.FirstOrDefault(g => g.Type == GoalType.SeaVoyage);
        (voyageGoal == null || voyageGoal.IsComplete).Should().BeTrue(
            "the SeaVoyage goal must be marked complete (and is eligible for pruning) once the crossing finishes");
    }

    [Fact]
    public void SeaVoyage_DisabledByConfig_CharacterNeverEntersWater()
    {
        var sim = TestSimConfig.With(c => c.Seafaring.OceanCrossingEnabled = false);
        var world = BuildSyntheticWorld(20, 10, sim);
        SetLandStrip(world, 0, 4, 5);
        SetLandStrip(world, 8, 14, 5);

        var origin = new TileCoord(4, 5);
        var character = CharacterFactory.Spawn(origin, BiomeType.Plains, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
        character.Goals.Add(new GoalData
        {
            Type       = GoalType.SeaVoyage,
            Priority   = 1f,
            TargetTile = new TileCoord(8, 5),
            FormedTick = 0,
            StaleSince = 0,
        });
        world.Entities.Add(character);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        for (long tick = 0; tick < 20; tick++)
        {
            world.CurrentTick = tick;
            phase.Execute(world, tick);
        }

        world.IsLand(character.Location).Should().BeTrue(
            "with OceanCrossingEnabled=false, the SeaVoyage candidate must never be offered");
    }

    // ─── Reproducibility ─────────────────────────────────────────────────────

    [Fact]
    public void SeaVoyage_SameSeedProducesSameRoute()
    {
        TileCoord? Run()
        {
            var world = BuildSyntheticWorld(20, 10);
            SetLandStrip(world, 0, 4, 5);
            SetLandStrip(world, 8, 14, 5);

            var origin = new TileCoord(4, 5);
            var character = CharacterFactory.Spawn(origin, BiomeType.Plains, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
            var scorer = new UtilityScorer(world.SimConfig);
            var dest = scorer.FindVoyageDestination(origin, world);
            character.Goals.Add(new GoalData { Type = GoalType.SeaVoyage, Priority = 1f, TargetTile = dest, FormedTick = 0, StaleSince = 0 });
            world.Entities.Add(character);

            var phase = new CharacterBehaviorPhase(world.SimConfig);
            for (long tick = 0; tick < 60 && character.Location != dest!.Value; tick++)
            {
                world.CurrentTick = tick;
                phase.Execute(world, tick);
            }
            return character.Location;
        }

        var r1 = Run();
        var r2 = Run();
        r1.Should().Be(r2, "same synthetic map + same seed must produce the same crossing outcome");
    }
}
