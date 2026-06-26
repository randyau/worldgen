using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// Phase 3.0 — City-State Territory Model integration tests.
/// </summary>
public class TerritoryTests
{
    private static WorldState BuildWorld(int seed = 42)
        => WorldTestHelper.CreateSmallWorld(seed);

    // ─── Helper: plant a settlement and run territory claims ─────────────────

    private static (WorldState world, TileCoord cityTile, CivId civId) PlantSettlement(
        WorldState world)
    {
        // Find the first fertile land tile
        TileCoord cityTile = default;
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth;  x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c)) continue;
            if (world.TileGrid.GetTile(c).Fertility < 10) continue;
            cityTile = c;
            goto FoundTile;
        }
        FoundTile:

        // Spawn a founder character
        var biome   = (BiomeType)world.TileGrid.GetTile(cityTile).BiomeType;
        var founder = CharacterFactory.Spawn(cityTile, biome, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
        world.Entities.Add(founder);

        // Establish a settlement via CivTracker (creates civ + claims initial territory)
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(
            new EstablishSettlement(founder.Id, cityTile),
            world, pending, world.SimConfig.SettlementNames);

        // Verify civ was created
        var civId = world.Settlements[cityTile].CivId;
        return (world, cityTile, civId);
    }

    // ─── Test 1: Initial territory claim at founding ──────────────────────────

    [Fact]
    public void EstablishSettlement_ClaimsInitialTerritory()
    {
        var world = BuildWorld();
        var (_, cityTile, civId) = PlantSettlement(world);

        // City tile claims itself
        world.TerritoryMap.Should().ContainKey(cityTile,
            "the city tile must own itself");
        world.TerritoryMap[cityTile].Should().Be(cityTile,
            "a city tile maps to itself in TerritoryMap");

        // Some surrounding tiles should also be claimed
        var civ = world.Civilizations[civId];
        civ.CityTerritories.Should().ContainKey(cityTile,
            "CityTerritories must have an entry for the new city");
        civ.CityTerritories[cityTile].Count.Should().BeGreaterThan(0,
            "at least the city tile itself should be owned");

        // All owned tiles must be in TerritoryMap pointing to this city
        foreach (var t in civ.CityTerritories[cityTile])
        {
            world.TerritoryMap.Should().ContainKey(t,
                $"owned tile {t} must appear in TerritoryMap");
            world.TerritoryMap[t].Should().Be(cityTile,
                $"tile {t} must point to cityTile {cityTile}");
        }
    }

    // ─── Test 2: TerritoryPhase expands territory ────────────────────────────

    [Fact]
    public void TerritoryPhase_ExpandsTerritoryAnnually()
    {
        var world = BuildWorld(seed: 7);
        var (_, cityTile, civId) = PlantSettlement(world);

        int initialCount = world.Civilizations[civId].CityTerritories[cityTile].Count;

        // Inflate population so the city wants more tiles than it currently has
        world.Settlements[cityTile] = world.Settlements[cityTile] with
        {
            Population = world.SimConfig.Territory.MinCityTiles
                       * world.SimConfig.Territory.ClaimTilesPerPerson * 3
        };

        var phase = new TerritoryPhase(world.SimConfig);
        var events = phase.Execute(world);

        int finalCount = world.Civilizations[civId].CityTerritories[cityTile].Count;
        finalCount.Should().BeGreaterThanOrEqualTo(initialCount,
            "annual territory phase should not shrink a growing city");

        // Should emit TerritoryExpanded events
        events.Should().Contain(e => e.Type == EventType.TerritoryExpanded,
            "TerritoryPhase must emit TerritoryExpanded events when tiles are claimed");
    }

    // ─── Test 3: Territory release on settlement abandonment ─────────────────

    [Fact]
    public void RegisterRuin_ReleasesTerritory()
    {
        var world = BuildWorld(seed: 13);
        var (_, cityTile, civId) = PlantSettlement(world);

        // Verify territory is claimed
        world.TerritoryMap.Should().ContainKey(cityTile);
        int ownedBefore = world.Civilizations[civId].CityTerritories[cityTile].Count;
        ownedBefore.Should().BeGreaterThan(0);

        // Abandon / ruin the settlement
        var stub = world.Settlements[cityTile];
        world.Settlements.Remove(cityTile);
        var pending = new List<PendingEvent>();
        CivTracker.RegisterRuin(cityTile, stub, "abandoned", world, pending);

        // All territory tiles should be gone
        world.TerritoryMap.Should().NotContainKey(cityTile,
            "city tile must no longer be in TerritoryMap after abandonment");
        world.Civilizations[civId].CityTerritories.Should().NotContainKey(cityTile,
            "CityTerritories must remove the entry for the abandoned city");

        // Should emit TerritoryLost event
        pending.Should().Contain(e => e.Type == EventType.TerritoryLost,
            "RegisterRuin must emit a TerritoryLost event");
    }

    // ─── Test 4: Snapshot propagation ────────────────────────────────────────

    [Fact]
    public void SnapshotBuilder_IncludesTerritoryAndImprovements()
    {
        var world = BuildWorld(seed: 17);
        var (_, cityTile, _) = PlantSettlement(world);

        // Plant an improvement on the city tile (skip build ticks for test simplicity)
        world.ImprovementMap[cityTile] = new TileImprovement(
            ImprovementType.Farm, cityTile, world.CurrentYear, new EntityId(1L));

        var builder = new SnapshotBuilder();
        var snap = builder.Build(
            world,
            OverlayType.Biome,
            SimSpeed.Normal,
            paused: true,
            ticksPerSecond: 4,
            recentEvents: Array.Empty<SimEvent>());

        snap.TerritoryMap.Should().ContainKey(cityTile,
            "WorldSnapshot.TerritoryMap must include the founded city tile");
        snap.TerritoryMap[cityTile].CityTile.Should().Be(cityTile,
            "TerritorySnapshot.CityTile must point to the owning city");

        snap.ImprovementMap.Should().ContainKey(cityTile,
            "WorldSnapshot.ImprovementMap must include the planted farm");
        snap.ImprovementMap[cityTile].ImprovementType.Should().Be("Farm",
            "ImprovementSnapshot.ImprovementType should be 'Farm'");
    }
}
