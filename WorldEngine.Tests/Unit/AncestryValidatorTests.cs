using FluentAssertions;
using WorldEngine.Sim.Config;

namespace WorldEngine.Tests.Unit;

public class AncestryValidatorTests
{
    private static AncestryConfig Valid(string id = "human") => new()
    {
        Id = id,
        DisplayName = "Human",
        MinLifespanSeasons = 60,
        MaxLifespanSeasons = 200,
    };

    [Fact]
    public void Validate_AcceptsWellFormedAncestryList()
    {
        var act = () => AncestryValidator.Validate([Valid()]);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsDuplicateIds()
    {
        var act = () => AncestryValidator.Validate([Valid("elf"), Valid("elf")]);
        act.Should().Throw<AncestryValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("duplicate ancestry id"));
    }

    [Fact]
    public void Validate_RejectsBlankId()
    {
        var act = () => AncestryValidator.Validate([Valid("")]);
        act.Should().Throw<AncestryValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("id must not be blank"));
    }

    [Fact]
    public void Validate_RejectsInvertedLifespanRange()
    {
        var cfg = Valid();
        cfg.MinLifespanSeasons = 300;
        cfg.MaxLifespanSeasons = 100;

        var act = () => AncestryValidator.Validate([cfg]);
        act.Should().Throw<AncestryValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("min_lifespan_seasons") && v.Contains("max_lifespan_seasons"));
    }

    [Fact]
    public void Validate_RejectsUnknownBiomeInSpawnWeights()
    {
        var cfg = Valid();
        cfg.SpawnWeights["not_a_biome"] = 1f;

        var act = () => AncestryValidator.Validate([cfg]);
        act.Should().Throw<AncestryValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("unknown biome"));
    }

    [Fact]
    public void Validate_RejectsUnknownAncestryReferenceInFirstMeetingTrust()
    {
        var cfg = Valid();
        cfg.FirstMeetingTrust["ghost_ancestry"] = 0.1f;

        var act = () => AncestryValidator.Validate([cfg]);
        act.Should().Throw<AncestryValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("unknown ancestry 'ghost_ancestry'"));
    }

    [Fact]
    public void Validate_RejectsOutOfRangeTrust()
    {
        var elf = Valid("elf");
        var cfg = Valid();
        cfg.FirstMeetingTrust["elf"] = 5f;

        var act = () => AncestryValidator.Validate([cfg, elf]);
        act.Should().Throw<AncestryValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("first_meeting_trust.elf") && v.Contains("[-1, 1]"));
    }

    [Fact]
    public void Validate_RejectsOutOfRangeCulturalDistance()
    {
        var elf = Valid("elf");
        var cfg = Valid();
        cfg.CulturalDistance["elf"] = 1.5f;

        var act = () => AncestryValidator.Validate([cfg, elf]);
        act.Should().Throw<AncestryValidationException>()
            .Which.Violations.Should().Contain(v => v.Contains("cultural_distance.elf") && v.Contains("[0, 1]"));
    }

    [Fact]
    public void Validate_RealAncestriesToml_PassesValidation()
    {
        var act = () => AncestryLoader.LoadOrDefault();
        act.Should().NotThrow();
    }
}
