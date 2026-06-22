using WorldEngine.Sim.Config;

namespace WorldEngine.Tests.Helpers;

public static class TestSimConfig
{
    public static SimConfig Default() => SimConfigLoader.LoadOrCreateDefault();

    public static SimConfig With(Action<SimConfig> configure)
    {
        var cfg = Default();
        configure(cfg);
        return cfg;
    }
}
