using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;

namespace WorldEngine.Tests.Helpers;

public static class WorldTestHelper
{
    public static WorldState CreateSmallWorld(int seed = 42)
    {
        var config = new WorldConfig
        {
            Seed       = seed,
            WidthKm    = 100,
            HeightKm   = 100,
            TileWidthKm = 10
        };
        var simConfig = TestSimConfig.Default();
        var pipeline  = new WorldGenPipeline();
        return pipeline.RunFullAsync(config, simConfig).GetAwaiter().GetResult();
    }
}
