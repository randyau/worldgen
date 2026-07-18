using Dapper;
using Microsoft.Extensions.Logging;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;

// ─── Argument parsing ─────────────────────────────────────────────────────────

int seed       = 0;
int years      = 100;
string? configPath   = null;
string? profileName  = null;
string? outDir  = null;
var overrides   = new List<KeyValuePair<string, string>>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed"    when i + 1 < args.Length: seed        = int.Parse(args[++i]); break;
        case "--years"   when i + 1 < args.Length: years       = int.Parse(args[++i]); break;
        case "--config"  when i + 1 < args.Length: configPath  = args[++i];            break;
        case "--profile" when i + 1 < args.Length: profileName = args[++i];            break;
        case "--out"     when i + 1 < args.Length: outDir      = args[++i];            break;
        case "--set"     when i + 1 < args.Length:
            var kv = args[++i].Split('=', 2);
            if (kv.Length == 2) overrides.Add(new KeyValuePair<string, string>(kv[0], kv[1]));
            break;
    }
}

outDir ??= Path.Combine("headless_out", $"seed{seed}");
Directory.CreateDirectory(outDir);

// ─── Logging ──────────────────────────────────────────────────────────────────

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("WorldEngine");
logger.LogInformation("Headless runner — seed={Seed}, years={Years}, out={Out}", seed, years, outDir);

// ─── Config ───────────────────────────────────────────────────────────────────

SimConfigLoader.StrictMode = false; // headless runner: warn but don't throw on unbound keys
var simConfig = SimConfigLoader.Load(configPath, profileName, overrides.Count > 0 ? overrides : null);

// ─── World gen ────────────────────────────────────────────────────────────────

logger.LogInformation("Generating world...");
var worldConfig = new WorldConfig { Seed = seed };
var pipeline    = new WorldGenPipeline();
var world       = await pipeline.RunFullAsync(worldConfig, simConfig,
    progress: new Progress<(string Layer, float Fraction)>(p =>
        logger.LogInformation("  WorldGen [{Layer}] {Pct:P0}", p.Layer, p.Fraction)));

// ─── Entity spawn ─────────────────────────────────────────────────────────────

var dbPath      = Path.Combine(outDir, "world.db");
var eventStore  = new EventStore(dbPath);
var eventCache  = new EventCache(simConfig.Events.RecentEventCacheSize);
var gate        = new EventGate(simConfig);
var beastCatalog = BeastCatalogLoader.LoadOrCreateDefault();
var phaseRunner  = new PhaseRunner(simConfig, eventStore, eventCache, gate,
    beastCatalog: beastCatalog);

var spawnEvents      = BeastSpawner.SpawnAll(world, beastCatalog);
var charSpawnEvents  = CharacterSpawner.SpawnAll(world, simConfig);
var tier2SpawnEvents = Tier2Spawner.SpawnAll(world, simConfig);
foreach (var pe in spawnEvents)      phaseRunner.InjectPendingEvent(pe);
foreach (var pe in charSpawnEvents)  phaseRunner.InjectPendingEvent(pe);
foreach (var pe in tier2SpawnEvents) phaseRunner.InjectPendingEvent(pe);

// ─── Run simulation ───────────────────────────────────────────────────────────

// CommandQueue and StateCache are required by SimLoop's constructor but unused in headless mode.
// We construct them to satisfy the wiring without spinning the threaded loop.
var cmdQueue        = new WorldEngine.Sim.Core.CommandQueue();
var stateCache      = new WorldEngine.Sim.World.StateCache();
var snapshotBuilder = new WorldEngine.Sim.World.SnapshotBuilder();
var simLoop = new SimLoop(world, cmdQueue, stateCache, phaseRunner, snapshotBuilder, simConfig, eventCache);

int totalTicks = years * simConfig.SimLoop.TicksPerYear;
logger.LogInformation("Simulating {Years} years ({Ticks} ticks)...", years, totalTicks);

