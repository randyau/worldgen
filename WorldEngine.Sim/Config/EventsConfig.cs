using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Config;

public class EventsConfig
{
    public EventTier MinimumRecordedTier { get; set; } = EventTier.Background;
    public List<string> SuppressedTypes { get; set; } = new();
    public int RecentEventCacheSize { get; set; } = 500;
}
