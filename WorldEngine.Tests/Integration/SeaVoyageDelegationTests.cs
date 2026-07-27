using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// M11 Phase 11.2 — ruler delegation: an over-capacity, landlocked (no local frontier) civ that
/// owns a Port seeds a SeaVoyage goal for its emigrant instead of FoundCity; without a Port it
/// falls back to the pre-M11 FoundCity behavior unchanged.
/// </summary>
public class SeaVoyageDelegationTests
{
    private static WorldState BuildSyntheticWorld(SimConfig simConfig)
    {
        var config = new WorldConfig { Seed = 1, WidthKm = 200, HeightKm = 100, TileWidthKm = 10 };
        var grid = new TileGrid(20, 10);
        var world = new WorldState(config, simConfig, grid, new SeasonalProfile[200],
            new Dictionary<TileCoord, List<ResourceDeposit>>(), stormCorridorNormalizedLat: 0f);

        for (int y = 0; y < 10; y++)
        for (int x = 0; x < 20; x++)
            SetTile(world, new TileCoord(x, y), BiomeType.Ocean);

        // Landmass A: a tiny strip (x8..12,y5) — small enough that InitialCityClaimRadius (=2)
        // claims the whole thing when a settlement is founded at its center, leaving no local
        // frontier. Landmass B: x15..19,y5, a 2-tile strait away (x13,x14) — within both the
        // shallow-ocean radius and the default MaxVoyageTiles.
        for (int x = 8; x <= 12; x++) SetTile(world, new TileCoord(x, 5), BiomeType.Plains);
        for (int x = 15; x <= 19; x++) SetTile(world, new TileCoord(x, 5), BiomeType.Plains);

        return world;
    }

    private static void SetTile(WorldState world, TileCoord coord, BiomeType biome)
    {
        var tile = world.TileGrid.GetTile(coord);
        tile.BiomeType = (byte)biome;
        tile.Fertility = 80;
        tile.BaseMoisture = 80;
        world.TileGrid.SetTile(coord, tile);
    }

    private static SimConfig FastEmigrationConfig()
    {
        var sim = TestSimConfig.With(c =>
        {
            c.Character.CivBirthChancePerSeason = 1f;   // guarantee a birth roll on the first tick
            c.Character.CivBirthMinPop          = 1;
            c.Settlement.EmigrationBonusChance  = 1f;
        });
        return sim;
    }

    private static (WorldState world, TileCoord cityTile, CivId civId) FoundLandlockedCiv(SimConfig sim)
    {
        var world = BuildSyntheticWorld(sim);
        var cityTile = new TileCoord(10, 5);
        var founder = CharacterFactory.Spawn(cityTile, BiomeType.Plains, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
        world.Entities.Add(founder);

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, cityTile), world, pending, world.SimConfig.SettlementNames);
        var civId = world.Settlements[cityTile].CivId;

        // Over capacity: population above EmigrationThreshold (0.75) of CarryingCapacity.
        var stub = world.Settlements[cityTile];
        world.Settlements[cityTile] = stub with { Population = (int)(stub.CarryingCapacity * 0.9f) };

        return (world, cityTile, civId);
    }

    [Fact]
    public void LandlockedWithPort_EmigrantGetsSeaVoyageGoal()
    {
        var sim = FastEmigrationConfig();
        var (world, cityTile, civId) = FoundLandlockedCiv(sim);

        // Build a Port directly on a coastal tile of the civ's own territory (bypassing the
        // multi-tick BuildImprovement flow — this test is about delegation, not construction).
        var portTile = new TileCoord(12, 5); // edge of the landmass, adjacent to the strait
        world.ImprovementMap[portTile] = new TileImprovement(ImprovementType.Port, cityTile, world.CurrentYear, world.Settlements[cityTile].FounderId);
        world.Civilizations[civId].CityTerritories[cityTile].Should().Contain(portTile,
            "sanity check: the Port tile must actually be inside the claimed territory used by CivOwnsPort");

        world.CurrentTick = 0;
        var phase = new CharacterBehaviorPhase(world.SimConfig);
        phase.Execute(world, 0, isAnnualTick: true);

        var emigrant = world.Entities.Characters.FirstOrDefault(c => c.Id != world.Settlements[cityTile].FounderId);
        emigrant.Should().NotBeNull("an emigrant should have been born under emigration pressure");
        emigrant!.Goals.Should().ContainSingle(g => g.Type == GoalType.SeaVoyage,
            "landlocked + Port-owning civ should delegate a sea voyage, not an overland FoundCity trek");
    }

    [Fact]
    public void LandlockedWithoutPort_EmigrantFallsBackToFoundCityGoal()
    {
        var sim = FastEmigrationConfig();
        var (world, cityTile, _) = FoundLandlockedCiv(sim);
        // No Port built.

        world.CurrentTick = 0;
        var phase = new CharacterBehaviorPhase(world.SimConfig);
        phase.Execute(world, 0, isAnnualTick: true);

        var emigrant = world.Entities.Characters.FirstOrDefault(c => c.Id != world.Settlements[cityTile].FounderId);
        emigrant.Should().NotBeNull("an emigrant should have been born under emigration pressure");
        emigrant!.Goals.Should().ContainSingle(g => g.Type == GoalType.FoundCity,
            "without a Port, delegation must fall back to the pre-M11 FoundCity behavior unchanged");
        emigrant.Goals.Should().NotContain(g => g.Type == GoalType.SeaVoyage);
    }

    [Fact]
    public void NoOpportunity_NeverSeedsSeaVoyage()
    {
        // Regression guard: even with the feature enabled, a civ that never becomes landlocked
        // (plenty of local frontier) and never builds a Port must never see a SeaVoyage goal.
        var sim = TestSimConfig.Default();
        var world = BuildSyntheticWorld(sim);
        // Give landmass A more room so HasLocalFrontier stays true (widen it well past the claim radius).
        for (int x = 0; x <= 12; x++) SetTile(world, new TileCoord(x, 5), BiomeType.Plains);

        var cityTile = new TileCoord(6, 5);
        var founder = CharacterFactory.Spawn(cityTile, BiomeType.Plains, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
        world.Entities.Add(founder);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, cityTile), world, pending, world.SimConfig.SettlementNames);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        for (long tick = 0; tick < 20; tick++)
        {
            world.CurrentTick = tick;
            phase.Execute(world, tick, isAnnualTick: tick % 4 == 0);
        }

        world.Entities.Characters.Should().NotContain(c => c.Goals.Any(g => g.Type == GoalType.SeaVoyage),
            "a civ with ample local frontier and no Port has no path to ever forming a SeaVoyage goal");
    }
}
