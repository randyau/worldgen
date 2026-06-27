using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// Phase 3.4 — Tile Inspect panel integration tests.
/// Verifies TileInspectorData is populated with territory, improvement, and history data.
/// </summary>
public class TileInspectTests
{
    private static WorldState BuildWorld(int seed = 42)
        => WorldTestHelper.CreateSmallWorld(seed);

    private static (WorldState world, TileCoord cityTile, CivId civId) PlantSettlement(WorldState world)
    {
        TileCoord cityTile = default;
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth;  x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c)) continue;
            if (world.TileGrid.GetTile(c).Fertility < 10) continue;
            cityTile = c;
            goto Found;
        }
        Found:
        var biome   = (BiomeType)world.TileGrid.GetTile(cityTile).BiomeType;
        var founder = CharacterFactory.Spawn(cityTile, biome, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
        world.Entities.Add(founder);

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(
            new EstablishSettlement(founder.Id, cityTile),
            world, pending, world.SimConfig.SettlementNames);

        var civId = world.Settlements[cityTile].CivId;
        return (world, cityTile, civId);
    }

    // Helper: build TileInspectorData using SnapshotBuilder's internal logic
    private static TileInspectorData BuildInspectorData(WorldState world, TileCoord coord)
    {
        var builder  = new SnapshotBuilder();
        var snapshot = builder.Build(
            world,
            OverlayType.Biome,
            SimSpeed.Paused,
            paused: true,
            ticksPerSecond: 0,
            recentEvents: Array.Empty<SimEvent>());
        // The InspectedTile was null because we didn't set it on world — build it by
        // temporarily setting and calling Build again
        world.InspectedTile = coord;
        snapshot = builder.Build(
            world,
            OverlayType.Biome,
            SimSpeed.Paused,
            paused: true,
            ticksPerSecond: 0,
            recentEvents: Array.Empty<SimEvent>());
        world.InspectedTile = null;
        return snapshot.InspectedTile!;
    }

    // ─── Test 1: TerritoryOwner populated when tile is claimed ───────────────

    [Fact]
    public void TileInspectorData_IncludesTerritoryOwner_WhenTileIsClaimed()
    {
        var world = BuildWorld();
        var (_, cityTile, _) = PlantSettlement(world);

        // The city tile itself should be in TerritoryMap (city owns itself)
        Assert.True(world.TerritoryMap.ContainsKey(cityTile),
            "City tile should be in territory map after settlement founding");

        var data = BuildInspectorData(world, cityTile);

        Assert.NotNull(data.TerritoryOwnerName);
        Assert.NotNull(data.TerritoryCityName);
        Assert.Equal(cityTile, data.TerritoryCityTile);
    }

    // ─── Test 2: Improvement populated when one exists on the tile ──────────

    [Fact]
    public void TileInspectorData_IncludesImprovement_WhenPresent()
    {
        var world = BuildWorld();
        var (_, cityTile, _) = PlantSettlement(world);

        // Find an adjacent land tile to place an improvement on
        TileCoord targetTile = default;
        for (int y = 0; y < world.TileGrid.TileHeight; y++)
        for (int x = 0; x < world.TileGrid.TileWidth;  x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c) || c == cityTile) continue;
            targetTile = c;
            goto FoundImpTile;
        }
        FoundImpTile:

        // Manually place an improvement
        var imp = new TileImprovement(
            Type:      ImprovementType.Farm,
            CityTile:  cityTile,
            BuiltYear: 10,
            BuilderId: new EntityId(1));
        world.ImprovementMap[targetTile] = imp;

        var data = BuildInspectorData(world, targetTile);

        Assert.Equal(ImprovementType.Farm, data.Improvement);
        Assert.Equal(10, data.ImprovementBuiltYear);
    }

    // ─── Test 3: TerritoryOwner null for unclaimed tile ──────────────────────

    [Fact]
    public void TileInspectorData_TerritoryOwner_IsNull_WhenUnclaimed()
    {
        var world = BuildWorld();
        // No settlements planted — TerritoryMap is empty

        // Find any land tile
        TileCoord unclaimedTile = default;
        for (int y = 0; y < world.TileGrid.TileHeight; y++)
        for (int x = 0; x < world.TileGrid.TileWidth;  x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) { unclaimedTile = c; break; }
        }

        Assert.False(world.TerritoryMap.ContainsKey(unclaimedTile));

        var data = BuildInspectorData(world, unclaimedTile);

        Assert.Null(data.TerritoryOwnerName);
        Assert.Null(data.TerritoryCityName);
        Assert.Null(data.TerritoryCityTile);
    }
}
