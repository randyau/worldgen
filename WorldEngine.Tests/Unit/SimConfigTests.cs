using FluentAssertions;
using WorldEngine.Sim.Config;

namespace WorldEngine.Tests.Unit;

public class SimConfigTests
{
    [Fact]
    public void SimConfigLoader_LoadsExistingToml()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid()}.toml");

        try
        {
            var sourceConfig = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "config", "sim_config.toml");
            if (!File.Exists(sourceConfig))
            {
                sourceConfig = Path.Combine(AppContext.BaseDirectory, "config", "sim_config.toml");
            }
            if (!File.Exists(sourceConfig))
            {
                sourceConfig = "config/sim_config.toml";
            }

            File.Copy(sourceConfig, tempPath, overwrite: true);

            var config = SimConfigLoader.LoadOrCreateDefault(tempPath);

            config.Should().NotBeNull();
            config.WorldGen.Should().NotBeNull();
            config.WorldGen.Tectonics.Should().NotBeNull();
            config.WorldGen.Tectonics.PlateCount.Should().Be(15);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void SimConfigLoader_AllSectionsPresent()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid()}.toml");

        try
        {
            var sourceConfig = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "config", "sim_config.toml");
            if (!File.Exists(sourceConfig))
            {
                sourceConfig = Path.Combine(AppContext.BaseDirectory, "config", "sim_config.toml");
            }
            if (!File.Exists(sourceConfig))
            {
                sourceConfig = "config/sim_config.toml";
            }

            File.Copy(sourceConfig, tempPath, overwrite: true);

            var config = SimConfigLoader.LoadOrCreateDefault(tempPath);

            config.Should().NotBeNull();
            config.WorldGen.Should().NotBeNull();
            config.WorldGen.Tectonics.Should().NotBeNull();
            config.WorldGen.Rivers.Should().NotBeNull();
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void SimConfigLoader_MissingFileCreatesDefault()
    {
        var nonexistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.toml");

        var act = () => SimConfigLoader.LoadOrCreateDefault(nonexistentPath);

        act.Should().NotThrow();
        var config = SimConfigLoader.LoadOrCreateDefault(nonexistentPath);
        config.Should().NotBeNull();
    }

    [Fact]
    public void SimConfig_TectonicsPlateCountIsPositive()
    {
        var config = SimConfigLoader.LoadOrCreateDefault();

        config.Should().NotBeNull();
        config.WorldGen.Should().NotBeNull();
        config.WorldGen.Tectonics.Should().NotBeNull();
        config.WorldGen.Tectonics.PlateCount.Should().BeGreaterThan(0);
    }
}
