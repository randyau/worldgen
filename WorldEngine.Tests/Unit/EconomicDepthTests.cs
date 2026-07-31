using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M9 Phase 9.1 — per-capita demand model and bonus-store consumer tests.
/// Covers: non-vital resource per-capita normalization, the five wired bonus_* consumers
/// (food yield, disease resistance, civ cohesion, military strength, trade income), and
/// demand-aware merchant routing.
/// </summary>
public class EconomicDepthTests
{
    private static (WorldState world, TileCoord tile, CivId civId) BuildSingleSettlement(
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

        // Clear any world-gen deposits at this tile so mineral tests control supply exactly.
        world.ResourceRegistry.Remove(tile);

        return (world, tile, civId);
    }

    // ── 1. Non-vital per-capita demand ────────────────────────────────────────

    [Fact]
    public void NonVitalResource_LedgerRatio_IsPerCapitaDemandNormalized()
    {
        const int population = 400;
        var (world, tile, _) = BuildSingleSettlement(seed: 1, population: population);

        // Single surface deposit, full quality, no depth penalty → raw contribution = 1.0.
        world.ResourceRegistry[tile] = new List<ResourceDeposit> { new("iron", Quality: 255, Depth: 0) };

        var phase = new ResourcePressurePhase(world.SimConfig);
        phase.Execute(world, tick: 0);

        float rate = world.SimConfig.ResourcePressure.NonVitalDemandPerCapita;
        float expectedRatio = 1.0f / (population * rate);

        var ledger = world.Settlements[tile].ResourceLedger!;
        ledger.Should().ContainKey("iron");
        ledger["iron"].Should().BeApproximately(expectedRatio, expectedRatio * 0.01f);
    }

    // ── 2. bonus_food_yield ────────────────────────────────────────────────────

    [Fact]
    public void BonusFoodYield_ScalesFoodRatio_ByCappedMultiplier()
    {
        var (worldBase, tileBase, _) = BuildSingleSettlement(seed: 2, population: 300);
        new ResourcePressurePhase(worldBase.SimConfig).Execute(worldBase, tick: 0);
        float baseRatio = worldBase.Settlements[tileBase].FoodPressureRatio;

        var stores = new Dictionary<string, float> { ["bonus_food_yield"] = 0.5f };
        var (worldBonus, tileBonus, _) = BuildSingleSettlement(seed: 2, population: 300, stores: stores);
        new ResourcePressurePhase(worldBonus.SimConfig).Execute(worldBonus, tick: 0);
        float bonusRatio = worldBonus.Settlements[tileBonus].FoodPressureRatio;

        var cfg = worldBonus.SimConfig.ResourcePressure;
        float expectedMult = 1f + Math.Min(cfg.FoodYieldBonusCap, 0.5f * cfg.FoodYieldBonusScale);

        bonusRatio.Should().BeApproximately(baseRatio * expectedMult, baseRatio * expectedMult * 0.01f);
    }

    [Fact]
    public void BonusFoodYield_Cap_LimitsMultiplierAtExtremeStoreValue()
    {
        var (worldBase, tileBase, _) = BuildSingleSettlement(seed: 3, population: 300);
        new ResourcePressurePhase(worldBase.SimConfig).Execute(worldBase, tick: 0);
        float baseRatio = worldBase.Settlements[tileBase].FoodPressureRatio;

        // Absurdly large store value — must clamp at FoodYieldBonusCap, not scale unbounded.
        var stores = new Dictionary<string, float> { ["bonus_food_yield"] = 1000f };
        var (worldBonus, tileBonus, _) = BuildSingleSettlement(seed: 3, population: 300, stores: stores);
        new ResourcePressurePhase(worldBonus.SimConfig).Execute(worldBonus, tick: 0);
        float bonusRatio = worldBonus.Settlements[tileBonus].FoodPressureRatio;

        float cap = worldBonus.SimConfig.ResourcePressure.FoodYieldBonusCap;
        bonusRatio.Should().BeApproximately(baseRatio * (1f + cap), baseRatio * (1f + cap) * 0.01f);
    }

    // ── 3. bonus_civ_cohesion ──────────────────────────────────────────────────

