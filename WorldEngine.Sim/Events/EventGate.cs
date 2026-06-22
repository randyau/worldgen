using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Events;

/// <summary>
/// Pre-write gate deciding whether an event is recorded to the history log.
/// God Mode events are always recorded; otherwise suppressed types and
/// sub-minimum-tier events are dropped.
/// </summary>
public sealed class EventGate(SimConfig config)
{
    public bool ShouldRecord(EventType type, EventTier tier, bool isGodMode = false)
    {
        if (isGodMode) return true;
        if (config.Events.SuppressedTypes.Contains(type.ToString())) return false;
        if (tier < config.Events.MinimumRecordedTier) return false;
        return true;
    }
}
