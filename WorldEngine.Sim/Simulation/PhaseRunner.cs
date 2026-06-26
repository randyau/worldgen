using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.Simulation.Phases;
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
    private readonly EventGate _gate;
    private readonly EnvironmentalPhase _envPhase;
    private readonly EntityBehaviorPhase _entityPhase;
    private readonly CharacterBehaviorPhase  _charPhase;
    private readonly Tier2BehaviorPhase      _tier2Phase;
    private readonly PopulationDynamicsPhase _popPhase;
    private readonly ResourcePressurePhase   _pressurePhase;
    private readonly Action<SimPhase>? _phaseObserver;
    private readonly List<PendingEvent> _injectedEvents = new();
    private int _lastAnnualTickYear;

    public PhaseRunner(
        SimConfig config,
        EventStore eventStore,
        EventCache eventCache,
        EventGate? gate = null,
        Action<SimPhase>? phaseObserver = null,
        BeastCatalog? beastCatalog = null)
    {
        _config        = config;
        _eventStore    = eventStore;
        _eventCache    = eventCache;
        _gate          = gate ?? new EventGate(config);
        _envPhase      = new EnvironmentalPhase(config);
        _entityPhase   = new EntityBehaviorPhase(
            beastCatalog ?? BeastCatalogLoader.LoadOrCreateDefault(),
            config.Beasts.StarvationHealthLoss);
        _charPhase     = new CharacterBehaviorPhase(config);
        _tier2Phase    = new Tier2BehaviorPhase(config);
        _popPhase      = new PopulationDynamicsPhase(config);
        _pressurePhase = new ResourcePressurePhase(config);
        _phaseObserver = phaseObserver;
    }

    /// <summary>
    /// Inject a pending event that will be processed by Phase 7 on the next RunTick call.
    /// Used by tests to simulate Phase 1 output without running the full environmental system.
    /// </summary>
    public void InjectPendingEvent(PendingEvent pending) => _injectedEvents.Add(pending);

    public void RunTick(WorldState world)
    {
        bool isAnnualTick = world.CurrentSeason == Season.Spring
            && world.CurrentYear != _lastAnnualTickYear;
        if (isAnnualTick)
            _lastAnnualTickYear = world.CurrentYear;

        var pending = RunEnvironmentalPhase(world, isAnnualTick);
        pending.AddRange(_injectedEvents);
        _injectedEvents.Clear();

        RunPhaseStub(world, SimPhase.ResourceProduction);
        RunPopulationDynamicsPhase(world, pending, isAnnualTick);
        pending.AddRange(_pressurePhase.Execute(world, world.CurrentTick));
        RunEntityBehaviorPhase(world, pending, isAnnualTick);
        RunCharacterBehaviorPhase(world, pending, isAnnualTick);
        if (isAnnualTick)
            CivTracker.RunAnnualDiplomacy(world, pending);
        RunPhaseStub(world, SimPhase.ConflictResolution);
        RunEventGeneration(world, pending);

        world.CurrentTick++;
    }

    private List<PendingEvent> RunEnvironmentalPhase(WorldState world, bool isAnnualTick)
    {
        _phaseObserver?.Invoke(SimPhase.Environmental);
        var pending = new List<PendingEvent>();
        _envPhase.RunTick(world, pending, isAnnualTick);
        return pending;
    }

    private void RunPopulationDynamicsPhase(WorldState world, List<PendingEvent> pending, bool isAnnualTick)
    {
        _phaseObserver?.Invoke(SimPhase.PopulationDynamics);
        pending.AddRange(_popPhase.Execute(world, isAnnualTick));
    }

    private void RunEntityBehaviorPhase(WorldState world, List<PendingEvent> pending, bool isAnnualTick)
    {
        _phaseObserver?.Invoke(SimPhase.EntityBehavior);
        _entityPhase.RunTick(world, pending, isAnnualTick);
    }

    private void RunCharacterBehaviorPhase(WorldState world, List<PendingEvent> pending, bool isAnnualTick)
    {
        _phaseObserver?.Invoke(SimPhase.CharacterDecisions);
        pending.AddRange(_charPhase.Execute(world, world.CurrentTick, isAnnualTick));
        pending.AddRange(_tier2Phase.Execute(world, world.CurrentTick));
    }

    private void RunPhaseStub(WorldState world, SimPhase phase)
    {
        _phaseObserver?.Invoke(phase);
    }

    private static string GetEventDomain(EventType type) => (int)type switch
    {
        >= 1000 and < 2000 => "Environmental",
        >= 2000 and < 3000 => "Beast",
        >= 3000 and < 7000 => "Character",
        >= 9000            => "GodMode",
        _                  => "Unknown"
    };

    private void RunEventGeneration(WorldState world, List<PendingEvent> pending)
    {
        _phaseObserver?.Invoke(SimPhase.EventGeneration);
        if (pending.Count == 0) return;

        // Step 1: classify + gate
        var batch = new List<(PendingEvent pe, SimEvent ev)>();
        foreach (var pe in pending)
        {
            bool isFirst = !_eventCache.ContainsType(pe.Type);
            var (tier, impact) = SignificanceClassifier.Classify(pe.Type, pe.PayloadJson, isFirst);
            if (!_gate.ShouldRecord(pe.Type, tier)) continue;

            var ev = new SimEvent
            {
                Id               = new EventId(0),
                Type             = pe.Type,
                TypeName         = pe.Type.ToString(),
                Domain           = GetEventDomain(pe.Type),
                Year             = world.CurrentYear,
                Season           = world.CurrentSeason,
                Tick             = world.CurrentTick,
                Location         = pe.Location,
                TierInvolvement  = tier,
                VerbClass        = VerbClassification.Classify(pe.Type),
                PopulationImpact = impact,
                IsFirstOfKind    = isFirst,
                IsGodMode        = false,
                ActorId          = pe.ActorId,
                ActorName        = pe.ActorName,
                CivId            = pe.CivId,
                SettlementName   = pe.SettlementName,
                PayloadJson      = pe.PayloadJson,
            };
            batch.Add((pe, ev));
        }

        if (batch.Count == 0) return;

        // Step 2: DB — single transaction writes events + causal edges + entity refs
        var inserted = _eventStore.BatchWriteAll(batch);

        // Step 3: Update OriginEventId on matching ActiveDisasters (WorldState mutation, no DB)
        UpdateActiveDisasterOrigins(world, batch, inserted);

        // Step 4: Cache (ALWAYS after DB)
        foreach (var ev in inserted)
            _eventCache.Add(ev);
    }

    private static void UpdateActiveDisasterOrigins(
        WorldState world,
        List<(PendingEvent pe, SimEvent ev)> batch,
        IReadOnlyList<SimEvent> inserted)
    {
        for (int i = 0; i < batch.Count && i < inserted.Count; i++)
        {
            var (pe, _) = batch[i];
            var realEv = inserted[i];

            // Drought events have no Location but track ActiveDroughts.
            if (pe.Type == EventType.DroughtBegan)
            {
                for (int d = 0; d < world.ActiveDroughts.Count; d++)
                {
                    if (world.ActiveDroughts[d].OriginEventId.Value == 0)
                    {
                        world.ActiveDroughts[d] = world.ActiveDroughts[d] with { OriginEventId = realEv.Id };
                        break;
                    }
                }
                continue;
            }

            if (pe.Location is not { } coord) continue;

            DisasterType? dType = pe.Type switch
            {
                EventType.VolcanicEruption   => DisasterType.VolcanicAsh,
                EventType.EarthquakeOccurred => DisasterType.SeismicDamage,
                EventType.WildfireOccurred   => DisasterType.Wildfire,
                EventType.FloodOccurred      => DisasterType.Flood,
                _                            => null
            };
            if (dType is null) continue;

            if (!world.ActiveTileDisasters.TryGetValue(coord, out var disasters)) continue;
            for (int d = 0; d < disasters.Count; d++)
            {
                if (disasters[d].Type == dType.Value && disasters[d].OriginEventId.Value == 0)
                {
                    disasters[d] = disasters[d] with { OriginEventId = realEv.Id };
                    break;
                }
            }
        }
    }
}