var wallStart = DateTime.UtcNow;
simLoop.RunSynchronous(totalTicks);
// Flush any batched events that haven't been written yet
phaseRunner.FlushPendingEvents(world);
var wallEnd   = DateTime.UtcNow;

// ─── Collect summary stats ────────────────────────────────────────────────────

double wallSec = (wallEnd - wallStart).TotalSeconds;
double ticksPerSec = wallSec > 0 ? totalTicks / wallSec : 0;

int worldPop      = world.Settlements.Values.Sum(s => s.Population);
int activeCivs    = world.Civilizations.Values.Count(c => !c.IsCollapsed);
int collapsedCivs = world.Civilizations.Values.Count(c => c.IsCollapsed);

// Event-based aggregates from the DB (after flush, all events are committed)
int settFounded  = 0, settAbandoned = 0, settConquered = 0;
int warsDeclared = 0, warsEnded = 0;
int tier0Events  = 0, tier1Events = 0, tier2Events = 0, tier3Events = 0;

try
{
    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
        $"Data Source={dbPath};Mode=ReadOnly;Cache=Private");
    conn.Open();

    settFounded   = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Events WHERE Type = 3203;");
    settAbandoned = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Events WHERE Type = 3403;");
    settConquered = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Events WHERE Type = 3207;");
    warsDeclared  = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Events WHERE Type = 3103;");
    warsEnded     = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Events WHERE Type = 3104;");
    tier0Events   = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Events WHERE TierInvolvement = 0;");
    tier1Events   = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Events WHERE TierInvolvement = 1;");
    tier2Events   = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Events WHERE TierInvolvement = 2;");
    tier3Events   = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Events WHERE TierInvolvement = 3;");
}
catch (Exception ex)
{
    logger.LogWarning("Could not read event counts from DB: {Msg}", ex.Message);
}

// ─── Dispose EventStore (flushes WAL, closes file) ────────────────────────────
eventStore.Dispose();

// ─── Print one-screen summary ─────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════════════════");
Console.WriteLine("  WorldEngine Headless Run Complete");
Console.WriteLine("══════════════════════════════════════════════════════════");
Console.WriteLine($"  Seed:            {seed}");
Console.WriteLine($"  Profile:         {profileName ?? "(base)"}");
Console.WriteLine($"  Years simulated: {years}  ({totalTicks} ticks)");
Console.WriteLine($"  Wall time:       {wallSec:F1}s  ({ticksPerSec:F0} ticks/sec)");
Console.WriteLine($"  Per 100 years:   {(wallSec / years * 100):F1}s");
Console.WriteLine();
Console.WriteLine($"  World population:   {worldPop:N0}");
Console.WriteLine($"  Active civs:        {activeCivs}");
Console.WriteLine($"  Collapsed civs:     {collapsedCivs}");
Console.WriteLine($"  Settlements total:  {world.Settlements.Count}");
Console.WriteLine($"  Ruins total:        {world.Ruins.Count}");
Console.WriteLine();
Console.WriteLine($"  Settlements founded:  {settFounded}");
Console.WriteLine($"  Settlements abandoned:{settAbandoned}");
Console.WriteLine($"  Settlements conquered:{settConquered}");
Console.WriteLine($"  Wars declared:        {warsDeclared}");
Console.WriteLine($"  Wars ended:           {warsEnded}");
Console.WriteLine();
Console.WriteLine("  Events by tier:");
Console.WriteLine($"    Background (0): {tier0Events}");
Console.WriteLine($"    Character  (1): {tier1Events}");
Console.WriteLine($"    Regional   (2): {tier2Events}");
Console.WriteLine($"    Headline   (3): {tier3Events}");
Console.WriteLine($"    Total:          {tier0Events + tier1Events + tier2Events + tier3Events}");
Console.WriteLine();
Console.WriteLine($"  Output: {Path.GetFullPath(outDir)}");
Console.WriteLine("══════════════════════════════════════════════════════════");

