using WorldEngine.Sim.Config;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Tests for B3 — SimConfigValidator range, ordering, and cross-field invariants.
/// </summary>
public class SimConfigValidationTests : IDisposable
{
    private readonly bool _originalStrictMode = SimConfigLoader.StrictMode;

    public void Dispose() => SimConfigLoader.StrictMode = _originalStrictMode;

    // ── Default config passes validation ──────────────────────────────────────

    [Fact]
    public void DefaultConfig_PassesValidation()
    {
        var config = SimConfig.Default();
        var act = () => SimConfigValidator.Validate(config);
        act.Should().NotThrow();
    }

    // ── Production sim_config.toml passes validation ───────────────────────────

    [Fact]
    public void ProductionToml_PassesValidation()
    {
        // The shipped config is already validated during load (B3 wires Validate into LoadOrCreateDefault).
        // This test is a regression guard: if any TOML value violates an invariant, it will fail here.
        SimConfigLoader.StrictMode = true;  // also verify no dead keys
        var act = () => SimConfigLoader.LoadOrCreateDefault();
        act.Should().NotThrow();
    }

    // ── Utility weights must sum to 1.0 ─────────────────────────────────────

    [Fact]
    public void CharacterWeights_NotSummingToOne_Throws()
    {
        var config = SimConfig.Default();
        config.Character.NeedsWeight = 0.5f;
        config.Character.GoalsWeight = 0.5f;
        config.Character.PersonalityWeight = 0.5f; // sum = 1.5

        var act = () => SimConfigValidator.Validate(config);

        act.Should().Throw<SimConfigValidationException>()
            .Which.Message.Should().Contain("needs_weight + goals_weight + personality_weight");
    }

    // ── Probability out of range ─────────────────────────────────────────────

    [Fact]
    public void Probability_OutOfRange_Throws()
    {
        var config = SimConfig.Default();
        config.Disasters.WildfireIgnitionProbabilityPerTick = 1.5f; // > 1

        var act = () => SimConfigValidator.Validate(config);

        act.Should().Throw<SimConfigValidationException>()
            .Which.Message.Should().Contain("wildfire_ignition_probability_per_tick");
    }

    // ── Ordering invariant: shortage_threshold > crisis_threshold ────────────

    [Fact]
    public void ShortageThreshold_BelowCrisis_Throws()
    {
        var config = SimConfig.Default();
        config.ResourcePressure.ShortageThreshold = 0.2f;
        config.ResourcePressure.CrisisThreshold   = 0.5f; // crisis > shortage

        var act = () => SimConfigValidator.Validate(config);

        act.Should().Throw<SimConfigValidationException>()
            .Which.Message.Should().Contain("shortage_threshold");
    }

    // ── Ordering invariant: optimal_temperature_low < optimal_temperature_high ─

    [Fact]
    public void OptimalTemperature_Inverted_Throws()
    {
        var config = SimConfig.Default();
        config.ResourcePressure.OptimalTemperatureLow  = 200;
        config.ResourcePressure.OptimalTemperatureHigh = 100; // low > high

        var act = () => SimConfigValidator.Validate(config);

        act.Should().Throw<SimConfigValidationException>()
            .Which.Message.Should().Contain("optimal_temperature_low");
    }

    // ── Min/max pair: raid_damage_min ≤ raid_damage_max ─────────────────────

    [Fact]
    public void RaidDamage_MinAboveMax_Throws()
    {
        var config = SimConfig.Default();
        // D5: raid_damage moved to WarConfig
        config.War.RaidDamageMin = 50;
        config.War.RaidDamageMax = 10; // min > max

        var act = () => SimConfigValidator.Validate(config);

        act.Should().Throw<SimConfigValidationException>()
            .Which.Message.Should().Contain("raid_damage_min/max");
    }

    // ── Multiple violations reported together ─────────────────────────────────

    [Fact]
    public void MultipleViolations_AllReportedInOneException()
    {
        var config = SimConfig.Default();
        config.Disasters.DroughtProbabilityPerYear = -0.1f;   // < 0
        config.Disasters.DroughtMinSeasons = 10;
        config.Disasters.DroughtMaxSeasons = 5;               // min > max

        var act = () => SimConfigValidator.Validate(config);

        act.Should().Throw<SimConfigValidationException>()
            .Which.Violations.Should().HaveCountGreaterThan(1);
    }

    // ── Violation exception exposes violation list ────────────────────────────

    [Fact]
    public void ValidationException_ExposesViolationList()
    {
        var config = SimConfig.Default();
        config.ResourcePressure.ShortageThreshold = 0.1f;
        config.ResourcePressure.CrisisThreshold   = 0.8f;

        SimConfigValidationException? caught = null;
        try { SimConfigValidator.Validate(config); }
        catch (SimConfigValidationException ex) { caught = ex; }

        caught.Should().NotBeNull();
        caught!.Violations.Should().NotBeEmpty();
    }
}
