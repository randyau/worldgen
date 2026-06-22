using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation;

/// <summary>
/// Runs the 7 simulation phases in order each tick.
/// Phase 1 (Environmental) produces PendingEvents consumed by Phase 7 (EventGeneration).
/// All other phases are stubs in M1.
/// </summary>
public sealed class PhaseRunner
{
    private readonly SimConfig _config;
    private readonly EventStore _eventStore;
    private readonly EventCache _eventCache;
    private readonly Action<SimPhase>? _phaseObserver;
    private readonly List<PendingEvent> _injectedEvents = new();

    public PhaseRunner(
        SimConfig config,
        EventStore eventStore,
        EventCache eventCache,
        Action<SimPhase>? phaseObserver = null)
    {
        _config       = config;
        _eventStore   = eventStore;
        _eventCache   = eventCache;
        _phaseObserver = phaseObserver;
    }

    /// <summary>
    /// Inject a pending event that will be processed by Phase 7 on the next RunTick call.
    /// Used by tests to simulate Phase 1 output without running the full environmental system.
    /// </summary>
    public void InjectPendingEvent(PendingEvent pending) => _injectedEvents.Add(pending);

    public void RunTick(WorldState world)
    {
        var pending = RunEnvironmentalPhase(world);
        pending.AddRange(_injectedEvents);
        _injectedEvents.Clear();

        RunPhaseStub(world, SimPhase.ResourceProduction);
        RunPhaseStub(world, SimPhase.PopulationDynamics);
        RunPhaseStub(world, SimPhase.EntityBehavior);
        RunPhaseStub(world, SimPhase.CharacterDecisions);
        RunPhaseStub(world, SimPhase.ConflictResolution);
        RunEventGeneration(world, pending);

        world.CurrentTick++;
    }

    private List<PendingEvent> RunEnvironmentalPhase(WorldState world)
    {
        _phaseObserver?.Invoke(SimPhase.Environmental);
        // Stub — full implementation in Epic 1.5 (Phase 5)
        return new List<PendingEvent>();
    }

    private void RunPhaseStub(WorldState world, SimPhase phase)
    {
        _phaseObserver?.Invoke(phase);
        // Stub — full implementation in respective future phases
    }

    private void RunEventGeneration(WorldState world, List<PendingEvent> pending)
    {
        _phaseObserver?.Invoke(SimPhase.EventGeneration);

        foreach (var pe in pending)
        {
            var ev = new SimEvent
            {
                Id               = new EventId(world.CurrentTick * 1000 + pending.IndexOf(pe)),
                Type             = pe.Type,
                Year             = world.CurrentYear,
                Season           = world.CurrentSeason,
                Tick             = world.CurrentTick,
                Location         = pe.Location,
                TierInvolvement  = EventTier.Background,
                VerbClass        = VerbClass.Transformation,
                PopulationImpact = PopulationImpact.None,
                IsFirstOfKind    = false,
                IsGodMode        = false,
                PayloadJson      = pe.PayloadJson,
            };

            _eventStore.Write(ev);
            _eventCache.Add(ev);
        }
    }
}
