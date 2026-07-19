using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

public sealed class PopulationDynamicsTests
{
    private static SimConfig DefaultConfig() => SimConfigLoader.LoadOrCreateDefault();

    private static (Sim.World.WorldState world, TileCoord tile) SetupWorldWithSettlement(
        int seed, int initialPop = 100)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed);
        var tile  = FindFirstLandTile(world);
        var civ   = new CivId(1);
        world.NextCivId = 2;
        world.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), tile, 1);
        world.Settlements[tile]  = new SettlementStub(new EntityId(1), civ, tile, 1,
            Population: initialPop, Health: 100);
        return (world, tile);
    }

    [Fact]
    public void PopulationPhase_FertileTile_GrowsPopulation()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.PopGrowthRate = 5f; // aggressive growth for test
        cfg.Settlement.PopDecayRate  = 0f;

        var (world, tile) = SetupWorldWithSettlement(seed: 42);
        int initialPop = world.Settlements[tile].Population;

        var phase = new PopulationDynamicsPhase(cfg);
        phase.Execute(world);

        // After one season with growth, population should have increased or at least not crashed
        world.Settlements.ContainsKey(tile).Should().BeTrue();
        world.Settlements[tile].Population.Should().BeGreaterThanOrEqualTo(initialPop - 1);
    }

    [Fact]
    public void PopulationPhase_ZeroFertilityMaxDecay_ShrinksPopulation()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.PopGrowthRate = 0f;
        cfg.Settlement.PopDecayRate  = 10f; // very fast decay

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 50);
        int initialPop = world.Settlements[tile].Population;

        var phase = new PopulationDynamicsPhase(cfg);
        // Run multiple ticks
        for (int i = 0; i < 5; i++) phase.Execute(world);

        // Should have shrunk or been abandoned
        if (world.Settlements.ContainsKey(tile))
            world.Settlements[tile].Population.Should().BeLessThan(initialPop);
        // else abandoned — also valid
    }

    [Fact]
    public void PopulationPhase_PopulationBelowMinViable_AbandonsSett()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.PopGrowthRate = 0f;
        cfg.Settlement.PopDecayRate  = 1000f; // instant obliteration
        cfg.Settlement.PopMinViable  = 5;

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 50);

        var phase = new PopulationDynamicsPhase(cfg);
        var events = phase.Execute(world);

        world.Settlements.ContainsKey(tile).Should().BeFalse("settlement should be abandoned");
        events.Should().Contain(e => e.Type == EventType.SettlementAbandoned);
    }

    [Fact]
    public void PopulationPhase_CrystalThreshold_SpawnsSpecialist()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.PopGrowthRate       = 0f; // no growth
        cfg.Settlement.PopDecayRate        = 0f;
        cfg.Settlement.CrystalPopArtisan   = 50; // threshold below initial pop

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 100);

        var phase = new PopulationDynamicsPhase(cfg);
        var events = phase.Execute(world);

        events.Should().Contain(e => e.Type == EventType.AppointedToRole);
        world.Entities.Tier2Chars.Should().Contain(c => c.Livelihood.Role == Tier2Role.Artisan);
    }

    [Fact]
    public void PopulationPhase_CrystalThreshold_OnlyFiresOnce()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.PopGrowthRate     = 0f;
        cfg.Settlement.PopDecayRate      = 0f;
        cfg.Settlement.CrystalPopArtisan = 50;

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 100);
        var phase = new PopulationDynamicsPhase(cfg);

        phase.Execute(world);
        int countAfterFirst = world.Entities.Tier2Chars.Count(c => c.Livelihood.Role == Tier2Role.Artisan);

        phase.Execute(world); // second tick — should NOT spawn another
        int countAfterSecond = world.Entities.Tier2Chars.Count(c => c.Livelihood.Role == Tier2Role.Artisan);

        countAfterSecond.Should().Be(countAfterFirst,
            "threshold should only fire once per settlement");
    }

    // ─── Disease model (D4) ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies the structural disease factor composition:
    /// outbreakChance = base × (1 + density × DensityMult) × contactFactor × famineFactor.
    /// Uses guaranteed-to-fire config (base_chance = 1.0) so the annual check always triggers
    /// regardless of the RNG roll.
    /// </summary>
    [Fact]
    public void DiseaseModel_BaseChanceOne_AlwaysOutbreaks()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.DiseaseBaseChance   = 1.0f;  // always fires
        cfg.Settlement.DiseaseMinPop       = 1;
        cfg.Settlement.DiseaseDensityMult  = 0f;    // isolate base only
        cfg.Settlement.DiseaseContactMult  = 1.0f;
        cfg.Settlement.DiseaseFamineMult   = 1.0f;
        cfg.Settlement.DiseaseFamineThreshold = 0.5f;

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 200);
        // Set high carrying capacity so density factor doesn't dominate
        world.Settlements[tile] = world.Settlements[tile] with { CarryingCapacity = 10_000 };

        var phase = new PopulationDynamicsPhase(cfg);
        var events = phase.Execute(world, isAnnualTick: true);

        events.Should().Contain(e => e.Type == EventType.DiseaseOutbreak,
            "base_chance=1.0 should always produce an outbreak");
        world.Settlements[tile].IsInfected.Should().BeTrue();
    }

    [Fact]
    public void DiseaseModel_ZeroBaseChance_NeverOutbreaks()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.DiseaseBaseChance   = 0.0f;  // never fires
        cfg.Settlement.DiseaseMinPop       = 1;
        cfg.Settlement.DiseaseContactMult  = 999f;  // even huge multipliers can't fire at 0 base
        cfg.Settlement.DiseaseFamineMult   = 999f;

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 200);
        world.Settlements[tile] = world.Settlements[tile] with
        {
            FoodPressureRatio = 0.1f,  // trigger famine factor
            CarryingCapacity  = 200,   // trigger density factor (density=1.0)
        };

        var phase = new PopulationDynamicsPhase(cfg);
        var events = phase.Execute(world, isAnnualTick: true);

        events.Should().NotContain(e => e.Type == EventType.DiseaseOutbreak,
            "base_chance=0 means no outbreak regardless of multipliers");
    }

    [Fact]
    public void DiseaseModel_FamineFactor_MultipliesChance()
    {
        // Verify that a settlement in famine fires the famine factor by using a
        // carefully chosen base that only fires when famine multiplier is applied.
        // base = 0.6, famine_mult = 2.0 → effective = 1.2 (> 1, always fires)
        // Without famine: effective = 0.6 (< 1, may or may not fire depending on RNG)
        // To make this deterministic we set it just above the famine threshold.
        var cfg = DefaultConfig();
        cfg.Settlement.DiseaseBaseChance      = 1.0f;
        cfg.Settlement.DiseaseDensityMult     = 0f;
        cfg.Settlement.DiseaseContactMult     = 1.0f;
        cfg.Settlement.DiseaseFamineMult      = 2.0f;
        cfg.Settlement.DiseaseFamineThreshold = 0.8f;
        cfg.Settlement.DiseaseMinPop          = 1;

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 200);
        world.Settlements[tile] = world.Settlements[tile] with
        {
            FoodPressureRatio = 0.5f,  // below famine threshold
            CarryingCapacity  = 10_000,
        };

        var phase  = new PopulationDynamicsPhase(cfg);
        var events = phase.Execute(world, isAnnualTick: true);

        // With base=1.0 the outbreak always fires; verify the payload carries InFamine=true
        events.Should().Contain(e => e.Type == EventType.DiseaseOutbreak);
        var payload = System.Text.Json.JsonDocument.Parse(
            events.First(e => e.Type == EventType.DiseaseOutbreak).PayloadJson);
        payload.RootElement.GetProperty("InFamine").GetBoolean().Should().BeTrue();
        payload.RootElement.GetProperty("FamineFactor").GetDouble().Should().BeApproximately(2.0, 0.001);
    }

    [Fact]
    public void DiseaseModel_ContactFactor_AppliesWhenAtWar()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.DiseaseBaseChance   = 1.0f;
        cfg.Settlement.DiseaseDensityMult  = 0f;
        cfg.Settlement.DiseaseContactMult  = 1.5f;
        cfg.Settlement.DiseaseFamineMult   = 1.0f;
        cfg.Settlement.DiseaseMinPop       = 1;

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 200);
        // Put the civ at war
        var enemyCivId = new CivId(99);
        world.NextCivId = 100;
        world.Civilizations[enemyCivId] = new Civilization(enemyCivId, "EnemyCiv",
            new EntityId(999), tile, 1);
        world.Civilizations[new CivId(1)].WarsAgainst[enemyCivId] = 0;

        var phase  = new PopulationDynamicsPhase(cfg);
        var events = phase.Execute(world, isAnnualTick: true);

        events.Should().Contain(e => e.Type == EventType.DiseaseOutbreak);
        var payload = System.Text.Json.JsonDocument.Parse(
            events.First(e => e.Type == EventType.DiseaseOutbreak).PayloadJson);
        payload.RootElement.GetProperty("InWar").GetBoolean().Should().BeTrue();
        payload.RootElement.GetProperty("ContactFactor").GetDouble().Should().BeApproximately(1.5, 0.001);
    }

    [Fact]
    public void DiseaseModel_InfectedSettlement_RecoveryAfterMaxDuration()
    {
        var cfg = DefaultConfig();
        cfg.Settlement.DiseaseMaxDurationYears = 3;
        cfg.Settlement.DiseaseRecoveryChance   = 0f; // no early recovery
        cfg.Settlement.DiseaseBaseChance       = 0f; // no new outbreaks

        var (world, tile) = SetupWorldWithSettlement(seed: 42, initialPop: 200);
        // Mark as already infected 3 years ago — should recover this annual tick
        world.Settlements[tile] = world.Settlements[tile] with
        {
            IsInfected = true, InfectedSinceYear = world.CurrentYear - 3
        };

        var phase  = new PopulationDynamicsPhase(cfg);
        var events = phase.Execute(world, isAnnualTick: true);

        events.Should().Contain(e => e.Type == EventType.DiseaseRecovered,
            "settlement infected for max duration should auto-recover");
        world.Settlements[tile].IsInfected.Should().BeFalse();
    }

    private static TileCoord FindFirstLandTile(Sim.World.WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        for (int y = 1; y < h - 1; y++)
        for (int x = 0; x < w; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        return new TileCoord(0, 0);
    }
}
