using System.Text.Json;
using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Economy;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M14 14.2 — persistent trade routes / caravan travel-time simulation. Covers: route formation
/// after EconomyConfig.TradeRouteFormationThreshold successful RunMerchant trades (and not before),
/// caravan transit duration + arrival resolution reusing 14.1's pricing/home-cut/Wealth-credit
/// mechanics, distributional interception/disaster/piracy roll rates, war severance + reopening,
/// TradeRoute/Caravan DTO round-trip, and an integration check that a route actually forms and a
/// caravan actually completes transit within a reasonable tick budget (the same "verify the
/// mechanic actually fires" discipline used for M13.8's Estrangement/OathBroken fix and M14 14.0/
/// 14.1). See docs/phases/m14_economy_independent_wealth.md.
/// </summary>
public class TradeRouteCaravanTests : IDisposable
{
    private readonly string _saveDir = Path.Combine(Path.GetTempPath(), $"m14_2_traderoute_test_{Guid.NewGuid():N}");

    public void Dispose() => WorldStateSaver.DeleteSave(_saveDir);

    private static TileCoord FindLandTile(WorldState world, TileCoord? exclude = null, int minDist = 0)
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
            return c;
        }
        throw new InvalidOperationException("No suitable land tile found");
    }

    /// <summary>
    /// Same two-settlement + one-merchant shape as MerchantTradeWealthTests.BuildTradeWorld: home
    /// has a surplus of exactly one resource so RunMerchant's routing deterministically picks it
    /// and the sole other settlement as the trade destination, and MerchantTradeChance is forced
    /// so the trade always fires. Optionally places the two settlements under distinct,
    /// war-capable civs for the severance tests.
    /// </summary>
    private static (WorldState world, TileCoord home, TileCoord dest, Tier2Character merchant,
        CivId homeCiv, CivId destCiv) BuildRouteWorld(
        float homeIron, float destGold, bool distinctCivs = false)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 4242);
        world.SimConfig.Character.MerchantTradeChance = 1f;
        // M14 14.4 — this suite tests 14.2's route-formation/caravan mechanics in isolation over
        // many repeated trades against the same merchant identity; keep the orthogonal
        // Guild-formation mechanic from promoting the merchant away mid-run (see GuildTreasuryTests
        // for Guild-formation's own coverage).
        world.SimConfig.Economy.GuildFormationWealthThreshold = float.MaxValue;

        var home = FindLandTile(world);
        var dest = FindLandTile(world, exclude: home, minDist: 3);

        var homeCiv = distinctCivs ? new CivId(1) : CivId.None;
        var destCiv = distinctCivs ? new CivId(2) : CivId.None;

        if (distinctCivs)
        {
            world.Civilizations[homeCiv] = new Civilization(homeCiv, "HomeCiv", new EntityId(101), home, 0);
            world.Civilizations[destCiv] = new Civilization(destCiv, "DestCiv", new EntityId(102), dest, 0);
        }

        world.Settlements[home] = new SettlementStub(
            FounderId: new EntityId(1), CivId: homeCiv, Tile: home, FoundedYear: 0,
            Population: 100, Health: 100, Name: "Home",
            ResourceStores: new Dictionary<string, float> { ["iron"] = homeIron });

        world.Settlements[dest] = new SettlementStub(
            FounderId: new EntityId(2), CivId: destCiv, Tile: dest, FoundedYear: 0,
            Population: 100, Health: 100, Name: "Dest",
            ResourceStores: new Dictionary<string, float> { ["gold"] = destGold });

        var merchant = new Tier2Character(
            id: new EntityId(9001), location: home, name: "Merchant",
            personality: PersonalityVector6.Default,
            livelihood: new LivelihoodData(Tier2Role.Merchant, null, home, 0.5f),
            maxHealth: 100, maxAgeSeason: 1000);
        world.Entities.Add(merchant);

        return (world, home, dest, merchant, homeCiv, destCiv);
    }

    private static string? ExtractCause(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return doc.RootElement.TryGetProperty("Cause", out var el) ? el.GetString() : null;
    }

    private static long ExpectedCaravanDuration(TileCoord a, TileCoord b, WorldState world)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        float speedTilesPerTick = world.SimConfig.Economy.CaravanSpeedTilesPerYear
                                  / world.SimConfig.SimLoop.TicksPerYear;
        return Math.Max(1L, (long)Math.Ceiling(dist / speedTilesPerTick));
    }

    // ─── Route formation ────────────────────────────────────────────────────────────────────

    [Fact]
    public void RunMerchant_FormsTradeRoute_AfterFormationThreshold_NotBefore()
    {
        var (world, home, dest, _, _, _) = BuildRouteWorld(homeIron: 10_000f, destGold: 10_000f);
        world.SimConfig.Economy.TradeRouteFormationThreshold = 3;
        var key = TradeRouteKey.Of(home, dest);

        var phase = new Tier2BehaviorPhase(world.SimConfig);

        phase.Execute(world, tick: 0);
        world.TradeRoutes.Should().NotContainKey(key, "one successful trade is below the formation threshold");

        phase.Execute(world, tick: 1);
        world.TradeRoutes.Should().NotContainKey(key, "two successful trades are still below the formation threshold");

        var pending = phase.Execute(world, tick: 2);
        world.TradeRoutes.Should().ContainKey(key, "the third successful trade should graduate the pair into a persistent TradeRoute");
        world.TradeRoutes[key].Status.Should().Be(TradeRouteStatus.Active);
        pending.Should().Contain(pe => pe.Type == Sim.Core.EventType.TradeRouteFormed);
    }

    // ─── Caravan transit duration + arrival resolution ─────────────────────────────────────────

    [Fact]
    public void ActiveRoute_CommitsCaravan_ArrivesAfterExpectedTicks_AndPaysLikeInstantTrade()
    {
        var (world, home, dest, merchant, _, _) = BuildRouteWorld(homeIron: 10_000f, destGold: 10_000f);
        world.GlobalPriceIndex = 0.5f;
        var cfg = world.SimConfig.Economy;
        cfg.CaravanInterceptionChance = 0f;
        cfg.CaravanDisasterChance = 0f;
        cfg.CaravanPiracyChance = 0f;
        var key = TradeRouteKey.Of(home, dest);
        world.TradeRoutes[key] = new TradeRoute(key, world.CurrentYear); // pre-formed, skip the counter

        var phase = new Tier2BehaviorPhase(world.SimConfig);

        // Dispatch: RunMerchant commits the first trade to the caravan slot instead of resolving instantly.
        phase.Execute(world, tick: 0);
        world.TradeRoutes[key].InTransit.Should().NotBeNull("an active route should commit the trade to a caravan instead of paying instantly");
        var caravan = world.TradeRoutes[key].InTransit!;
        merchant.Wealth.Should().Be(0f, "payment must not happen at dispatch — only at arrival");

        long expectedDuration = ExpectedCaravanDuration(home, dest, world);
        caravan.ArrivalTick.Should().Be(0 + expectedDuration);

        // One tick before arrival: still in transit, no payment yet.
        if (expectedDuration > 1)
        {
            phase.Execute(world, tick: caravan.ArrivalTick - 1);
            world.TradeRoutes[key].InTransit.Should().NotBeNull("the caravan should not resolve before its ArrivalTick");
            merchant.Wealth.Should().Be(0f);
        }

        // Arrival tick: resolves, reusing 14.1's pricing/home-cut/Wealth-credit mechanics.
        var arrivalPending = phase.Execute(world, tick: caravan.ArrivalTick);
        world.TradeRoutes[key].InTransit.Should().BeNull("the caravan slot should clear once resolved");
        merchant.Wealth.Should().BeGreaterThan(0f, "a successful arrival should pay the merchant exactly like the instant one-shot path");

        float unitPrice = cfg.GetBaseValue("iron") * 1f /* neutral scarcity, no dest ledger entry */ * world.GlobalPriceIndex;
        float totalValue = unitPrice * caravan.Quantity;
        float expectedMerchantShare = totalValue * (1f - cfg.MerchantHomeCutFraction);
        merchant.Wealth.Should().BeApproximately(expectedMerchantShare, 0.01f);

        world.Settlements[dest].GetStore("iron").Should().BeApproximately(caravan.Quantity, 0.01f,
            "the physical goods should be delivered to the destination on arrival, not at dispatch");

        arrivalPending.Should().Contain(pe => pe.Type == Sim.Core.EventType.TradePaid);
    }

    // ─── Distributional: interception / disaster / piracy roll rates ──────────────────────────

    private static (int lostWithCause, int total) RunCaravanTrials(
        WorldState world, TileCoord home, TileCoord dest, TradeRouteKey key, string expectedCause, int trials)
    {
        int lostWithCause = 0;
        var phase = new Tier2BehaviorPhase(world.SimConfig);

        for (int i = 0; i < trials; i++)
        {
            long tick = 1000 + i; // distinct tick each trial so WorldRng (keyed off WorldState.CurrentTick) varies
            world.CurrentTick = tick;
            world.TradeRoutes[key] = new TradeRoute(key, world.CurrentYear)
            {
                InTransit = new Caravan(new EntityId(9001), home, dest, "iron", 5f, tick, tick)
            };

            var pending = phase.Execute(world, tick: tick);
            var raided = pending.FirstOrDefault(pe => pe.Type == Sim.Core.EventType.CaravanRaided);
            if (raided is not null && ExtractCause(raided.PayloadJson) == expectedCause)
                lostWithCause++;
        }

        return (lostWithCause, trials);
    }

    [Fact]
    public void CaravanInterception_FiresAtRoughlyConfiguredRate_WhenAtWar()
    {
        var (world, home, dest, _, homeCiv, destCiv) = BuildRouteWorld(homeIron: 10_000f, destGold: 10_000f, distinctCivs: true);
        world.Civilizations[homeCiv].WarsAgainst[destCiv] = world.CurrentYear;
        world.Civilizations[destCiv].WarsAgainst[homeCiv] = world.CurrentYear;
        world.SimConfig.Economy.CaravanInterceptionChance = 0.3f;
        world.SimConfig.Economy.CaravanDisasterChance = 0f;
        world.SimConfig.Economy.CaravanPiracyChance = 0f;
        // Large threshold so repeated losses across trials never trip severance mid-run.
        world.SimConfig.Economy.TradeRouteSeverThreshold = int.MaxValue;
        var key = TradeRouteKey.Of(home, dest);

        var (lost, total) = RunCaravanTrials(world, home, dest, key, "war", trials: 2000);
        float observedRate = (float)lost / total;

        observedRate.Should().BeInRange(0.3f * 0.6f, 0.3f * 1.4f,
            $"observed interception rate {observedRate:F3} over {total} trials should track the configured 0.3 chance");
    }

    [Fact]
    public void CaravanDisaster_FiresAtRoughlyConfiguredRate_RegardlessOfWar()
    {
        var (world, home, dest, _, _, _) = BuildRouteWorld(homeIron: 10_000f, destGold: 10_000f);
        world.SimConfig.Economy.CaravanInterceptionChance = 0f;
        world.SimConfig.Economy.CaravanDisasterChance = 0.15f;
        world.SimConfig.Economy.CaravanPiracyChance = 0f;
        world.SimConfig.Economy.TradeRouteSeverThreshold = int.MaxValue;
        var key = TradeRouteKey.Of(home, dest);

        var (lost, total) = RunCaravanTrials(world, home, dest, key, "disaster", trials: 2000);
        float observedRate = (float)lost / total;

        observedRate.Should().BeInRange(0.15f * 0.6f, 0.15f * 1.4f,
            $"observed disaster rate {observedRate:F3} over {total} trials should track the configured 0.15 chance");
    }

    [Fact]
    public void CaravanPiracy_FiresAtRoughlyConfiguredRate()
    {
        var (world, home, dest, _, _, _) = BuildRouteWorld(homeIron: 10_000f, destGold: 10_000f);
        world.SimConfig.Economy.CaravanInterceptionChance = 0f;
        world.SimConfig.Economy.CaravanDisasterChance = 0f;
        world.SimConfig.Economy.CaravanPiracyChance = 0.1f;
        world.SimConfig.Economy.TradeRouteSeverThreshold = int.MaxValue;
        var key = TradeRouteKey.Of(home, dest);

        var (lost, total) = RunCaravanTrials(world, home, dest, key, "piracy", trials: 2000);
        float observedRate = (float)lost / total;

        observedRate.Should().BeInRange(0.1f * 0.5f, 0.1f * 1.5f,
            $"observed piracy rate {observedRate:F3} over {total} trials should track the configured 0.1 chance");
    }

    // ─── Severance / reopening ──────────────────────────────────────────────────────────────

    [Fact]
    public void TradeRoute_SeversUnderWar_AndReopensAfterCooldown()
    {
        var (world, home, dest, _, homeCiv, destCiv) = BuildRouteWorld(homeIron: 10_000f, destGold: 10_000f, distinctCivs: true);
        var key = TradeRouteKey.Of(home, dest);
        world.TradeRoutes[key] = new TradeRoute(key, world.CurrentYear);
        int cooldown = world.SimConfig.Economy.TradeRouteReopenCooldownTicks;

        var phase = new Tier2BehaviorPhase(world.SimConfig);

        // Declare war between the endpoints' civs — the route should sever on the very next tick check.
        world.Civilizations[homeCiv].WarsAgainst[destCiv] = world.CurrentYear;
        world.Civilizations[destCiv].WarsAgainst[homeCiv] = world.CurrentYear;
        var severPending = phase.Execute(world, tick: 100);

        world.TradeRoutes[key].Status.Should().Be(TradeRouteStatus.Severed);
        severPending.Should().Contain(pe => pe.Type == Sim.Core.EventType.TradeRouteSevered
            && ExtractCause(pe.PayloadJson) == "war");

        // End the war but stay under the cooldown — should remain severed.
        world.Civilizations[homeCiv].WarsAgainst.Remove(destCiv);
        world.Civilizations[destCiv].WarsAgainst.Remove(homeCiv);
        phase.Execute(world, tick: 100 + cooldown - 1);
        world.TradeRoutes[key].Status.Should().Be(TradeRouteStatus.Severed,
            "the route should stay severed until the reopen cooldown has fully elapsed");

        // Cooldown elapsed — should reopen.
        var reopenPending = phase.Execute(world, tick: 100 + cooldown);
        world.TradeRoutes[key].Status.Should().Be(TradeRouteStatus.Active);
        reopenPending.Should().Contain(pe => pe.Type == Sim.Core.EventType.TradeRouteFormed);
    }

    // ─── Persistence: DTO round-trip ───────────────────────────────────────────────────────────

    [Fact]
    public void TradeRouteAndCaravan_RoundTripThroughSaveLoad()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 4243);
        var home = FindLandTile(world);
        var dest = FindLandTile(world, exclude: home, minDist: 3);
        var key = TradeRouteKey.Of(home, dest);

        var route = new TradeRoute(key, formedYear: 12)
        {
            Status = TradeRouteStatus.Severed,
            ConsecutiveCaravanLosses = 2,
            SeveredSinceTick = 555,
            InTransit = new Caravan(new EntityId(42), key.TileA, key.TileB, "gold", 7.5f, 100, 140)
        };
        world.TradeRoutes[key] = route;
        world.TradeRouteFormationProgress[TradeRouteKey.Of(home, dest)] = 2;

        WorldStateSaver.Save(world, _saveDir, world.SimConfig);
        var loaded = WorldStateSaver.Load(_saveDir, world.SimConfig);

        loaded.TradeRoutes.Should().ContainKey(key);
        var loadedRoute = loaded.TradeRoutes[key];
        loadedRoute.Status.Should().Be(TradeRouteStatus.Severed);
        loadedRoute.FormedYear.Should().Be(12);
        loadedRoute.ConsecutiveCaravanLosses.Should().Be(2);
        loadedRoute.SeveredSinceTick.Should().Be(555);
        loadedRoute.InTransit.Should().NotBeNull();
        loadedRoute.InTransit!.MerchantId.Should().Be(new EntityId(42));
        loadedRoute.InTransit!.Resource.Should().Be("gold");
        loadedRoute.InTransit!.Quantity.Should().BeApproximately(7.5f, 0.001f);
        loadedRoute.InTransit!.DepartTick.Should().Be(100);
        loadedRoute.InTransit!.ArrivalTick.Should().Be(140);

        loaded.TradeRouteFormationProgress.Should().ContainKey(key);
        loaded.TradeRouteFormationProgress[key].Should().Be(2);
    }

    // ─── Integration: the mechanic actually fires within a reasonable tick budget ──────────────

    [Fact]
    public void ShortRun_TradeRouteFormsAndCaravanCompletesTransit_WithinTickBudget()
    {
        var (world, home, dest, merchant, _, _) = BuildRouteWorld(homeIron: 100_000f, destGold: 100_000f);
        world.SimConfig.Economy.TradeRouteFormationThreshold = 3;
        world.SimConfig.Economy.CaravanInterceptionChance = 0f;
        world.SimConfig.Economy.CaravanDisasterChance = 0f;
        world.SimConfig.Economy.CaravanPiracyChance = 0f;
        var key = TradeRouteKey.Of(home, dest);

        var phase = new Tier2BehaviorPhase(world.SimConfig);

        bool routeFormed = false;
        bool caravanCompleted = false;
        const int tickBudget = 500;

        for (long tick = 0; tick < tickBudget; tick++)
        {
            world.CurrentTick = tick;
            var pending = phase.Execute(world, tick);

            if (!routeFormed && world.TradeRoutes.ContainsKey(key))
                routeFormed = true;

            if (routeFormed && pending.Any(pe => pe.Type == Sim.Core.EventType.TradePaid))
            {
                caravanCompleted = true;
                break;
            }
        }

        routeFormed.Should().BeTrue($"a TradeRoute should form within {tickBudget} ticks given a forced trade chance and ample resources");
        caravanCompleted.Should().BeTrue($"a caravan dispatched on the formed route should complete transit within {tickBudget} ticks");
        merchant.Wealth.Should().BeGreaterThan(0f, "the completed caravan transit should have paid the merchant");
    }
}
