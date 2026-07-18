using Tomlyn;
using Tomlyn.Model;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Balance;

/// <summary>
/// Balance regression harness — runs the sim for multiple seeds and asserts world-health bands
/// from <c>config/balance_invariants.toml</c>.
///
/// This suite is excluded from the fast test run; run it explicitly or nightly:
///   dotnet test --filter "Category=Balance"
///
/// See scripts/test-balance.sh for the convenience wrapper.
/// See docs/balance_invariants.md for the philosophy and update procedure.
/// </summary>
[Trait("Category", "Balance")]
public class BalanceRegressionTests
{
    // Seeds chosen to be a small representative set.
    // Calibration was run with {42, 777, 9999}; use 2 here to keep nightly runtime
    // under ~4 minutes (~100s per seed at 300 years, Release build).
    private static readonly int[] Seeds = [42, 9999];
    private const int Years = 300;

    /// <summary>
    /// Runs 2 seeds × 300 years and asserts all year-300 bands from balance_invariants.toml.
    /// Failures print all offending metrics (aggregate, not stop-at-first).
    /// </summary>
    [Fact]
    public void Year300_AllBandsGreen()
    {
        var invariants = LoadInvariants();
        var failures   = new List<string>();

        foreach (int seed in Seeds)
        {
            var metrics = RunAndGetYear300Row(seed);

            // ── active_civs ────────────────────────────────────────────────────
            CheckRange(failures, $"seed={seed} active_civs", metrics.ActiveCivs,
                invariants.Y300ActiveCivsMin, invariants.Y300ActiveCivsMax);

            // ── world_population ──────────────────────────────────────────────
            CheckRange(failures, $"seed={seed} world_population", metrics.WorldPopulation,
                invariants.Y300PopMin, invariants.Y300PopMax);

            // ── settlements_total ─────────────────────────────────────────────
            CheckRange(failures, $"seed={seed} settlements_total", metrics.SettlementsTotal,
                invariants.Y300SettlementsMin, invariants.Y300SettlementsMax);

            // ── mean_food_ratio ───────────────────────────────────────────────
            CheckRange(failures, $"seed={seed} mean_food_ratio", (double)metrics.MeanFoodRatio,
                invariants.Y300FoodRatioMin, invariants.Y300FoodRatioMax);

            // ── mean_wellbeing ────────────────────────────────────────────────
            CheckRange(failures, $"seed={seed} mean_wellbeing", (double)metrics.MeanWellbeing,
                invariants.Y300WellbeingMin, invariants.Y300WellbeingMax);

            // ── active_diseases ≤ max ─────────────────────────────────────────
            if (metrics.ActiveDiseases > invariants.Y300ActiveDiseasesMax)
                failures.Add($"seed={seed} active_diseases={metrics.ActiveDiseases} exceeds max={invariants.Y300ActiveDiseasesMax}");

            // ── goals_formed_ytd ≥ min ────────────────────────────────────────
            if (metrics.GoalsFormedYtd < invariants.Y300GoalsFormedYtdMin)
                failures.Add($"seed={seed} goals_formed_ytd={metrics.GoalsFormedYtd} below min={invariants.Y300GoalsFormedYtdMin}");

            // ── wars_active ≤ max ─────────────────────────────────────────────
            if (metrics.WarsActive > invariants.Y300WarsActiveMax)
                failures.Add($"seed={seed} wars_active={metrics.WarsActive} exceeds max={invariants.Y300WarsActiveMax}");
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                $"Balance regression: {failures.Count} band violation(s):\n" +
                string.Join("\n", failures.Select(f => $"  • {f}")));
        }
    }

    /// <summary>
    /// Asserts the cumulative goals-formed total over 300 years exceeds the minimum.
    /// Guards against the A5-class regression where goal formation silently drops to 0.
    /// </summary>
    [Fact]
    public void Year300_CumulativeGoalsFormedAboveFloor()
    {
        var invariants = LoadInvariants();
        var failures   = new List<string>();

        foreach (int seed in Seeds)
        {
            long totalGoals = RunAndGetCumulativeGoalsFormed(seed);
            if (totalGoals < invariants.Y300CumulativeGoalsMin)
                failures.Add($"seed={seed} cumulative goals_formed={totalGoals} below min={invariants.Y300CumulativeGoalsMin}");
        }

        if (failures.Count > 0)
            Assert.Fail($"Cumulative goal regression:\n" + string.Join("\n", failures.Select(f => $"  • {f}")));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void CheckRange(List<string> failures, string label, double value, double min, double max)
    {
        if (value < min || value > max)
            failures.Add($"{label}={value:F2} outside [{min:F2}, {max:F2}]");
    }

    private static YearlyMetricsRow RunAndGetYear300Row(int seed)
    {
        var (_, eventStore) = RunSim(seed);
        var row = eventStore.GetMetricsRowForYear(Years);
        eventStore.Dispose();
        return row ?? throw new InvalidOperationException(
            $"No metrics row found for year {Years} (seed {seed})");
    }

    private static long RunAndGetCumulativeGoalsFormed(int seed)
    {
        var (_, eventStore) = RunSim(seed);
        var rows = eventStore.GetAllMetricsRows();
        long total = rows.Sum(r => (long)r.GoalsFormedYtd);
        eventStore.Dispose();
        return total;
    }

    private static (WorldState world, EventStore eventStore) RunSim(int seed)
    {
        var cfg    = new WorldConfig { Seed = seed, WidthKm = 1000, HeightKm = 800, TileWidthKm = 10 };
        var simCfg = TestSimConfig.Default();
        var world  = new WorldGenPipeline().RunFullAsync(cfg, simCfg).GetAwaiter().GetResult();

        var eventStore   = new EventStore(":memory:");
        var eventCache   = new EventCache(simCfg.Events.RecentEventCacheSize);
        var gate         = new EventGate(simCfg);
        var beastCatalog = BeastCatalogLoader.LoadOrCreateDefault();
        var phaseRunner  = new PhaseRunner(simCfg, eventStore, eventCache, gate,
            beastCatalog: beastCatalog);

        foreach (var pe in BeastSpawner.SpawnAll(world, beastCatalog))  phaseRunner.InjectPendingEvent(pe);
        foreach (var pe in CharacterSpawner.SpawnAll(world, simCfg))    phaseRunner.InjectPendingEvent(pe);
        foreach (var pe in Tier2Spawner.SpawnAll(world, simCfg))        phaseRunner.InjectPendingEvent(pe);

        var cmdQueue        = new CommandQueue();
        var stateCache      = new StateCache();
        var snapshotBuilder = new SnapshotBuilder();
        var simLoop = new SimLoop(world, cmdQueue, stateCache, phaseRunner, snapshotBuilder, simCfg, eventCache);

        simLoop.RunSynchronous(Years * simCfg.SimLoop.TicksPerYear);
        phaseRunner.FlushPendingEvents(world);

        return (world, eventStore);
    }

    // ─── Invariant loading ────────────────────────────────────────────────────

    private static BalanceInvariants LoadInvariants()
    {
        // balance_invariants.toml is in config/ which is copied to test output as config/
        string path = Path.Combine(AppContext.BaseDirectory, "config", "balance_invariants.toml");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"balance_invariants.toml not found at {path}. " +
                "Ensure it is listed in WorldEngine.Tests.csproj Content items.");

        var toml = Toml.Parse(File.ReadAllText(path));
        var doc  = toml.ToModel();

        // Tomlyn nested tables: [year_300.active_civs] → doc["year_300"]["active_civs"]["min"]
        static TomlTable? GetSection(TomlTable root, string outer, string inner)
        {
            if (root[outer] is TomlTable outerT && outerT[inner] is TomlTable innerT)
                return innerT;
            return null;
        }

        static double GetDouble(TomlTable? t, string key, double def)
        {
            if (t is null) return def;
            if (t[key] is double d) return d;
            if (t[key] is long l) return (double)l;
            return def;
        }

        static long GetLong(TomlTable? t, string key, long def)
        {
            if (t is null) return def;
            if (t[key] is long l) return l;
            if (t[key] is double d) return (long)d;
            return def;
        }

        var y300 = doc["year_300"] as TomlTable;

        return new BalanceInvariants
        {
            Y300ActiveCivsMin      = (int)GetLong(GetSection(doc, "year_300", "active_civs"), "min", 2),
            Y300ActiveCivsMax      = (int)GetLong(GetSection(doc, "year_300", "active_civs"), "max", 12),
            Y300PopMin             = (int)GetLong(GetSection(doc, "year_300", "world_population"), "min", 4500),
            Y300PopMax             = (int)GetLong(GetSection(doc, "year_300", "world_population"), "max", 26000),
            Y300SettlementsMin     = (int)GetLong(GetSection(doc, "year_300", "settlements_total"), "min", 3),
            Y300SettlementsMax     = (int)GetLong(GetSection(doc, "year_300", "settlements_total"), "max", 18),
            Y300FoodRatioMin       = GetDouble(GetSection(doc, "year_300", "mean_food_ratio"), "min", 1.5),
            Y300FoodRatioMax       = GetDouble(GetSection(doc, "year_300", "mean_food_ratio"), "max", 200.0),
            Y300WellbeingMin       = GetDouble(GetSection(doc, "year_300", "mean_wellbeing"), "min", -0.6),
            Y300WellbeingMax       = GetDouble(GetSection(doc, "year_300", "mean_wellbeing"), "max", 0.5),
            Y300ActiveDiseasesMax  = (int)GetLong(GetSection(doc, "year_300", "active_diseases_max"), "value", 8),
            Y300GoalsFormedYtdMin  = (int)GetLong(GetSection(doc, "year_300", "goals_formed_ytd"), "min", 1),
            Y300WarsActiveMax      = (int)GetLong(GetSection(doc, "year_300", "wars_active_max"), "value", 5),
            Y300CumulativeGoalsMin = GetLong(GetSection(doc, "year_300", "goals_formed_cumulative_min"), "value", 500),
        };
    }

    private sealed class BalanceInvariants
    {
        public int    Y300ActiveCivsMin      { get; set; }
        public int    Y300ActiveCivsMax      { get; set; }
        public int    Y300PopMin             { get; set; }
        public int    Y300PopMax             { get; set; }
        public int    Y300SettlementsMin     { get; set; }
        public int    Y300SettlementsMax     { get; set; }
        public double Y300FoodRatioMin       { get; set; }
        public double Y300FoodRatioMax       { get; set; }
        public double Y300WellbeingMin       { get; set; }
        public double Y300WellbeingMax       { get; set; }
        public int    Y300ActiveDiseasesMax  { get; set; }
        public int    Y300GoalsFormedYtdMin  { get; set; }
        public int    Y300WarsActiveMax      { get; set; }
        public long   Y300CumulativeGoalsMin { get; set; }
    }
}
