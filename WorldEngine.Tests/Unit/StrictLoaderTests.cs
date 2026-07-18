using WorldEngine.Sim.Config;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Tests for B1 — SimConfigLoader strict mode that detects unbound TOML keys.
/// </summary>
public class StrictLoaderTests : IDisposable
{
    // Preserve the original StrictMode value and restore after each test
    private readonly bool _originalStrictMode = SimConfigLoader.StrictMode;

    public void Dispose() => SimConfigLoader.StrictMode = _originalStrictMode;

    // ── Strict mode throws on unknown keys ───────────────────────────────────

    [Fact]
    public void StrictMode_ThrowsOnUnknownTopLevelKey()
    {
        SimConfigLoader.StrictMode = true;

        var toml = """
            [world_gen]
            default_tile_size_km = 10

            [totally_bogus_section]
            some_key = 99
            """;

        var act = () => SimConfigLoader.LoadFromToml(toml);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*totally_bogus_section*");
    }

    [Fact]
    public void StrictMode_ThrowsOnUnknownNestedKey()
    {
        SimConfigLoader.StrictMode = true;

        var toml = """
            [world_gen]
            default_tile_size_km = 10
            nonexistent_key = 42
            """;

        var act = () => SimConfigLoader.LoadFromToml(toml);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*world_gen.nonexistent_key*");
    }

    // ── Non-strict mode warns but does not throw ──────────────────────────────

    [Fact]
    public void NonStrictMode_DoesNotThrowOnUnknownKey()
    {
        SimConfigLoader.StrictMode = false;

        var toml = """
            [unknown_garbage]
            foo = 1
            """;

        var act = () => SimConfigLoader.LoadFromToml(toml);

        // Should warn (to stderr) but not throw
        act.Should().NotThrow();
    }

    // ── Clean config loads without warnings ───────────────────────────────────

    [Fact]
    public void StrictMode_CleanTomlLoadsWithNoException()
    {
        SimConfigLoader.StrictMode = true;

        // Minimal valid subset — only keys that actually bind to SimConfig properties.
        // Note: days_per_season / seasons_per_year are dead in current sim_config.toml
        // (SimLoopConfig binds ticks_per_seasonal_change, not those names).
        var toml = """
            [world_gen]
            default_tile_size_km = 10
            default_width_km     = 4000
            default_height_km    = 3000

            [world_gen.tectonics]
            plate_count = 15

            [sim_loop]
            ticks_per_seasonal_change = 4
            auto_save_interval_ticks  = 960
            auto_save_dir             = "worldsave"

            [events]
            minimum_recorded_tier   = 0
            recent_event_cache_size = 500
            suppressed_types        = []
            """;

        var act = () => SimConfigLoader.LoadFromToml(toml);

        act.Should().NotThrow();
    }

    // ── Known keys bind correctly even in strict mode ─────────────────────────

    [Fact]
    public void StrictMode_KnownKeysLoadCorrectValues()
    {
        SimConfigLoader.StrictMode = true;

        var toml = """
            [world_gen]
            default_tile_size_km = 42

            [disasters]
            wildfire_max_ticks = 99
            """;

        var config = SimConfigLoader.LoadFromToml(toml);

        config.WorldGen.DefaultTileSizeKm.Should().Be(42);
        config.Disasters.WildfireMaxTicks.Should().Be(99);
    }

    // ── Nested sub-table with bound keys is recognized ───────────────────────

    [Fact]
    public void StrictMode_KnownNestedSubTableIsRecognized()
    {
        SimConfigLoader.StrictMode = true;

        // [world_gen.tectonics] maps to WorldGenConfig.Tectonics → TectonicsConfig
        var toml = """
            [world_gen.tectonics]
            plate_count                     = 15
            min_plate_separation_fraction   = 0.12
            continental_plate_fraction      = 0.45
            """;

        var act = () => SimConfigLoader.LoadFromToml(toml);

        act.Should().NotThrow();
    }

    // ── Multiple unknown keys are reported together ───────────────────────────

    [Fact]
    public void StrictMode_ReportsAllUnknownKeysInOneException()
    {
        SimConfigLoader.StrictMode = true;

        var toml = """
            [bogus_one]
            alpha = 1

            [bogus_two]
            beta = 2
            """;

        var act = () => SimConfigLoader.LoadFromToml(toml);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("bogus_one").And.Contain("bogus_two");
    }

    // ── B2: production sim_config.toml loads with zero dead keys ─────────────

    /// <summary>
    /// Verifies that the shipped sim_config.toml has zero unbound keys (B2 guarantee).
    /// Any future addition to sim_config.toml must be backed by a C# config property
    /// or this test will fail, preventing the dead-key problem from recurring.
    /// </summary>
    [Fact]
    public void ProductionToml_LoadsCleanUnderStrictMode()
    {
        SimConfigLoader.StrictMode = true;

        // LoadOrCreateDefault() searches for the real config file
        var act = () => SimConfigLoader.LoadOrCreateDefault();

        act.Should().NotThrow("sim_config.toml must contain zero unbound keys");
    }
}