    [Fact]
    public void BonusCivCohesion_DampensUnrestAccrual_UpToCap()
    {
        var cfg = TestSimConfig.Default();
        cfg.Unrest.UnrestComfortRadius = 0; // guarantee a positive distance-driver accrual

        var world = WorldTestHelper.CreateSmallWorld(seed: 4);
        world.SimConfig.Unrest.UnrestComfortRadius = 0;

        var capital = FindLandTile(world);
        var distant = FindLandTile(world, exclude: capital, minDist: 3);

        var civId = new CivId(1);
        world.Civilizations[civId] = new Civilization(civId, "TestCiv", new EntityId(1), capital, 0);

        var stores = new Dictionary<string, float> { ["bonus_civ_cohesion"] = 0.05f };
        world.Settlements[distant] = new SettlementStub(
            FounderId: new EntityId(1), CivId: civId, Tile: distant, FoundedYear: 0,
            Population: 100, Health: 100, Name: "Distant", ResourceStores: stores);

        var pending = new List<PendingEvent>();
        CivTracker.RunUnrestAndSecession(world, pending);

        int dx = distant.X - capital.X, dy = distant.Y - capital.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float rawAccrual = dist * world.SimConfig.Unrest.UnrestDistancePerTile;
        var ucfg = world.SimConfig.Unrest;
        float expectedAccrual = Math.Max(0f, rawAccrual - Math.Min(ucfg.CohesionBonusCap, 0.05f * ucfg.CohesionBonusScale));

        world.Settlements[distant].Unrest.Should().BeApproximately(expectedAccrual, 1e-4f);
    }

    // ── 4. bonus_military_strength ─────────────────────────────────────────────

