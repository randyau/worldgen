using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

public class SimLoopTests
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

    private static (SimLoop loop, CommandQueue cmdQueue, StateCache cache) MakeLoop(WorldState world)
    {
        var sim = TestSimConfig.Default();
        // Run at max speed (no sleep between ticks) so tests finish quickly
        sim.SimLoop.UltrafastTicksPerSecond = float.MaxValue;
        sim.SimLoop.UltrafastSnapshotIntervalTicks = 1;

        var cmdQueue = new CommandQueue();
        var cache    = new StateCache();
        var eventStore = new EventStore();
        var eventCache = new EventCache();
        var phaseRunner = new PhaseRunner(sim, eventStore, eventCache);
        var snapshotBuilder = new SnapshotBuilder();
        var loop = new SimLoop(world, cmdQueue, cache, phaseRunner, snapshotBuilder, sim, eventCache);

        // Start running at Ultrafast so ticks accumulate quickly
        cmdQueue.Enqueue(new SetSimSpeed(SimSpeed.Ultrafast));
        return (loop, cmdQueue, cache);
    }

    [Fact]
    public async Task SimLoop_RunsForTenTicksWithoutError()
    {
        var world = BuildWorld();
        var (loop, _, cache) = MakeLoop(world);

        loop.Start();
        await Task.Delay(500);
        loop.Stop();

        world.CurrentTick.Should().BeGreaterThanOrEqualTo(10,
            "500ms at Ultrafast should accumulate at least 10 ticks");
    }

    [Fact]
    public async Task SimLoop_PauseHaltsTickProgress()
    {
        var world = BuildWorld();
        var (loop, cmdQueue, _) = MakeLoop(world);

        loop.Start();
        await Task.Delay(100); // let some ticks run
        cmdQueue.Enqueue(new PauseToggle()); // pause
        await Task.Delay(50);  // wait for pause to apply

        long tickAtPause = world.CurrentTick;
        await Task.Delay(200); // wait while paused
        long tickAfterWait = world.CurrentTick;

        loop.Stop();

        tickAfterWait.Should().Be(tickAtPause,
            "no ticks should advance while the simulation is paused");
    }

    [Fact]
    public async Task SimLoop_UnpauseResumesProgress()
    {
        var world = BuildWorld();
        var (loop, cmdQueue, _) = MakeLoop(world);

        loop.Start();
        await Task.Delay(50);
        cmdQueue.Enqueue(new PauseToggle()); // pause
        await Task.Delay(100); // let pause settle

        long pausedTick = world.CurrentTick;
        cmdQueue.Enqueue(new PauseToggle()); // unpause
        await Task.Delay(300); // let ticks run again
        loop.Stop();

        world.CurrentTick.Should().BeGreaterThan(pausedTick,
            "ticks should advance again after unpausing");
    }

    [Fact]
    public async Task SimLoop_StepOneTickAdvancesExactlyOne()
    {
        var world = BuildWorld();
        var (loop, cmdQueue, _) = MakeLoop(world);

        loop.Start();
        await Task.Delay(50);
        cmdQueue.Enqueue(new PauseToggle()); // pause
        await Task.Delay(100); // settle into pause

        long before = world.CurrentTick;
        cmdQueue.Enqueue(new StepOneTick());
        await Task.Delay(100); // wait for the step to be processed

        loop.Stop();

        world.CurrentTick.Should().Be(before + 1,
            "StepOneTick while paused should advance exactly one tick");
    }

    [Fact]
    public async Task SimLoop_SeasonAdvancesCorrectly()
    {
        var world = BuildWorld();
        var sim = TestSimConfig.Default();
        int tps = sim.SimLoop.TicksPerSeasonalChange;

        var (loop, cmdQueue, _) = MakeLoop(world);
        loop.Start();

        // Wait for enough ticks to advance a season (at least tps ticks)
        long deadline = Environment.TickCount64 + 5000;
        while (world.CurrentTick < tps && Environment.TickCount64 < deadline)
            await Task.Delay(10);

        loop.Stop();

        world.CurrentSeason.Should().NotBe(Season.Spring,
            $"after {world.CurrentTick} ticks (>{tps} per seasonal change), season should have advanced from Spring");
    }

    [Fact]
    public async Task SimLoop_YearAdvancesAfterFourSeasons()
    {
        var world = BuildWorld();
        var sim = TestSimConfig.Default();
        int ticksPerYear = sim.SimLoop.TicksPerSeasonalChange * 4;

        var (loop, cmdQueue, _) = MakeLoop(world);
        loop.Start();

        long deadline = Environment.TickCount64 + 5000;
        while (world.CurrentTick < ticksPerYear && Environment.TickCount64 < deadline)
            await Task.Delay(10);

        loop.Stop();

        world.CurrentYear.Should().BeGreaterThan(1,
            $"after {world.CurrentTick} ticks (>={ticksPerYear} for a full year), year should have incremented");
    }
}
