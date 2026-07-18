using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Tests for the --audit-food diagnostic: verifies that FoodAuditSink captures
/// per-tile factor lines for a known settlement and that the tile sum ≈ raw food supply.
/// </summary>
public class FoodAuditTests
{
    private static WorldState BuildWorldWithSettlement(int seed, out TileCoord settleTile)
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
        var world     = TileGridAssembler.Assemble(ctx);

        // Find a non-ocean land tile far from edges to use as a settlement
        settleTile = new TileCoord(0, 0);
        for (int y = 10; y < world.TileGrid.TileHeight - 10; y++)
        for (int x = 10; x < world.TileGrid.TileWidth  - 10; x++)
        {
            var t    = new TileCoord(x, y);
            var tile = world.TileGrid.GetTile(t);
            var bio  = (BiomeType)tile.BiomeType;
            if (bio is not (BiomeType.Ocean or BiomeType.CoastalWater) && tile.Fertility > 50)
            {
                settleTile = t;
                goto foundTile;
            }
        }
        foundTile:

        var civId = new CivId(1);
        world.Civilizations[civId] = new Civilization(civId, "TestCiv", new EntityId(1), settleTile, 0);

        world.Settlements[settleTile] = new SettlementStub(
            FounderId: new EntityId(1),
            CivId: civId,
            Tile: settleTile,
            FoundedYear: 0,
            Population: 500,
            Health: 100,
            Name: "AuditVillage");

        // Give the settlement territory: a 5×5 patch of nearby land tiles
        var territory = new HashSet<TileCoord>();
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            var t   = new TileCoord(settleTile.X + dx, settleTile.Y + dy);
            var bio = (BiomeType)world.TileGrid.GetTile(t).BiomeType;
            if (bio is not (BiomeType.Ocean or BiomeType.CoastalWater))
                territory.Add(t);
        }
        world.Civilizations[civId].CityTerritories[settleTile] = territory;

        return world;
    }

    [Fact]
    public void FoodAudit_AllSettlements_CapturesFactorLines()
    {
        // Arrange
        var world = BuildWorldWithSettlement(seed: 42, out var settleTile);
        var phase = new ResourcePressurePhase(world.SimConfig);
        var sink  = new FoodAuditSink { AuditAll = true };

        // Act: run phase with audit sink
        phase.Execute(world, tick: 0, audit: sink);

        // Assert: Print() produces audit output without throwing
        using var sw          = new System.IO.StringWriter();
        var       originalOut = Console.Out;
        Console.SetOut(sw);
        sink.Print();
        Console.SetOut(originalOut);

        var output = sw.ToString();
        output.Should().Contain("FOOD AUDIT",                 "the header must be present");
        output.Should().Contain("Settlement: AuditVillage",   "the known settlement name must appear");
        output.Should().Contain("Per-tile factor breakdown:", "factor table header must be present");
        output.Should().Contain("TOTAL",                      "the tile-sum row must be present");
    }

    [Fact]
    public void FoodAudit_TileSum_ApproximatesRawFoodSupply()
    {
        // Arrange
        var world = BuildWorldWithSettlement(seed: 777, out var settleTile);
        var phase = new ResourcePressurePhase(world.SimConfig);

        // Run one tick first so stores/capacity are primed
        phase.Execute(world, tick: 0);

        var sink = new FoodAuditSink { AuditAll = true };
        phase.Execute(world, tick: 1, audit: sink);

        // Capture output
        using var sw          = new System.IO.StringWriter();
        var       originalOut = Console.Out;
        Console.SetOut(sw);
        sink.Print();
        Console.SetOut(originalOut);

        var output = sw.ToString();

        // Parse "Raw food supply" and "TOTAL" values from output
        float rawSupply = float.NaN;
        float tileTotal = float.NaN;

        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("Raw food supply (people supported):"))
            {
                var valStr = line.Split(':').Last().Trim();
                float.TryParse(valStr,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out rawSupply);
            }
            if (line.TrimStart().StartsWith("TOTAL"))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                    float.TryParse(parts[^1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out tileTotal);
            }
        }

        rawSupply.Should().NotBe(float.NaN, "raw food supply must be present in audit output");
        tileTotal.Should().NotBe(float.NaN, "tile total must be present in audit output");

        // Per-tile sum should match raw supply within 1 person (float rounding only)
        tileTotal.Should().BeApproximately(rawSupply, 1.0f,
            "the sum of per-tile contributions must equal the recorded raw food supply");
    }

    [Fact]
    public void FoodAudit_TargetCoord_OnlyCapturesMatchingSettlement()
    {
        // Arrange: build world with a settlement, audit only a non-matching tile
        var world = BuildWorldWithSettlement(seed: 9999, out var settleTile);
        var phase = new ResourcePressurePhase(world.SimConfig);

        // Audit with a target that does NOT match settleTile → output should show no settlement
        var sink = new FoodAuditSink { AuditAll = false };
        var nonMatchingTile = new TileCoord(settleTile.X + 100, settleTile.Y + 100);
        sink.TargetCoords.Add(nonMatchingTile);

        phase.Execute(world, tick: 0, audit: sink);

        using var sw          = new System.IO.StringWriter();
        var       originalOut = Console.Out;
        Console.SetOut(sw);
        sink.Print();
        Console.SetOut(originalOut);

        var output = sw.ToString();
        // Non-matching coordinate: no settlement section should appear
        output.Should().NotContain("Settlement:", "non-matching target should produce no settlement output");

        // Now audit with the matching tile
        var sink2 = new FoodAuditSink { AuditAll = false };
        sink2.TargetCoords.Add(settleTile);
        phase.Execute(world, tick: 0, audit: sink2);

        using var sw2          = new System.IO.StringWriter();
        Console.SetOut(sw2);
        sink2.Print();
        Console.SetOut(originalOut);

        var output2 = sw2.ToString();
        output2.Should().Contain("Settlement: AuditVillage",
            "matching target should produce settlement output");
    }
}
