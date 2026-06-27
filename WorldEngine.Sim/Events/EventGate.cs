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
        string typeName = type.ToString();
        // Always-record list takes priority over suppression and tier filtering
        if (config.Events.Gate.AlwaysRecordTypes.Contains(typeName)) return true;
        if (config.Events.SuppressedTypes.Contains(typeName)) return false;
        if (tier < config.Events.MinimumRecordedTier) return false;
        return true;
    }
}
