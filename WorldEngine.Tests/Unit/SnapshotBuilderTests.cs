using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

public class SnapshotBuilderTests
{
    private static WorldState BuildWorld(int seed = 1)
    {
        var cfg = new WorldConfig { Seed = seed, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
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

    private static readonly SnapshotBuilder _builder = new();

    private static WorldSnapshot Snap(WorldState world, ViewportRect? vp = null) =>
        _builder.Build(world, vp ?? ViewportRect.Default, OverlayType.Biome,
            SimSpeed.Normal, paused: false, ticksPerSecond: 4, recentEvents: Array.Empty<SimEvent>());

    [Fact]
    public void SnapshotBuilder_EffectiveTempHigherAtEquator()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var vp = new ViewportRect(0, 0, w, h);
        var snap = Snap(world, vp);

        // Equatorial row ≈ center Y; polar rows are at Y=0 and Y=h-1
        int equatorY = h / 2;
        int polarY   = 0;

        float equatorTemp = 0f; int equatorCount = 0;
        float polarTemp   = 0f; int polarCount   = 0;

        foreach (var (coord, tile) in snap.VisibleTiles)
        {
            if (coord.Y == equatorY) { equatorTemp += tile.EffectiveTemperature; equatorCount++; }
            if (coord.Y == polarY)   { polarTemp   += tile.EffectiveTemperature; polarCount++;   }
        }

        float meanEquator = equatorCount > 0 ? equatorTemp / equatorCount : 0;
        float meanPolar   = polarCount   > 0 ? polarTemp   / polarCount   : 0;

        meanEquator.Should().BeGreaterThan(meanPolar,
            "equatorial tiles should have higher effective temperature than polar tiles");
    }

    [Fact]
    public void SnapshotBuilder_HasActiveDisasterTrueWhenInRegistry()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth;
        var testCoord = new TileCoord(w / 2, world.TileGrid.TileHeight / 2);

        world.ActiveTileDisasters[testCoord] = new List<ActiveDisaster>
        {
            new ActiveDisaster(DisasterType.Wildfire, 0.5f, 5, new EventId(1))
        };

        var vp = new ViewportRect(0, 0, w, world.TileGrid.TileHeight);
        var snap = Snap(world, vp);

        snap.VisibleTiles[testCoord].HasActiveDisaster.Should().BeTrue(
            "tile with an entry in ActiveTileDisasters must have HasActiveDisaster=true");
    }

    [Fact]
    public void SnapshotBuilder_HasActiveDisasterFalseWhenNotInRegistry()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var vp = new ViewportRect(0, 0, w, h);
        var snap = Snap(world, vp);

        foreach (var (coord, tile) in snap.VisibleTiles)
        {
            if (!world.ActiveTileDisasters.ContainsKey(coord))
                tile.HasActiveDisaster.Should().BeFalse(
                    $"tile {coord} not in ActiveTileDisasters must have HasActiveDisaster=false");
        }
    }

    [Fact]
    public void SnapshotBuilder_InspectedTilePopulatedWhenSet()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        world.InspectedTile = new TileCoord(w / 2, h / 2);
        var snap = Snap(world, new ViewportRect(0, 0, w, h));

        snap.InspectedTile.Should().NotBeNull(
            "InspectedTile should be set in the snapshot when world.InspectedTile is set");
        snap.InspectedTile!.Coord.Should().Be(world.InspectedTile.Value);
    }

    [Fact]
    public void SnapshotBuilder_InspectedTileNullWhenNotSet()
    {
        var world = BuildWorld();
        var snap = Snap(world);
        snap.InspectedTile.Should().BeNull("no tile selected → InspectedTile must be null");
    }

    [Fact]
    public void TileDisplayData_IsImmutableRecord()
    {
        // Sealed records implement IEquatable<T> and have a compiler-generated <Clone>$ method.
        typeof(TileDisplayData).Should().Implement<IEquatable<TileDisplayData>>(
            "TileDisplayData must be a record (immutable by convention)");

        var cloneMethod = typeof(TileDisplayData).GetMethod("<Clone>$");
        cloneMethod.Should().NotBeNull("records expose a <Clone>$ method — confirms this is a record type");

        // All properties on a positional record use init-only setters.
        // In reflection, init setters are decorated with IsExternalInit — verify via IsInitOnly flag.
        foreach (var prop in typeof(TileDisplayData).GetProperties())
        {
            bool isInitOnly = prop.SetMethod?.ReturnParameter
                .GetRequiredCustomModifiers()
                .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) == true;
            isInitOnly.Should().BeTrue(
                $"TileDisplayData.{prop.Name} should use an init-only setter (positional record property)");
        }
    }

    [Fact]
    public void TileInspectorData_ContainsAllDeposits()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Find a tile that has deposits
        var depositCoord = world.ResourceRegistry.Keys.FirstOrDefault();
        if (depositCoord == default) return; // no deposits in this seed — skip

        world.InspectedTile = depositCoord;
        var snap = Snap(world, new ViewportRect(0, 0, w, h));

        snap.InspectedTile!.Deposits.Should()
            .BeEquivalentTo(world.ResourceRegistry[depositCoord],
                "inspector data must include all deposits from ResourceRegistry");
    }

    [Fact]
    public void TileInspectorData_ContainsAllDisasters()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var coord = new TileCoord(w / 2, h / 2);

        var disasters = new List<ActiveDisaster>
        {
            new(DisasterType.Wildfire, 0.8f, 3, new EventId(10)),
            new(DisasterType.Flood, 0.3f, 2, new EventId(11))
        };
        world.ActiveTileDisasters[coord] = disasters;
        world.InspectedTile = coord;

        var snap = Snap(world, new ViewportRect(0, 0, w, h));

        snap.InspectedTile!.Disasters.Should()
            .BeEquivalentTo(disasters, "inspector data must include all disasters from registry");
    }

    [Fact]
    public void TileInspectorData_IsInActiveDroughtCorrect()
    {
        var world = BuildWorld();
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        // Pick a land tile
        TileCoord landCoord = default;
        for (int y = 0; y < h && landCoord == default; y++)
            for (int x = 0; x < w && landCoord == default; x++)
            {
                var c = new TileCoord(x, y);
                if (world.IsLand(c)) landCoord = c;
            }

        var tile = world.GetTile(landCoord);
        var biome = (BiomeType)tile.BiomeType;

        // Create a drought matching this tile's biome
        int latBand = landCoord.Y / (h / 4);
        world.ActiveDroughts.Add(new ActiveDrought(latBand, biome, 0.6f, 2, new EventId(99)));
        world.InspectedTile = landCoord;

        var snap = Snap(world, new ViewportRect(0, 0, w, h));

        snap.InspectedTile!.IsInActiveDrought.Should().BeTrue(
            "tile whose biome+latitude band matches an ActiveDrought should report IsInActiveDrought=true");
    }
}
