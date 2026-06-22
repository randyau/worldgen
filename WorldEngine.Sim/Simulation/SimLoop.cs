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

    private SimSpeed _currentSpeed = SimSpeed.Normal;
    private bool _paused;
    private bool _stepOneTick;

    private ViewportRect _viewport = ViewportRect.Default;
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
    }

    private void Run()
    {
        while (_running)
        {
            // 1. Drain and apply commands
            foreach (var cmd in _cmdQueue.DrainAll())
                ApplyCommand(cmd);

            // 2. Paused idle
            if (_paused && !_stepOneTick)
            {
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

            if (buildSnapshot)
            {
                var recentEvents = _eventCache.GetRecent(_world.SimConfig.Events.RecentEventCacheSize);
                var snapshot = _snapshotBuilder.Build(
                    _world, _viewport, _overlay,
                    _currentSpeed, _paused,
                    ticksPerSecond: (long)TicksPerSecond(),
                    recentEvents: recentEvents);
                _stateCache.Commit(snapshot);
            }

            // 6. Clear StepOneTick after one step
            if (_stepOneTick) _stepOneTick = false;

            // 7. Throttle
            int sleepMs = ThrottleSleepMs();
            if (sleepMs > 0) Thread.Sleep(sleepMs);
        }
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
            case SetViewport v:
                _viewport = new ViewportRect(v.X, v.Y, v.Width, v.Height);
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
