using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation;

/// <summary>
/// Background simulation thread. Ticks WorldState, builds snapshots, commits to StateCache.
/// Only the background thread touches WorldState. UI thread only reads StateCache.
/// </summary>
public sealed class SimLoop
{
    private readonly WorldState _world;
    private readonly CommandQueue _cmdQueue;
    private readonly StateCache _stateCache;
    private readonly PhaseRunner _phaseRunner;
    private readonly SnapshotBuilder _snapshotBuilder;
    private readonly EventCache _eventCache;
    private readonly SimLoopConfig _cfg;

    private Thread? _thread;
    private volatile bool _running;

    public Exception? LastException { get; private set; }

    private SimSpeed _currentSpeed = SimSpeed.Normal;
    private bool _paused;
    private bool _stepOneTick;

    private OverlayType _overlay = OverlayType.Biome;

    public SimLoop(
        WorldState world,
        CommandQueue cmdQueue,
        StateCache stateCache,
        PhaseRunner phaseRunner,
        SnapshotBuilder snapshotBuilder,
        SimConfig config,
        EventCache eventCache)
    {
        _world           = world;
        _cmdQueue        = cmdQueue;
        _stateCache      = stateCache;
        _phaseRunner     = phaseRunner;
        _snapshotBuilder = snapshotBuilder;
        _eventCache      = eventCache;
        _cfg             = config.SimLoop;
    }

    public void Start()
    {
        _running = true;
        _thread  = new Thread(Run) { IsBackground = true, Name = "SimLoop" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(millisecondsTimeout: 2000);
        // Flush any pending events to ensure they're written before shutdown
        _phaseRunner.FlushPendingEvents(_world);
    }

    private void Run()
    {
        try
        {
            while (_running)
            {
                // 1. Drain and apply commands; track whether any were processed
                int cmdCount = 0;
                foreach (var cmd in _cmdQueue.DrainAll())
                {
                    ApplyCommand(cmd);
                    cmdCount++;
                }

                // 2. Paused idle — still rebuild snapshot when commands changed state
                if (_paused && !_stepOneTick)
                {
                    if (cmdCount > 0) CommitSnapshot();
                    Thread.Sleep(16);
                    continue;
                }

                // 3. Run tick
                _phaseRunner.RunTick(_world);

                // 4. Advance time
                AdvanceTime();

                // 5. Build snapshot (skip in Ultrafast except every N ticks)
                bool buildSnapshot = _currentSpeed != SimSpeed.Ultrafast
                    || (_world.CurrentTick % _cfg.UltrafastSnapshotIntervalTicks == 0);

                if (buildSnapshot) CommitSnapshot();

                // 6. Clear StepOneTick after one step
                if (_stepOneTick) _stepOneTick = false;

                // 7. Throttle
                int sleepMs = ThrottleSleepMs();
                if (sleepMs > 0) Thread.Sleep(sleepMs);
            }
        }
        catch (Exception ex)
        {
            LastException = ex;
            _running = false;
        }
    }

    private void CommitSnapshot()
    {
        var recentEvents = _eventCache.GetRecent(_world.SimConfig.Events.RecentEventCacheSize);
        var snapshot = _snapshotBuilder.Build(
            _world, _overlay,
            _currentSpeed, _paused,
            ticksPerSecond: (long)TicksPerSecond(),
            recentEvents: recentEvents);
        _stateCache.Commit(snapshot);
    }

    private void ApplyCommand(ICommand cmd)
    {
        switch (cmd)
        {
            case SetSimSpeed s:
                _currentSpeed = s.Speed;
                _paused = s.Speed == SimSpeed.Paused;
                break;
            case PauseToggle:
                _paused = !_paused;
                break;
            case StepOneTick:
                _stepOneTick = true;
                break;
            case SetInspectedTile t:
                _world.InspectedTile = t.Coord;
                break;
            case SetActiveOverlay o:
                _overlay = o.Overlay;
                break;
        }
    }

    private void AdvanceTime()
    {
        // Season changes every TicksPerSeasonalChange ticks
        if (_world.CurrentTick % _cfg.TicksPerSeasonalChange == 0 && _world.CurrentTick > 0)
        {
            var nextSeason = (Season)(((int)_world.CurrentSeason + 1) % 4);
            // Year advances when wrapping from Winter back to Spring
            if (nextSeason == Season.Spring)
                _world.CurrentYear++;
            _world.CurrentSeason = nextSeason;
        }
    }

    private float TicksPerSecond() => _currentSpeed switch
    {
        SimSpeed.Slow      => _cfg.SlowTicksPerSecond,
        SimSpeed.Normal    => _cfg.NormalTicksPerSecond,
        SimSpeed.Fast      => _cfg.FastTicksPerSecond,
        SimSpeed.Ultrafast => _cfg.UltrafastTicksPerSecond,
        _                  => 0f
    };

    private int ThrottleSleepMs()
    {
        float tps = TicksPerSecond();
        if (tps <= 0f || float.IsInfinity(tps) || float.IsNaN(tps)) return 0;
        return (int)(1000f / tps);
    }
}
