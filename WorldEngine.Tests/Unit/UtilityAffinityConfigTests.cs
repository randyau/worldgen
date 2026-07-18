using FluentAssertions;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Entities.Characters;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Regression pins for the UtilityAffinityConfig goal→action affinity table and
/// action need-weight table (D3 extraction). Each test asserts the loaded table
/// matches the values previously hardcoded in UtilityScorer.cs, ensuring the
/// behaviour-preserving extraction is exact.
/// </summary>
public class UtilityAffinityConfigTests
{
    // Load the production config once for the class (strict mode — catches dead keys).
    private static readonly SimConfig _cfg;

    static UtilityAffinityConfigTests()
    {
        SimConfigLoader.StrictMode = true;
        _cfg = SimConfigLoader.LoadOrCreateDefault();
    }

    // ── Goal affinity table ───────────────────────────────────────────────────

    [Fact]
    public void GoalAffinity_Survive_Rest_Is_0_8()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.GoalAffinity[(int)GoalType.Survive, 0 /* Rest */].Should().BeApproximately(0.8f, 0.001f);
    }

    [Fact]
    public void GoalAffinity_Survive_Travel_Is_0_4()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.GoalAffinity[(int)GoalType.Survive, 1 /* Travel */].Should().BeApproximately(0.4f, 0.001f);
    }

    [Fact]
    public void GoalAffinity_Dominance_War_Is_1_0()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.GoalAffinity[(int)GoalType.Dominance, 6 /* War */].Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void GoalAffinity_Dominance_Raid_Is_0_8()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.GoalAffinity[(int)GoalType.Dominance, 7 /* Raid */].Should().BeApproximately(0.8f, 0.001f);
    }

    [Fact]
    public void GoalAffinity_Alliance_Ally_Is_1_0()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.GoalAffinity[(int)GoalType.Alliance, 3 /* Ally */].Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void GoalAffinity_Alliance_Negotiate_Is_0_5()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.GoalAffinity[(int)GoalType.Alliance, 4 /* Negotiate */].Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void GoalAffinity_Avenge_Raid_Is_0_9()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.GoalAffinity[(int)GoalType.Avenge, 7 /* Raid */].Should().BeApproximately(0.9f, 0.001f);
    }

    [Fact]
    public void GoalAffinity_Avenge_War_Is_0_8()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.GoalAffinity[(int)GoalType.Avenge, 6 /* War */].Should().BeApproximately(0.8f, 0.001f);
    }

    [Fact]
    public void GoalAffinity_FoundCity_FoundCityAction_Is_1_0()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        // FoundCity action index = 11
        tables.GoalAffinity[(int)GoalType.FoundCity, 11].Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void GoalAffinity_FoundCity_Travel_Is_0_8()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.GoalAffinity[(int)GoalType.FoundCity, 1 /* Travel */].Should().BeApproximately(0.8f, 0.001f);
    }

    [Fact]
    public void GoalAffinity_Unmapped_DefaultsToZero()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        // Security has no affinity entries — all actions should be 0.
        tables.GoalAffinity[(int)GoalType.Security, 6 /* War */].Should().Be(0f);
        tables.GoalAffinity[(int)GoalType.Security, 0 /* Rest */].Should().Be(0f);
    }

    // ── Action need-weights table ─────────────────────────────────────────────

    // Need indices: 0=food, 1=safety, 2=shelter, 3=belonging, 4=status, 5=purpose, 6=spiritual

    [Fact]
    public void ActionNeeds_Rest_Food_Is_0_20()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.ActionNeedsCoeff[0 /* Rest */, 0 /* food */].Should().BeApproximately(0.20f, 0.001f);
    }

    [Fact]
    public void ActionNeeds_Rest_Safety_Is_0_20()
    {
        // Original: (2 - safety - food) * 0.2 = (1 - safety)*0.2 + (1 - food)*0.2
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.ActionNeedsCoeff[0 /* Rest */, 1 /* safety */].Should().BeApproximately(0.20f, 0.001f);
    }

    [Fact]
    public void ActionNeeds_Rest_Shelter_Is_0_15()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.ActionNeedsCoeff[0 /* Rest */, 2 /* shelter */].Should().BeApproximately(0.15f, 0.001f);
    }

    [Fact]
    public void ActionNeeds_Establish_Shelter_Is_0_70()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.ActionNeedsCoeff[2 /* Establish */, 2 /* shelter */].Should().BeApproximately(0.70f, 0.001f);
    }

    [Fact]
    public void ActionNeeds_Establish_Status_Is_0_30()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.ActionNeedsCoeff[2 /* Establish */, 4 /* status */].Should().BeApproximately(0.30f, 0.001f);
    }

    [Fact]
    public void ActionNeeds_War_Status_Is_0_70()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.ActionNeedsCoeff[6 /* War */, 4 /* status */].Should().BeApproximately(0.70f, 0.001f);
    }

    [Fact]
    public void ActionNeeds_BuildImprovement_Purpose_Is_0_50()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.ActionNeedsCoeff[10 /* BuildImprovement */, 5 /* purpose */].Should().BeApproximately(0.50f, 0.001f);
    }

    [Fact]
    public void ActionNeeds_BuildImprovement_Status_Is_0_20()
    {
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.ActionNeedsCoeff[10 /* BuildImprovement */, 4 /* status */].Should().BeApproximately(0.20f, 0.001f);
    }

    [Fact]
    public void ActionNeeds_Create_DefaultIs_0_1()
    {
        // Create has no need coefficients; only _default = 0.1 (the original fallback)
        var tables = new UtilityAffinityTables(_cfg.UtilityAffinity);
        tables.ActionNeedsDefault[8 /* Create */].Should().BeApproximately(0.1f, 0.001f);
        tables.ActionNeedsCoeff[8 /* Create */, 4 /* status */].Should().Be(0f);
    }

    // ── Strict-mode load test ─────────────────────────────────────────────────

    [Fact]
    public void ProductionToml_UtilityAffinitySection_LoadsUnderStrictMode()
    {
        // Verifies the [utility_affinity] TOML is fully bound — no dead keys.
        SimConfigLoader.StrictMode = true;
        var act = () => SimConfigLoader.LoadOrCreateDefault();
        act.Should().NotThrow("sim_config.toml must have zero unbound keys including [utility_affinity]");
    }

    [Fact]
    public void UtilityAffinityConfig_IsPartOfSimConfig()
    {
        _cfg.UtilityAffinity.Should().NotBeNull();
        _cfg.UtilityAffinity.GoalAffinity.Should().NotBeEmpty("TOML must populate goal_affinity");
        _cfg.UtilityAffinity.ActionNeeds.Should().NotBeEmpty("TOML must populate action_needs");
    }
}