    [Fact]
    public void BonusMilitaryStrength_IncreasesAttackerWinRate_AcrossYears()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 5);
        var cityA = FindLandTile(world);
        var cityB = FindLandTile(world, exclude: cityA, minDist: 3);

        var civAId = new CivId(1);
        var civBId = new CivId(2);
        var founderA = new EntityId(100);
        var founderB = new EntityId(200);

        var civA = new Civilization(civAId, "CivA", founderA, cityA, 0);
        var civB = new Civilization(civBId, "CivB", founderB, cityB, 0);
        world.Civilizations[civAId] = civA;
        world.Civilizations[civBId] = civB;
        civA.WarsAgainst[civBId] = 0;
        civB.WarsAgainst[civAId] = 0;
        civA.Members.Add(founderA);

        var founder = CharacterFactory.Spawn(cityA, (BiomeType)world.TileGrid.GetTile(cityA).BiomeType,
            world.WorldSeed, 1L, world.SimConfig, world.CurrentYear);
        founder.Skills = founder.Skills with { Combat = 0.2f };
        CivTracker.SetCharacterCiv(founder, civAId, OrganizationRole.Leader, world);
        world.Entities.Add(founder);
        civA.Members.Clear();
        civA.Members.Add(founder.Id);

        world.Settlements[cityA] = new SettlementStub(
            FounderId: founderA, CivId: civAId, Tile: cityA, FoundedYear: 0,
            Population: 200, Health: 100, Name: "CapitalA");

        int WinsAcrossYears(float attackerBonus)
        {
            int wins = 0;
            for (int year = 0; year < 60; year++)
            {
                world.CurrentYear = year;
                world.Settlements[cityB] = new SettlementStub(
                    FounderId: founderB, CivId: civBId, Tile: cityB, FoundedYear: 0,
                    Population: 200, Health: 100, Name: "CapitalB");

                var stores = attackerBonus > 0f
                    ? new Dictionary<string, float> { ["bonus_military_strength"] = attackerBonus }
                    : null;
                world.Settlements[cityA] = world.Settlements[cityA] with { ResourceStores = stores };

                var pending = new List<PendingEvent>();
                CivTracker.RunWarCampaigns(world, pending);
                if (world.Settlements[cityB].Health < 100) wins++;
            }
            return wins;
        }

        int winsNoBonus = WinsAcrossYears(0f);
        int winsWithBonus = WinsAcrossYears(1000f); // clamps at MilitaryStrengthBonusCap

        winsWithBonus.Should().BeGreaterThan(winsNoBonus,
            "a capped military-strength bonus should raise the attacker's win rate across independent yearly rolls");
    }

    // ── 5. bonus_disease_resistance ────────────────────────────────────────────

    [Fact]
    public void BonusDiseaseResistance_LowersOutbreakRate_AcrossYears()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 6);
        var tile = FindLandTile(world);
        var civId = new CivId(1);
        world.Civilizations[civId] = new Civilization(civId, "TestCiv", new EntityId(1), tile, 0);

        int CountOutbreaks(float resistanceStore)
        {
            int outbreaks = 0;
            var phase = new PopulationDynamicsPhase(world.SimConfig);
            for (int year = 0; year < 80; year++)
            {
                world.CurrentYear = year;
                var stores = resistanceStore > 0f
                    ? new Dictionary<string, float> { ["bonus_disease_resistance"] = resistanceStore }
                    : null;
                world.Settlements[tile] = new SettlementStub(
                    FounderId: new EntityId(1), CivId: civId, Tile: tile, FoundedYear: 0,
                    Population: 500, Health: 100, Name: "Plagueville",
                    CarryingCapacity: 500, ResourceStores: stores, IsInfected: false);

                phase.Execute(world, isAnnualTick: true);
                if (world.Settlements[tile].IsInfected) outbreaks++;
            }
            return outbreaks;
        }

        int outbreaksNoBonus = CountOutbreaks(0f);
        int outbreaksWithBonus = CountOutbreaks(1000f); // clamps at DiseaseResistanceBonusCap

        outbreaksWithBonus.Should().BeLessThan(outbreaksNoBonus,
            "a capped disease-resistance bonus should reduce outbreak frequency across independent yearly rolls");
    }

    // ── 6. bonus_trade_income ──────────────────────────────────────────────────

    [Fact]
    public void BonusTradeIncome_ScalesMerchantTransfer_ByCappedMultiplier()
    {
        int TransferAmount(float bonusStore)
        {
            var world = WorldTestHelper.CreateSmallWorld(seed: 7);
            world.SimConfig.Character.MerchantTradeChance = 1f; // always attempt a trade

            var home = FindLandTile(world);
            var dest = FindLandTile(world, exclude: home, minDist: 3);
            var civId = new CivId(1);
            world.Civilizations[civId] = new Civilization(civId, "TestCiv", new EntityId(1), home, 0);

            var homeStores = new Dictionary<string, float> { ["iron"] = 100f };
            if (bonusStore > 0f) homeStores["bonus_trade_income"] = bonusStore;

            world.Settlements[home] = new SettlementStub(
                FounderId: new EntityId(1), CivId: civId, Tile: home, FoundedYear: 0,
                Population: 100, Health: 100, Name: "Home", ResourceStores: homeStores);
            world.Settlements[dest] = new SettlementStub(
                FounderId: new EntityId(2), CivId: civId, Tile: dest, FoundedYear: 0,
                Population: 100, Health: 100, Name: "Dest");

            var merchant = new Tier2Character(
                id: new EntityId(9001), location: home, name: "Merchant",
                personality: PersonalityVector6.Default,
                livelihood: new LivelihoodData(Tier2Role.Merchant, null, home, 0.5f),
                maxHealth: 100, maxAgeSeason: 1000);
            world.Entities.Add(merchant);

            var phase = new Tier2BehaviorPhase(world.SimConfig);
            phase.Execute(world, tick: 0);

            return (int)(100f - world.Settlements[home].GetStore("iron"));
        }

        int baseTransfer = TransferAmount(0f);
        int bonusTransfer = TransferAmount(0.3f);

        bonusTransfer.Should().BeGreaterThan(baseTransfer,
            "bonus_trade_income should scale up the merchant's transfer fraction");
    }

    // ── 7. Demand-aware merchant routing ───────────────────────────────────────

    [Fact]
    public void RunMerchant_PrefersDeficientDestination_OverLargerRawSurplus()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 8);
        world.SimConfig.Character.MerchantTradeChance = 1f;

        var home    = FindLandTile(world);
        var destA   = FindLandTile(world, exclude: home, minDist: 3);
        var destB   = FindLandTile(world, exclude: home, minDist: 3, exclude2: destA);
        var civId   = new CivId(1);
        world.Civilizations[civId] = new Civilization(civId, "TestCiv", new EntityId(1), home, 0);

        // Home has a larger raw surplus of "timber" than "copper", but destA (timber's
        // destination) is well-supplied per-capita (ratio >= 1) while destB (copper's
        // destination) is deeply deficient (ratio << 1) — demand weighting should favor copper.
        var homeStores = new Dictionary<string, float> { ["timber"] = 100f, ["copper"] = 40f };
        world.Settlements[home] = new SettlementStub(
            FounderId: new EntityId(1), CivId: civId, Tile: home, FoundedYear: 0,
            Population: 100, Health: 100, Name: "Home", ResourceStores: homeStores);

        world.Settlements[destA] = new SettlementStub(
            FounderId: new EntityId(2), CivId: civId, Tile: destA, FoundedYear: 0,
            Population: 100, Health: 100, Name: "DestA",
            ResourceLedger: new Dictionary<string, float> { ["timber"] = 5.0f });
        world.Settlements[destB] = new SettlementStub(
            FounderId: new EntityId(3), CivId: civId, Tile: destB, FoundedYear: 0,
            Population: 100, Health: 100, Name: "DestB",
            ResourceLedger: new Dictionary<string, float> { ["copper"] = 0.05f });

        var merchant = new Tier2Character(
            id: new EntityId(9002), location: home, name: "Merchant",
            personality: PersonalityVector6.Default,
            livelihood: new LivelihoodData(Tier2Role.Merchant, null, home, 0.5f),
            maxHealth: 100, maxAgeSeason: 1000);
        world.Entities.Add(merchant);

        var phase = new Tier2BehaviorPhase(world.SimConfig);
        phase.Execute(world, tick: 0);

        // Copper should have moved (destB gained copper), not timber, despite the smaller raw surplus.
        world.Settlements[destB].GetStore("copper").Should().BeGreaterThan(0f,
            "demand-weighted routing should prefer the deeply-deficient destB/copper pair");
    }

    // ─── Local helpers ─────────────────────────────────────────────────────────

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
