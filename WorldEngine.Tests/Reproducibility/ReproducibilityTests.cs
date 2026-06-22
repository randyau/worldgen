using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Reproducibility;

public class ReproducibilityTests
{
    [Fact]
    public async Task SameSeedProducesSameWorld()
    {
        var config = new WorldConfig { Seed = 12345, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
        var simCfg = TestSimConfig.Default();

        var w1 = await new WorldGenPipeline().RunFullAsync(config, simCfg);
        var w2 = await new WorldGenPipeline().RunFullAsync(config, simCfg);

        int tileCount = w1.TileGrid.TileWidth * w1.TileGrid.TileHeight;

        for (int y = 0; y < w1.TileGrid.TileHeight; y++)
        {
            for (int x = 0; x < w1.TileGrid.TileWidth; x++)
            {
                var coord = new TileCoord(x, y);
                var t1 = w1.TileGrid.GetTile(coord);
                var t2 = w2.TileGrid.GetTile(coord);

                t1.Should().BeEquivalentTo(t2,
                    $"tile ({x},{y}) must be identical across two runs with seed 12345");
            }
        }

        w1.SeasonalProfiles.Should().BeEquivalentTo(w2.SeasonalProfiles,
            "seasonal profiles must be identical across runs");
    }
}
