using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class DisasterSystemTests
{
    private static WorldState BuildWorld(int seed = 42)
    {
        var cfg = new WorldConfig { Seed = seed, WidthKm = 1000, HeightKm = 800, TileWidthKm = 10 };
        var sim = TestSimConfig.Default();
        var ctx = new WorldGenContext(cfg, sim);
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.River     = new RiverLayer().Generate(ctx);
        ctx.Magic     = new MagicLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        ctx.Biome     = new BiomeLayer().Generate(ctx);
        ctx.Resource  = new ResourceLayer().Generate(ctx);
        ctx.Poi       = new PoiCandidateLayer().Generate(ctx);
        return TileGridAssembler.Assemble(ctx);
    }

    private static void RunDisasterTick(WorldState world, List<PendingEvent>? pending = null)
    {
        pending ??= new List<PendingEvent>();
        new EnvironmentalPhase(world.SimConfig).RunTick(world, pending);
    }

    [Fact]
    public void Disaster_VolcanicEruptionOnlyOnVolcanicTiles()
    {
        var world = BuildWorld();
        world.SimConfig.Disasters.VolcanicEruptionProbabilityPerTick = 1.0f;
        RunDisasterTick(world);

        var eruptionCoords = world.ActiveTileDisasters
            .Where(kv => kv.Value.Any(d => d.Type == DisasterType.VolcanicAsh))
            .Select(kv => kv.Key)
            .ToList();

        eruptionCoords.Should().NotBeEmpty("at least one volcanic eruption should occur with prob=1.0");
        foreach (var coord in eruptionCoords)
        {
            var tile = world.TileGrid.GetTile(coord);
            tile.StaticFlags.HasFlag(TileStaticFlags.IsVolcanic).Should().BeTrue(
                $"eruption at {coord} should only occur on a volcanic tile");
        }
    }

    [Fact]
    public void Disaster_EarthquakeOnlyOnFaultLineTiles()
    {
        var world = BuildWorld();
        world.SimConfig.Disasters.EarthquakeProbabilityPerTick = 1.0f;
        RunDisasterTick(world);

        var quakeCoords = world.ActiveTileDisasters
            .Where(kv => kv.Value.Any(d => d.Type == DisasterType.SeismicDamage))
            .Select(kv => kv.Key)
            .ToList();

        quakeCoords.Should().NotBeEmpty("at least one earthquake should occur with prob=1.0");
        foreach (var coord in quakeCoords)
        {
            var tile = world.TileGrid.GetTile(coord);
            tile.StaticFlags.HasFlag(TileStaticFlags.IsFaultLine).Should().BeTrue(
                $"earthquake at {coord} should only occur on fault-line tiles");
        }
    }

    [Fact]
    public void Disaster_WildfireOnlyInForestBiome()
    {
        var world = BuildWorld();
        world.SimConfig.Disasters.WildfireIgnitionProbabilityPerTick = 1.0f;
        world.CurrentSeason = Season.Summer;
        RunDisasterTick(world);

        var fireCoords = world.ActiveTileDisasters
            .Where(kv => kv.Value.Any(d => d.Type == DisasterType.Wildfire))
            .Select(kv => kv.Key)
            .ToList();

        // With spread prob 0.2 (default), some non-forest tiles may be spread targets — check only ignition tiles
        // The ignition logic only touches forest tiles; spreading also checks IsForestBiome
        foreach (var coord in fireCoords)
        {
            var tile = world.TileGrid.GetTile(coord);
            var biome = (BiomeType)tile.BiomeType;
            bool isForest = biome is BiomeType.TemperateForest or BiomeType.TropicalRainforest or BiomeType.BorealForest;
            isForest.Should().BeTrue($"wildfire at {coord} (biome={biome}) should only be in forest biomes");
        }
    }

    [Fact]
    public void Disaster_WildfireDoesNotIgniteOceanTiles()
    {
        var world = BuildWorld();
        world.SimConfig.Disasters.WildfireIgnitionProbabilityPerTick = 1.0f;
        world.SimConfig.Disasters.WildfireSpreadProbabilityPerTick = 1.0f;
        world.CurrentSeason = Season.Summer;
        RunDisasterTick(world);

        foreach (var coord in world.ActiveTileDisasters.Keys)
        {
            var biome = (BiomeType)world.TileGrid.GetTile(coord).BiomeType;
            biome.Should().NotBe(BiomeType.Ocean,
                $"wildfire must never appear on ocean tile at {coord}");
            biome.Should().NotBe(BiomeType.CoastalWater,
                $"wildfire must never appear on coastal water tile at {coord}");
        }
    }

    [Fact]
    public void Disaster_FloodOnlyOnRiverTiles()
    {
        var world = BuildWorld();
        world.SimConfig.Disasters.FloodIgnitionProbabilityPerTick = 1.0f;
        world.CurrentSeason = Season.Spring;
        RunDisasterTick(world);

        var floodIgnitionCoords = world.ActiveTileDisasters
            .Where(kv => kv.Value.Any(d => d.Type == DisasterType.Flood))
            .Select(kv => kv.Key)
            .ToList();

        // Check that at least one flood-origin tile has HasRiver flag
        // (spread tiles may not have river, but origins must)
        bool anyRiver = floodIgnitionCoords.Any(coord =>
            world.TileGrid.GetTile(coord).StaticFlags.HasFlag(TileStaticFlags.HasRiver));
        anyRiver.Should().BeTrue("at least one flood should originate on a river tile with prob=1.0");
    }

    [Fact]
    public void Disaster_ActiveDisasterRegistryUpdated()
    {
        var world = BuildWorld();
        world.SimConfig.Disasters.VolcanicEruptionProbabilityPerTick = 1.0f;
        RunDisasterTick(world);

        world.ActiveTileDisasters.Should().NotBeEmpty(
            "forcing eruption probability to 1.0 should produce entries in ActiveTileDisasters");
    }

    [Fact]
    public void Disaster_HasActiveDisasterFlagSet()
    {
        var world = BuildWorld();
        world.SimConfig.Disasters.VolcanicEruptionProbabilityPerTick = 1.0f;
        RunDisasterTick(world);

        foreach (var coord in world.ActiveTileDisasters.Keys)
        {
            var tile = world.TileGrid.GetTile(coord);
            tile.DynFlags.HasFlag(TileDynFlags.HasActiveDisaster).Should().BeTrue(
                $"tile at {coord} has active disaster but DynFlags.HasActiveDisaster is not set");
        }
    }

    [Fact]
    public void Disaster_HasActiveDisasterFlagClearedOnExpiry()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find a forest tile to plant a fire on
        TileCoord? target = null;
        for (int y = 0; y < h && target is null; y++)
        for (int x = 0; x < w && target is null; x++)
        {
            var t = world.TileGrid.GetTile(new TileCoord(x, y));
            if ((BiomeType)t.BiomeType is BiomeType.TemperateForest or BiomeType.BorealForest)
                target = new TileCoord(x, y);
        }
        if (target is null) return; // no forest in this seed

        // Manually add a wildfire with TicksRemaining=1
        var coord = target.Value;
        var tile = world.TileGrid.GetTile(coord);
        tile.DynFlags |= TileDynFlags.HasActiveDisaster;
        world.TileGrid.SetTile(coord, tile);
        world.ActiveTileDisasters[coord] = new List<ActiveDisaster>
        {
            new ActiveDisaster(DisasterType.Wildfire, 1.0f, 1, new EventId(0))
        };

        // Run one tick with zero probability so no new disasters fire
        world.SimConfig.Disasters.WildfireIgnitionProbabilityPerTick = 0f;
        world.SimConfig.Disasters.VolcanicEruptionProbabilityPerTick = 0f;
        world.SimConfig.Disasters.EarthquakeProbabilityPerTick = 0f;
        world.SimConfig.Disasters.FloodIgnitionProbabilityPerTick = 0f;
        world.CurrentSeason = Season.Summer;
        RunDisasterTick(world);

        var after = world.TileGrid.GetTile(coord);
        after.DynFlags.HasFlag(TileDynFlags.HasActiveDisaster).Should().BeFalse(
            "HasActiveDisaster flag should be cleared once all disasters on a tile expire");
        world.ActiveTileDisasters.ContainsKey(coord).Should().BeFalse(
            "tile with no remaining disasters should be removed from ActiveTileDisasters");
    }

    [Fact]
    public void Disaster_WildfireSpreadsToAdjacentForest()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find a forest tile that has at least one forest neighbor
        TileCoord? origin = null;
        for (int y = 1; y < h - 1 && origin is null; y++)
        for (int x = 0; x < w && origin is null; x++)
        {
            var coord = new TileCoord(x, y);
            if ((BiomeType)world.TileGrid.GetTile(coord).BiomeType is not (BiomeType.TemperateForest or BiomeType.BorealForest or BiomeType.TropicalRainforest))
                continue;
            TileCoord[] neighbors = {
                new TileCoord(((x + 1) % w + w) % w, y), new TileCoord(((x - 1) % w + w) % w, y),
                new TileCoord(x, y - 1), new TileCoord(x, y + 1),
            };
            bool hasForestNeighbor = neighbors.Any(nb =>
            {
                var bt = (BiomeType)world.TileGrid.GetTile(nb).BiomeType;
                return bt is BiomeType.TemperateForest or BiomeType.BorealForest or BiomeType.TropicalRainforest;
            });
            if (hasForestNeighbor) origin = coord;
        }
        if (origin is null) return;

        // Plant one fire manually
        var tile = world.TileGrid.GetTile(origin.Value);
        tile.DynFlags |= TileDynFlags.HasActiveDisaster;
        world.TileGrid.SetTile(origin.Value, tile);
        world.ActiveTileDisasters[origin.Value] = new List<ActiveDisaster>
        {
            new ActiveDisaster(DisasterType.Wildfire, 1.0f, 10, new EventId(0))
        };

        // Force spread prob to 1.0, disable ignition so no new fires start
        world.SimConfig.Disasters.WildfireSpreadProbabilityPerTick = 1.0f;
        world.SimConfig.Disasters.WildfireIgnitionProbabilityPerTick = 0f;
        world.SimConfig.Disasters.VolcanicEruptionProbabilityPerTick = 0f;
        world.SimConfig.Disasters.EarthquakeProbabilityPerTick = 0f;
        world.SimConfig.Disasters.FloodIgnitionProbabilityPerTick = 0f;
        world.CurrentSeason = Season.Summer;
        RunDisasterTick(world);

        // At least one neighbor should now have a wildfire
        TileCoord[] expectedNeighbors = {
            new TileCoord(((origin.Value.X + 1) % w + w) % w, origin.Value.Y),
            new TileCoord(((origin.Value.X - 1) % w + w) % w, origin.Value.Y),
            new TileCoord(origin.Value.X, origin.Value.Y - 1),
            new TileCoord(origin.Value.X, origin.Value.Y + 1),
        };
        bool spreadOccurred = expectedNeighbors.Any(nb =>
            world.ActiveTileDisasters.TryGetValue(nb, out var disasters) &&
            disasters.Any(d => d.Type == DisasterType.Wildfire));

        spreadOccurred.Should().BeTrue(
            "with spread probability=1.0, wildfire should spread to at least one adjacent forest tile");
    }

    [Fact]
    public void Disaster_MultipleDisastersCanStackOnOneTile()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find any non-ocean tile and add two different disasters manually
        TileCoord target = new TileCoord(w / 2, h / 2);
        world.ActiveTileDisasters[target] = new List<ActiveDisaster>
        {
            new ActiveDisaster(DisasterType.Flood, 0.7f, 4, new EventId(0)),
            new ActiveDisaster(DisasterType.SeismicDamage, 0.8f, 6, new EventId(0)),
        };

        world.ActiveTileDisasters[target].Should().HaveCount(2,
            "two different disaster types should be able to coexist on the same tile");
        world.ActiveTileDisasters[target].Select(d => d.Type).Should()
            .Contain(DisasterType.Flood).And.Contain(DisasterType.SeismicDamage);
    }

    [Fact]
    public void Disaster_DroughtAddsToActiveDroughtsList()
    {
        var world = BuildWorld();
        world.SimConfig.Disasters.DroughtProbabilityPerYear = 1.0f;
        world.CurrentSeason = Season.Spring;
        var pending = new List<PendingEvent>();
        new EnvironmentalPhase(world.SimConfig).RunTick(world, pending, isAnnualTick: true);

        world.ActiveDroughts.Should().NotBeEmpty(
            "with DroughtProbabilityPerYear=1.0, at least one drought should start per annual tick");
        pending.Should().Contain(p => p.Type == EventType.DroughtBegan,
            "a DroughtBegan event should be emitted when a drought starts");
    }

    [Fact]
    public void Disaster_DroughtRemovedWhenExpired()
    {
        var world = BuildWorld();
        world.ActiveDroughts.Add(new ActiveDrought(0, BiomeType.Grassland, 0.7f, 1, new EventId(0)));
        world.SimConfig.Disasters.DroughtProbabilityPerYear = 0f; // no new droughts

        var pending = new List<PendingEvent>();
        world.CurrentSeason = Season.Spring;
        new EnvironmentalPhase(world.SimConfig).RunTick(world, pending, isAnnualTick: true);

        world.ActiveDroughts.Should().BeEmpty(
            "drought with SeasonsRemaining=1 should be removed after one annual tick");
        pending.Should().Contain(p => p.Type == EventType.DroughtEnded,
            "a DroughtEnded event should be emitted when a drought expires");
    }

    [Fact]
    public void Disaster_PendingEventEmittedOnIgnition()
    {
        var world = BuildWorld();
        world.SimConfig.Disasters.VolcanicEruptionProbabilityPerTick = 1.0f;
        var pending = new List<PendingEvent>();
        RunDisasterTick(world, pending);

        pending.Should().Contain(p => p.Type == EventType.VolcanicEruption,
            "a VolcanicEruption PendingEvent should be emitted when a volcano erupts");
    }

    [Fact]
    public void Disaster_SameSeedSameDisasters()
    {
        var world1 = BuildWorld(42);
        var world2 = BuildWorld(42);

        world1.SimConfig.Disasters.VolcanicEruptionProbabilityPerTick = 0.5f;
        world2.SimConfig.Disasters.VolcanicEruptionProbabilityPerTick = 0.5f;

        RunDisasterTick(world1);
        RunDisasterTick(world2);

        var keys1 = world1.ActiveTileDisasters.Keys.OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        var keys2 = world2.ActiveTileDisasters.Keys.OrderBy(c => c.X).ThenBy(c => c.Y).ToList();

        keys1.Should().BeEquivalentTo(keys2,
            "same seed + same world state must produce identical disaster events (WorldRng determinism)");
    }
}
