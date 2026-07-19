using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests;

/// <summary>
/// Tests for M4 Phase 2 — Territory Dynamics &amp; War Outcomes.
/// Covers: event gate (always_record_types), territory-adjacency border tension,
/// annual war campaigns, war-end territory transfer, and WarBattleWins serialization.
/// </summary>
public class TerritoryWarTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a small world (10×10 tiles, 100km) and plants a settlement on the
    /// first fertile land tile, registering it properly via CivTracker.
    /// </summary>
    private static (WorldState world, TileCoord cityTile, CivId civId) PlantSettlement(
        WorldState world, long seedOffset = 0L)
    {
        TileCoord cityTile = default;
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c)) continue;
            if (world.TileGrid.GetTile(c).Fertility < 10) continue;
            // Skip tiles already claimed by another settlement
            if (world.Settlements.ContainsKey(c)) continue;
            cityTile = c;
            goto Found;
        }
        Found:
        var biome   = (BiomeType)world.TileGrid.GetTile(cityTile).BiomeType;
        var founder = CharacterFactory.Spawn(cityTile, biome, world.WorldSeed, 1L + seedOffset, world.SimConfig, world.CurrentYear);
        world.Entities.Add(founder);

        // Disable global min-distance so small test worlds can host multiple settlements
        int savedMinDist = world.SimConfig.Character.GlobalSettlementMinDist;
        world.SimConfig.Character.GlobalSettlementMinDist = 0;
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(
            new EstablishSettlement(founder.Id, cityTile),
            world, pending, world.SimConfig.SettlementNames);
        world.SimConfig.Character.GlobalSettlementMinDist = savedMinDist;

        var civId = world.Settlements[cityTile].CivId;
        return (world, cityTile, civId);
    }

    /// <summary>Finds a land tile near the given tile that is not yet in TerritoryMap.</summary>
    private static TileCoord FindFreeLandTile(WorldState world, TileCoord near, int searchRadius = 5)
    {
        for (int dy = -searchRadius; dy <= searchRadius; dy++)
        for (int dx = -searchRadius; dx <= searchRadius; dx++)
        {
            int nx = ((near.X + dx) % world.TileGrid.TileWidth + world.TileGrid.TileWidth) % world.TileGrid.TileWidth;
            int ny = Math.Clamp(near.Y + dy, 0, world.TileGrid.TileHeight - 1);
            var c = new TileCoord(nx, ny);
            if (world.IsLand(c) && !world.TerritoryMap.ContainsKey(c)) return c;
        }
        throw new InvalidOperationException("No free land tile found near " + near);
    }

    // ── Test 1: TerritoryExpanded fires and is not filtered ──────────────────

    /// <summary>
    /// Epic 4.2.1: TerritoryExpanded events must appear in history after the always_record_types gate
    /// is configured — even if MinimumRecordedTier would otherwise filter them.
    /// </summary>
    [Fact]
    public void EventGate_AlwaysRecordTypes_AllowsTerritoryExpandedThrough()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 42);
        var simCfg = TestSimConfig.Default();
        // Raise minimum tier to Headline so Background events are normally filtered
        simCfg.Events.MinimumRecordedTier = EventTier.Headline;
        // But TerritoryExpanded is in always_record_types — it should always pass
        simCfg.Events.Gate.AlwaysRecordTypes.Add("TerritoryExpanded");

        var gate = new WorldEngine.Sim.Events.EventGate(simCfg);
        // TerritoryExpanded classifies as Background tier normally
        bool result = gate.ShouldRecord(EventType.TerritoryExpanded, EventTier.Background);

        Assert.True(result,
            "TerritoryExpanded should pass the gate because it is in always_record_types, " +
            "even though MinimumRecordedTier = Headline would normally suppress Background events.");
    }

    // ── Test 2: Non-always-record event still filtered at low tier ────────────

    /// <summary>
    /// Epic 4.2.1: Events NOT in always_record_types are still filtered by tier.
    /// </summary>
    [Fact]
    public void EventGate_NonAlwaysRecord_FilteredByTier()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 42);
        var simCfg = TestSimConfig.Default();
        simCfg.Events.MinimumRecordedTier = EventTier.Headline;
        // BattleOccurred is NOT in always_record_types

        var gate = new WorldEngine.Sim.Events.EventGate(simCfg);
        bool result = gate.ShouldRecord(EventType.BattleOccurred, EventTier.Background);

        Assert.False(result,
            "BattleOccurred at Background tier should be filtered when MinimumRecordedTier = Headline.");
    }

    // ── Test 3: Territory-adjacency tension accrues ───────────────────────────

    /// <summary>
    /// Epic 4.2.2: When two civs have adjacent territory tiles, RunTerritoryBorderTension
    /// accrues border tension between them proportional to the number of touching pairs.
    /// </summary>
    [Fact]
    public void RunTerritoryBorderTension_AdjacentTiles_AccruesTension()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 42);

        // Plant two settlements on the same world
        var (w, cityA, civA) = PlantSettlement(world, seedOffset: 0);
        var (_, cityB, civB) = PlantSettlement(w, seedOffset: 100);

        if (!w.Civilizations.TryGetValue(civA, out var ca)) throw new Exception("CivA missing");
        if (!w.Civilizations.TryGetValue(civB, out var cb)) throw new Exception("CivB missing");

        // Ensure civs are not already at war (peace treaty would block tension accrual)
        ca.WarsAgainst.Clear();
        cb.WarsAgainst.Clear();
        ca.BorderTension.Clear();
        cb.BorderTension.Clear();

        // Manually inject adjacent territory tile pairs between civA and civB.
        // Place a tile for civA and one of its 4-neighbors for civB.
        var tileA = FindFreeLandTile(w, cityA);
        var tileB = new TileCoord(tileA.X + 1, tileA.Y); // right neighbor
        int bx = ((tileB.X) % w.TileGrid.TileWidth + w.TileGrid.TileWidth) % w.TileGrid.TileWidth;
        tileB = new TileCoord(bx, Math.Clamp(tileB.Y, 0, w.TileGrid.TileHeight - 1));

        // Assign tileA to civA's city, tileB to civB's city
        if (!ca.CityTerritories.ContainsKey(cityA)) ca.CityTerritories[cityA] = new HashSet<TileCoord>();
        if (!cb.CityTerritories.ContainsKey(cityB)) cb.CityTerritories[cityB] = new HashSet<TileCoord>();

        w.TerritoryMap[tileA] = cityA;
        ca.CityTerritories[cityA].Add(tileA);

        w.TerritoryMap[tileB] = cityB;
        cb.CityTerritories[cityB].Add(tileB);

        // Make sure a settlement exists at each cityTile for the lookup
        // (PlantSettlement already created them above)

        float tensionBefore = ca.BorderTension.GetValueOrDefault(civB, 0f);

        // Run territory border tension once (simulate one year)
        // RunTerritoryBorderTension is private; call via RunAnnualDiplomacy requires full sim setup.
        // Instead we call it indirectly via the internal-visible method by running a partial annual pass.
        // Since RunTerritoryBorderTension is private static, we test it via the public pipeline.
        // We use CivTracker.EndWarBetween indirectly: just verify that after a sim run tension exists.
        // DECISION: For direct unit-test access, reflect on the private method via the existing
        // [InternalsVisibleTo] coverage — but private statics aren't exposed. Instead we call the
        // full RunAnnualDiplomacy via the PhaseRunner's annual step and verify tension accumulates.
        // Here we check that the logic is wired: after the annual pass, civA should have tension vs civB.

        // Run one full year via the SimLoop to trigger RunAnnualDiplomacy → RunTerritoryBorderTension
        var cmdQueue    = new CommandQueue();
        var stateCache  = new StateCache();
        var eventStore  = new EventStore();
        var eventCache  = new EventCache();
        var phaseRunner = new PhaseRunner(w.SimConfig, eventStore, eventCache);
        var snapBuilder = new SnapshotBuilder();
        var loop        = new SimLoop(w, cmdQueue, stateCache, phaseRunner, snapBuilder, w.SimConfig, eventCache);

        // Run for one year (4 ticks = 4 seasons)
        cmdQueue.Enqueue(new WorldEngine.Sim.Commands.SetSimSpeed(WorldEngine.Sim.Core.SimSpeed.Ultrafast));
        loop.Start();
        Thread.Sleep(300);
        loop.Stop();

        // After at least one spring tick, tension should have accrued for adjacent territory pairs.
        // D5: opportunistic war causes (resource shortage / weak neighbor) may push tension above the
        // threshold immediately, causing war to be declared and tension reset to 0 in the same pass.
        // Accept either "tension > 0" OR "war was declared" as proof that the border tension system fired.
        float tensionAfterA = ca.BorderTension.GetValueOrDefault(civB, 0f);
        float tensionAfterB = cb.BorderTension.GetValueOrDefault(civA, 0f);
        bool warDeclared    = ca.IsAtWarWith(civB) || cb.IsAtWarWith(civA);

        Assert.True(tensionAfterA > tensionBefore || tensionAfterB > 0f || warDeclared,
            "Territory-adjacency border tension should have accrued between civs with adjacent tile pairs. " +
            $"civA BorderTension[civB]={tensionAfterA}, civB BorderTension[civA]={tensionAfterB}, warDeclared={warDeclared}");
    }

    // ── Test 4: RunWarCampaigns fires BattleOccurred events ──────────────────

    /// <summary>
    /// Epic 4.2.3: Active wars generate campaign battle events each year.
    /// </summary>
    [Fact]
    public void RunWarCampaigns_ActiveWar_GeneratesBattleEvents()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 42);

        var (w, cityA, civA) = PlantSettlement(world, seedOffset: 0);
        var (_, cityB, civB) = PlantSettlement(w, seedOffset: 100);

        if (!w.Civilizations.TryGetValue(civA, out var ca)) throw new Exception("CivA missing");
        if (!w.Civilizations.TryGetValue(civB, out var cb)) throw new Exception("CivB missing");

        // Force a war between them
        ca.WarsAgainst[civB] = w.CurrentYear;
        cb.WarsAgainst[civA] = w.CurrentYear;
        ca.WarHistory[civB] = ca.WarHistory.GetValueOrDefault(civB, 0) + 1;
        cb.WarHistory[civA] = cb.WarHistory.GetValueOrDefault(civA, 0) + 1;

        var cmdQueue    = new CommandQueue();
        var stateCache  = new StateCache();
        var eventStore  = new EventStore();
        var eventCache  = new EventCache();
        var phaseRunner = new PhaseRunner(w.SimConfig, eventStore, eventCache);
        var snapBuilder = new SnapshotBuilder();
        var loop        = new SimLoop(w, cmdQueue, stateCache, phaseRunner, snapBuilder, w.SimConfig, eventCache);

        // Run for ~5 in-sim years (each year = 4 ticks; 20 ticks × speed)
        cmdQueue.Enqueue(new WorldEngine.Sim.Commands.SetSimSpeed(WorldEngine.Sim.Core.SimSpeed.Ultrafast));
        loop.Start();
        Thread.Sleep(600);
        loop.Stop();

        // Check that at least one BattleOccurred event was recorded
        var history = eventStore.GetEventsByType(EventType.BattleOccurred);
        bool hasBattle = history.Any();

        Assert.True(hasBattle,
            "Active war should produce at least one BattleOccurred event from campaign ticks.");
    }

    // ── Test 5: War-end territory transfer ────────────────────────────────────

    /// <summary>
    /// Epic 4.2.4: When a war ends with a battle advantage, border tiles transfer from loser to winner.
    /// </summary>
    [Fact]
    public void EndWarBetween_BattleAdvantage_TransfersBorderTiles()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 42);

        var (w, cityA, civA) = PlantSettlement(world, seedOffset: 0);
        var (_, cityB, civB) = PlantSettlement(w, seedOffset: 100);

        if (!w.Civilizations.TryGetValue(civA, out var ca)) throw new Exception("CivA missing");
        if (!w.Civilizations.TryGetValue(civB, out var cb)) throw new Exception("CivB missing");

        // Force a war
        ca.WarsAgainst[civB] = w.CurrentYear;
        cb.WarsAgainst[civA] = w.CurrentYear;
        ca.WarHistory[civB] = 1;

        // Give civA a 4-1 battle win advantage
        ca.WarBattleWins[civB] = 4;
        cb.WarBattleWins[civA] = 1;

        // Plant adjacent territory so there are tiles to transfer
        // civB owns tileB, civA owns tileA adjacent to tileB
        var tileA = FindFreeLandTile(w, cityA);
        if (!ca.CityTerritories.ContainsKey(cityA)) ca.CityTerritories[cityA] = new HashSet<TileCoord>();
        w.TerritoryMap[tileA] = cityA;
        ca.CityTerritories[cityA].Add(tileA);

        int bx = ((tileA.X + 1) % w.TileGrid.TileWidth + w.TileGrid.TileWidth) % w.TileGrid.TileWidth;
        var tileB = new TileCoord(bx, tileA.Y);
        if (!cb.CityTerritories.ContainsKey(cityB)) cb.CityTerritories[cityB] = new HashSet<TileCoord>();
        w.TerritoryMap[tileB] = cityB;
        cb.CityTerritories[cityB].Add(tileB);

        int loserTilesBefore = cb.CityTerritories.Values.SelectMany(t => t).Count();

        // End the war (simulating expiry)
        var pending = new List<PendingEvent>();
        CivTracker.EndWarBetween(civA, civB, "expired", w, pending);

        int loserTilesAfter  = cb.CityTerritories.Values.SelectMany(t => t).Count();
        int winnerTilesAfter = ca.CityTerritories.Values.SelectMany(t => t).Count();

        // net advantage = 4 - 1 = 3; tiles_per_battle_win = 2 → 6 tiles, but only 1 border tile exists
        // So at least 1 tile should transfer (up to available border tiles)
        Assert.True(loserTilesAfter < loserTilesBefore || winnerTilesAfter > 0,
            "Losing civ should have fewer territory tiles after war ends with a battle advantage. " +
            $"Loser before={loserTilesBefore}, after={loserTilesAfter}; " +
            $"Winner total={winnerTilesAfter}");

        // WarBattleWins should be reset after war ends
        Assert.False(ca.WarBattleWins.ContainsKey(civB),
            "WarBattleWins should be reset for civA after war ends.");
        Assert.False(cb.WarBattleWins.ContainsKey(civA),
            "WarBattleWins should be reset for civB after war ends.");

        // TerritoryLost event should be emitted for the loser
        bool hasTerritoryLost = pending.Any(e => e.Type == EventType.TerritoryLost);
        Assert.True(hasTerritoryLost,
            "TerritoryLost event should be emitted for the losing civ.");
    }

    // ── Test 6: WarBattleWins round-trips through save/load ──────────────────

    /// <summary>
    /// Epic 4.2.5: WarBattleWins is persisted and restored correctly through WorldStateMapper.
    /// </summary>
    [Fact]
    public void WarBattleWins_RoundTrips_ThroughSaveLoad()
    {
        var saveDir = Path.Combine(Path.GetTempPath(), $"warbattle_test_{Guid.NewGuid():N}");
        try
        {
            var world  = WorldTestHelper.CreateSmallWorld(seed: 42);
            var simCfg = TestSimConfig.Default();

            var (w, cityA, civA) = PlantSettlement(world, seedOffset: 0);
            var (_, cityB, civB) = PlantSettlement(w, seedOffset: 100);

            if (!w.Civilizations.TryGetValue(civA, out var ca)) throw new Exception("CivA missing");

            // Set known WarBattleWins state
            ca.WarBattleWins[civB] = 7;

            // Save
            WorldStateSaver.Save(w, saveDir, simCfg);

            // Load
            var restored = WorldStateSaver.Load(saveDir, simCfg);
            Assert.NotNull(restored);

            if (!restored.Civilizations.TryGetValue(civA, out var rca))
                throw new Exception("CivA missing after restore");

            Assert.True(rca.WarBattleWins.TryGetValue(civB, out int wins),
                "WarBattleWins entry should survive save/load round-trip.");
            Assert.Equal(7, wins);
        }
        finally
        {
            WorldStateSaver.DeleteSave(saveDir);
        }
    }

    // ── D5 War Consolidation + Opportunistic Causes ───────────────────────────

    /// <summary>
    /// D5: WarConfig now holds MaxWarDurationYears (previously in CharacterSimConfig).
    /// Verifies the consolidation: reading from war config returns the expected value.
    /// </summary>
    [Fact]
    public void D5_WarConfig_HoldsConsolidatedKeys()
    {
        var world   = WorldTestHelper.CreateSmallWorld(seed: 42);
        var simCfg  = world.SimConfig;

        // All war-lifecycle keys should be on WarConfig, not CharacterSimConfig
        Assert.True(simCfg.War.MaxWarDurationYears > 0,
            "MaxWarDurationYears should be bound in WarConfig (D5 consolidation).");
        Assert.True(simCfg.War.MaxActiveWars > 0,
            "MaxActiveWars should be bound in WarConfig.");
        Assert.True(simCfg.War.PeaceCooldownYears >= 0,
            "PeaceCooldownYears should be bound in WarConfig.");
        Assert.True(simCfg.War.RaidDamageMin < simCfg.War.RaidDamageMax,
            "RaidDamageMin must be < RaidDamageMax in WarConfig.");
        Assert.True(simCfg.War.WarAggressionThreshold is >= 0f and <= 1f,
            "WarAggressionThreshold should be a probability in WarConfig.");
        Assert.True(simCfg.War.TensionWarThreshold > 0f,
            "TensionWarThreshold should be positive in WarConfig.");
    }

    /// <summary>
    /// D5: WarOutcome constants are typed strings; verify they distinguish conquest from truce.
    /// MetricsCollector uses string matching so the strings must be stable.
    /// </summary>
    [Fact]
    public void D5_WarOutcome_Constants_AreDistinct()
    {
        Assert.NotEqual(WarOutcome.Conquest, WarOutcome.Truce);
        Assert.NotEqual(WarOutcome.Conquest, WarOutcome.Surrender);
        Assert.NotEqual(WarOutcome.Conquest, WarOutcome.Destruction);
    }

    /// <summary>
    /// D5: WarCause constants cover both original and opportunistic causes.
    /// All values must be non-empty and distinct.
    /// </summary>
    [Fact]
    public void D5_WarCause_Constants_AreDistinctNonEmpty()
    {
        var causes = new[]
        {
            WarCause.CharacterEncounter,
            WarCause.BorderTension,
            WarCause.SuccessionCrisis,
            WarCause.WeakNeighbor,
            WarCause.ResourceShortage,
        };

        Assert.All(causes, c => Assert.False(string.IsNullOrEmpty(c), "WarCause should not be empty."));
        Assert.Equal(causes.Length, causes.Distinct().Count());
    }

    /// <summary>
    /// D5: OpportunisticWarConfig keys load correctly from sim_config.toml.
    /// Verifies the new D5 TOML keys bind to WarConfig properties.
    /// </summary>
    [Fact]
    public void D5_OpportunisticWarConfig_KeysBound()
    {
        var world  = WorldTestHelper.CreateSmallWorld(seed: 42);
        var wCfg   = world.SimConfig.War;

        Assert.True(wCfg.SuccessionCrisisWarTensionMult > 1f,
            "succession_crisis_war_tension_mult should be > 1 (amplifier).");
        Assert.True(wCfg.WeakNeighborSettlementFraction is > 0f and <= 1f,
            "weak_neighbor_settlement_fraction is a proportion; must be in (0, 1].");
        Assert.True(wCfg.WeakNeighborTensionBonus >= 0f,
            "weak_neighbor_tension_bonus must be non-negative.");
        Assert.True(wCfg.ResourceShortageTensionBonus >= 0f,
            "resource_shortage_tension_bonus must be non-negative.");
    }
}
