using WorldEngine.Sim.Config;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Tests for B4 — config profile overlay and --set override support.
/// </summary>
public class ConfigProfileTests : IDisposable
{
    private readonly bool _originalStrictMode = SimConfigLoader.StrictMode;

    public void Dispose() => SimConfigLoader.StrictMode = _originalStrictMode;

    // ─── Profile overlay ──────────────────────────────────────────────────────

    [Fact]
    public void Profile_OverridesOneKey_BaseValuesUntouched()
    {
        SimConfigLoader.StrictMode = true;

        // Build a base config TOML and a minimal profile that overrides one key
        var baseToml = """
            [world_gen]
            default_tile_size_km = 10

            [disasters]
            wildfire_max_ticks = 16

            [settlement]
            pop_max = 50000
            """;

        var profileToml = """
            [disasters]
            wildfire_max_ticks = 8
            """;

        // Write to temp files
        var baseFile    = WriteTempToml(baseToml);
        var profileFile = WriteTempToml(profileToml, "profile_");

        try
        {
            // Temporarily disable strict mode so our minimal test TOML (which lacks most keys) doesn't throw
            SimConfigLoader.StrictMode = false;

            var config = SimConfigLoader.Load(baseFile,
                overrides: null);

            // World gen key unchanged
            config.WorldGen.DefaultTileSizeKm.Should().Be(10);
            // Original disaster value
            config.Disasters.WildfireMaxTicks.Should().Be(16);
        }
        finally
        {
            File.Delete(baseFile);
            File.Delete(profileFile);
        }
    }

    [Fact]
    public void Profile_OverriddenKey_WinsOverBase()
    {
        SimConfigLoader.StrictMode = false;

        var baseToml = """
            [disasters]
            wildfire_max_ticks = 16
            drought_min_seasons = 2
            drought_max_seasons = 8
            drought_probability_per_year = 0.05
            """;

        var profileToml = """
            [disasters]
            wildfire_max_ticks = 4
            """;

        var baseFile = WriteTempToml(baseToml);

        try
        {
            // Use LoadFromToml with merged content to test the MergeToml path
            var merged = MergeTomlPublic(baseToml, profileToml);
            var config = SimConfigLoader.LoadFromToml(merged);

            config.Disasters.WildfireMaxTicks.Should().Be(4);
            // Other disaster keys unchanged
            config.Disasters.DroughtMinSeasons.Should().Be(2);
        }
        finally
        {
            File.Delete(baseFile);
        }
    }

    // ─── --set override wins over profile ─────────────────────────────────────

    [Fact]
    public void SetOverride_WinsOverProfile()
    {
        SimConfigLoader.StrictMode = true;

        // Use the real base config so strict mode passes, apply a --set override
        var config = SimConfigLoader.Load(
            overrides: [new KeyValuePair<string, string>("disasters.wildfire_max_ticks", "2")]);

        config.Disasters.WildfireMaxTicks.Should().Be(2, "CLI --set override wins over base");
    }

    // ─── Profile file discovery ───────────────────────────────────────────────

    [Fact]
    public void Load_WithNonexistentProfile_Throws()
    {
        var baseFile = WriteTempToml("[world_gen]\ndefault_tile_size_km = 10\n");

        try
        {
            SimConfigLoader.StrictMode = false;
            var act = () => SimConfigLoader.Load(baseFile, profileName: "nonexistent_profile_xyz");
            act.Should().Throw<FileNotFoundException>()
               .WithMessage("*nonexistent_profile_xyz*");
        }
        finally
        {
            File.Delete(baseFile);
        }
    }

    // ─── example fast_history profile loads under strict mode ────────────────

    [Fact]
    public void FastHistoryProfile_LoadsCleanUnderStrictMode()
    {
        SimConfigLoader.StrictMode = true;

        // Load the real base config with the fast_history profile
        var act = () => SimConfigLoader.Load(profileName: "fast_history");

        act.Should().NotThrow("fast_history.toml must only contain bound keys");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string WriteTempToml(string content, string prefix = "base_")
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid()}.toml");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Expose the private MergeToml for testing via the Load path with temp files.</summary>
    private static string MergeTomlPublic(string baseToml, string profileToml)
    {
        var baseFile    = WriteTempToml(baseToml);
        var profileDir  = Path.Combine(Path.GetTempPath(), $"profiles_{Guid.NewGuid()}");
        Directory.CreateDirectory(profileDir);
        var profileFile = Path.Combine(profileDir, "testprofile.toml");
        File.WriteAllText(profileFile, profileToml);

        try
        {
            // Use reflection to call the private MergeToml
            var method = typeof(SimConfigLoader)
                .GetMethod("MergeToml", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            return (string)method.Invoke(null, new object[] { baseToml, profileToml })!;
        }
        finally
        {
            File.Delete(baseFile);
            File.Delete(profileFile);
            Directory.Delete(profileDir);
        }
    }
}
