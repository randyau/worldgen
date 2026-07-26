using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M9 Phase 9.2 — settlement specialization: EMA-tracked dominant non-vital resource, the
/// resulting production bonus, and the merchant export bonus that reads it.
/// </summary>
public class SettlementSpecializationTests
{
    private static (WorldState world, TileCoord tile) BuildSingleSettlement(
        int seed, int population, IReadOnlyDictionary<string, float>? stores = null)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed);

        TileCoord tile = default;
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) { tile = c; goto Found; }
        }
        Found:

        var civId = new CivId(1);
        world.Civilizations[civId] = new Civilization(civId, "TestCiv", new EntityId(1), tile, 0);
        world.Civilizations[civId].CityTerritories[tile] = new HashSet<TileCoord> { tile };

        world.Settlements[tile] = new SettlementStub(
            FounderId: new EntityId(1),
            CivId: civId,
            Tile: tile,
            FoundedYear: 0,
            Population: population,
            Health: 100,
            Name: "TestVille",
            ResourceStores: stores);

        // Single high-quality iron deposit, no depth penalty — a strong, stable per-capita
        // surplus for a small population so it clears SpecializationMinRatio comfortably.
        world.ResourceRegistry[tile] = new List<ResourceDeposit> { new("iron", Quality: 255, Depth: 0) };

        return (world, tile);
    }

    [Fact]
    public void Specialization_ConvergesToSustainedDominantResource_AcrossTicks()
    {
        var (world, tile) = BuildSingleSettlement(seed: 20, population: 10);
        var phase = new ResourcePressurePhase(world.SimConfig);

        for (int tick = 0; tick < 40; tick++)
            phase.Execute(world, tick);

        var stub = world.Settlements[tile];
        stub.Specialization.Should().Be("iron");
        stub.SpecializationStrength.Should().BeGreaterThan(0.7f,
            "sustained dominance across many ticks should drive strength close to 1 via EMA");
    }

    [Fact]
    public void Specialization_DoesNotFlipInstantly_WhenDominantResourceChanges()
    {
        var (world, tile) = BuildSingleSettlement(seed: 21, population: 10);
        var phase = new ResourcePressurePhase(world.SimConfig);

        for (int tick = 0; tick < 40; tick++)
            phase.Execute(world, tick);

        world.Settlements[tile].Specialization.Should().Be("iron");
        float strengthBeforeSwitch = world.Settlements[tile].SpecializationStrength;

        // Add a much stronger copper deposit — copper now dominates the ledger every tick.
        world.ResourceRegistry[tile].Add(new ResourceDeposit("copper", Quality: 255, Depth: 0));
        world.ResourceRegistry[tile].Add(new ResourceDeposit("copper", Quality: 255, Depth: 0));
        world.ResourceRegistry[tile].Add(new ResourceDeposit("copper", Quality: 255, Depth: 0));

        phase.Execute(world, tick: 40);

        var stub = world.Settlements[tile];
        // One tick after the switch: still iron (decaying), not yet flipped to copper.
        stub.Specialization.Should().Be("iron");
        stub.SpecializationStrength.Should().BeLessThan(strengthBeforeSwitch);
    }

    [Fact]
    public void SpecializedResource_AccumulatesFasterThanUnspecialized()
    {
        // Settlement A: no prior specialization (fresh, strength 0) — control.
        var (worldControl, tileControl) = BuildSingleSettlement(seed: 22, population: 10);
        new ResourcePressurePhase(worldControl.SimConfig).Execute(worldControl, tick: 0);
        float controlIron = worldControl.Settlements[tileControl].GetStore("iron");

        // Settlement B: pre-seeded with full specialization strength in iron.
        var (worldSpecialized, tileSpecialized) = BuildSingleSettlement(seed: 22, population: 10);
        worldSpecialized.Settlements[tileSpecialized] = worldSpecialized.Settlements[tileSpecialized] with
        {
            Specialization = "iron",
            SpecializationStrength = 1f
        };
        new ResourcePressurePhase(worldSpecialized.SimConfig).Execute(worldSpecialized, tick: 0);
        float specializedIron = worldSpecialized.Settlements[tileSpecialized].GetStore("iron");

        specializedIron.Should().BeGreaterThan(controlIron,
            "a full-strength specialization should grant a production multiplier over an unspecialized settlement");

        // Control forms its own (low-strength) specialization from tick 0 — the comparison is
        // between the two resulting multipliers, not "no bonus vs. full bonus".
        var cfg = worldSpecialized.SimConfig.ResourcePressure;
        float specializedMult = 1f + Math.Min(cfg.SpecializationBonusCap, 1f * cfg.SpecializationBonusScale);
        float controlMult     = 1f + Math.Min(cfg.SpecializationBonusCap,
            cfg.SpecializationSmoothingAlpha * cfg.SpecializationBonusScale);
        float expectedRatio   = specializedMult / controlMult;

        (specializedIron / controlIron).Should().BeApproximately(expectedRatio, expectedRatio * 0.02f);
    }

    [Fact]
    public void RunMerchant_PrefersExportingHomeSpecializedResource_OverEquallyScoredAlternative()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 23);
        world.SimConfig.Character.MerchantTradeChance = 1f;

        var home  = FindLandTile(world);
        var destA = FindLandTile(world, exclude: home, minDist: 3);
        var destB = FindLandTile(world, exclude: home, minDist: 3, exclude2: destA);
        var civId = new CivId(1);
        world.Civilizations[civId] = new Civilization(civId, "TestCiv", new EntityId(1), home, 0);

        // Equal raw stores and equal (neutral) destination demand for both resources — the only
        // difference is home's specialization in "iron", which should tip the routing choice.
        var homeStores = new Dictionary<string, float> { ["iron"] = 100f, ["timber"] = 100f };
        world.Settlements[home] = new SettlementStub(
            FounderId: new EntityId(1), CivId: civId, Tile: home, FoundedYear: 0,
            Population: 100, Health: 100, Name: "Home", ResourceStores: homeStores,
            Specialization: "iron", SpecializationStrength: 1f);

        world.Settlements[destA] = new SettlementStub(
            FounderId: new EntityId(2), CivId: civId, Tile: destA, FoundedYear: 0,
            Population: 100, Health: 100, Name: "DestA");
        world.Settlements[destB] = new SettlementStub(
            FounderId: new EntityId(3), CivId: civId, Tile: destB, FoundedYear: 0,
            Population: 100, Health: 100, Name: "DestB");

        var merchant = new Tier2Character(
            id: new EntityId(9003), location: home, name: "Merchant",
            personality: PersonalityVector6.Default,
            livelihood: new LivelihoodData(Tier2Role.Merchant, null, home, 0.5f),
            maxHealth: 100, maxAgeSeason: 1000);
        world.Entities.Add(merchant);

        var phase = new Tier2BehaviorPhase(world.SimConfig);
        phase.Execute(world, tick: 0);

        (world.Settlements[destA].GetStore("iron") + world.Settlements[destB].GetStore("iron"))
            .Should().BeGreaterThan(0f, "the merchant should export iron — home's specialized resource — over timber");
    }

    private static TileCoord FindLandTile(WorldState world, TileCoord? exclude = null, int minDist = 0, TileCoord? exclude2 = null)
    {
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c)) continue;
            if (exclude is { } e)
            {
                int dx = c.X - e.X, dy = c.Y - e.Y;
                if (dx * dx + dy * dy < minDist * minDist) continue;
            }
            if (exclude2 is { } e2)
            {
                int dx2 = c.X - e2.X, dy2 = c.Y - e2.Y;
                if (dx2 * dx2 + dy2 * dy2 < minDist * minDist) continue;
            }
            return c;
        }
        throw new InvalidOperationException("No suitable land tile found");
    }
}
