using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M11 Phase 11.0 — seafaring foundations: Port improvement gating, SeafaringConfig, IsShallowOcean.
/// </summary>
public class SeafaringTests
{
    private static WorldState BuildWorld(int seed = 42) => WorldTestHelper.CreateSmallWorld(seed);

    private static void SetTile(WorldState world, TileCoord coord, BiomeType biome, TileStaticFlags flags = TileStaticFlags.None)
    {
        var tile = world.TileGrid.GetTile(coord);
        tile.BiomeType   = (byte)biome;
        tile.StaticFlags = flags;
        tile.Fertility   = 50;
        world.TileGrid.SetTile(coord, tile);
    }

    // ─── IsShallowOcean ─────────────────────────────────────────────────────

    [Fact]
    public void IsShallowOcean_TrueForOceanTileAdjacentToLand()
    {
        var world = BuildWorld();
        var ocean = new TileCoord(5, 5);
        var land  = new TileCoord(6, 5);
        SetTile(world, ocean, BiomeType.Ocean);
        SetTile(world, land, BiomeType.Plains);

        world.IsShallowOcean(ocean).Should().BeTrue("the ocean tile has a land neighbor");
    }

    [Fact]
    public void IsShallowOcean_FalseForOpenOceanWithNoLandNeighbor()
    {
        var world = BuildWorld();
        var center = new TileCoord(5, 5);
        // Clear the full radius-2 neighborhood to ocean so no land is within classification range.
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
            SetTile(world, new TileCoord(5 + dx, 5 + dy), BiomeType.Ocean);

        world.IsShallowOcean(center).Should().BeFalse("no land tile is within the shallow-ocean radius");
    }

    [Fact]
    public void IsShallowOcean_FalseForLandTile()
    {
        var world = BuildWorld();
        var land = new TileCoord(5, 5);
        SetTile(world, land, BiomeType.Plains);

        world.IsShallowOcean(land).Should().BeFalse("land tiles are never shallow ocean");
    }

    // ─── Port build gating ──────────────────────────────────────────────────

    private static (WorldState world, TileCoord cityTile, EntityId builderId, CivId civId) SetupCivWithBuilder(TileCoord targetTile)
    {
        var world = BuildWorld();
        var cityTile = new TileCoord(10, 10);
        SetTile(world, cityTile, BiomeType.Plains);

        var founder = CharacterFactory.Spawn(cityTile, BiomeType.Plains, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
        world.Entities.Add(founder);

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, cityTile), world, pending, world.SimConfig.SettlementNames);
        var civId = world.Settlements[cityTile].CivId;

        // Claim the target tile into the same city's territory directly (bypass radius-based claim
        // timing so the test controls exactly which tile is buildable).
        world.TerritoryMap[targetTile] = cityTile;
        world.Civilizations[civId].CityTerritories[cityTile].Add(targetTile);

        var builder = CharacterFactory.Spawn(targetTile, BiomeType.Plains, world.WorldSeed, 2L, world.SimConfig, world.CurrentYear);
        CivTracker.SetCharacterCiv(builder, civId, OrganizationRole.Member, world);
        world.Entities.Add(builder);
        world.Civilizations[civId].Members.Add(builder.Id);

        builder.Goals.Add(new GoalData
        {
            Type       = GoalType.BuildImprovement,
            Priority   = 1f,
            TargetTile = targetTile,
            ResourceTag = nameof(ImprovementType.Port),
            Progress   = 1f, // pre-complete so one resolve call finishes construction
        });

        return (world, cityTile, builder.Id, civId);
    }

    [Fact]
    public void BuildImprovement_Port_SucceedsOnCoastalTile()
    {
        var targetTile = new TileCoord(11, 10);
        var (world, _, builderId, _) = SetupCivWithBuilder(targetTile);
        SetTile(world, targetTile, BiomeType.Plains, TileStaticFlags.IsCoastal);

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new BuildImprovement(builderId, targetTile, ImprovementType.Port), world, pending);

        world.ImprovementMap.Should().ContainKey(targetTile);
        world.ImprovementMap[targetTile].Type.Should().Be(ImprovementType.Port);
    }

    [Fact]
    public void BuildImprovement_Port_RejectedOnNonCoastalTile()
    {
        var targetTile = new TileCoord(11, 10);
        var (world, _, builderId, _) = SetupCivWithBuilder(targetTile);
        SetTile(world, targetTile, BiomeType.Plains, TileStaticFlags.None);

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new BuildImprovement(builderId, targetTile, ImprovementType.Port), world, pending);

        world.ImprovementMap.Should().NotContainKey(targetTile,
            "a Port cannot be built on a tile with no adjacent water");
    }

    // ─── SeafaringConfig ────────────────────────────────────────────────────

    [Fact]
    public void SeafaringConfig_BindsFromToml()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid()}.toml");
        try
        {
            var sourceConfig = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "config", "sim_config.toml");
            if (!File.Exists(sourceConfig)) sourceConfig = Path.Combine(AppContext.BaseDirectory, "config", "sim_config.toml");
            if (!File.Exists(sourceConfig)) sourceConfig = "config/sim_config.toml";
            File.Copy(sourceConfig, tempPath, overwrite: true);

            var config = SimConfigLoader.LoadOrCreateDefault(tempPath);

            config.Seafaring.Should().NotBeNull();
            config.Seafaring.OceanCrossingEnabled.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void SeafaringConfig_DefaultsWhenSectionAbsent()
    {
        var config = new SimConfig();
        config.Seafaring.Should().NotBeNull();
        config.Seafaring.OceanCrossingEnabled.Should().BeTrue();
    }
}
