using FluentAssertions;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Regression pins for the WildlifeRiskConfig per-biome multiplier table (D3 extraction).
/// Each test asserts the loaded table matches the value previously hardcoded in
/// PopulationDynamicsPhase.BiomeWildlifeRisk(), ensuring behaviour-preserving extraction.
/// </summary>
public class WildlifeRiskConfigTests
{
    private static readonly SimConfig _cfg;

    static WildlifeRiskConfigTests()
    {
        SimConfigLoader.StrictMode = true;
        _cfg = SimConfigLoader.LoadOrCreateDefault();
    }

    // ── Per-biome multiplier pins ─────────────────────────────────────────────

    [Theory]
    [InlineData(BiomeType.TropicalRainforest, 2.0f)]
    [InlineData(BiomeType.BorealForest,       1.6f)]
    [InlineData(BiomeType.TemperateForest,    1.4f)]
    [InlineData(BiomeType.Swamp,              1.5f)]
    [InlineData(BiomeType.Grassland,          1.0f)]
    [InlineData(BiomeType.Hills,              0.9f)]
    [InlineData(BiomeType.Mountain,           0.8f)]
    [InlineData(BiomeType.Savanna,            0.6f)]
    [InlineData(BiomeType.Plains,             0.5f)]
    [InlineData(BiomeType.Desert,             0.4f)]
    [InlineData(BiomeType.Tundra,             0.5f)]
    [InlineData(BiomeType.HighMountain,       0.3f)]
    [InlineData(BiomeType.Volcanic,           0.4f)]
    public void BiomeRisk_MatchesPreviousHardcodedValues(BiomeType biome, float expected)
    {
        var table = _cfg.WildlifeRisk.BuildTable();
        table[(int)biome].Should().BeApproximately(expected, 0.001f,
            $"BiomeType.{biome} should have risk {expected} (was hardcoded in PopulationDynamicsPhase)");
    }

    [Fact]
    public void DefaultRisk_Is_0_6()
    {
        // The original _ => 0.6f fallback
        _cfg.WildlifeRisk.DefaultRisk.Should().BeApproximately(0.6f, 0.001f);
    }

    [Fact]
    public void TableLength_Is_16()
    {
        var table = _cfg.WildlifeRisk.BuildTable();
        table.Length.Should().Be(16, "BiomeType has 16 values");
    }

    // ── Strict-mode load test ─────────────────────────────────────────────────

    [Fact]
    public void ProductionToml_WildlifeRiskSection_LoadsUnderStrictMode()
    {
        SimConfigLoader.StrictMode = true;
        var act = () => SimConfigLoader.LoadOrCreateDefault();
        act.Should().NotThrow("sim_config.toml must have zero unbound keys including [wildlife_risk]");
    }

    [Fact]
    public void WildlifeRiskConfig_IsPartOfSimConfig()
    {
        _cfg.WildlifeRisk.Should().NotBeNull();
        _cfg.WildlifeRisk.BiomeRisk.Should().NotBeEmpty("TOML must populate wildlife_risk.biome_risk");
    }
}
