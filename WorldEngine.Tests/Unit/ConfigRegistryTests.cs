using FluentAssertions;
using WorldEngine.Sim.Config;

namespace WorldEngine.Tests.Unit;

public class ConfigRegistryTests
{
    [Fact]
    public void Build_FindsKnownLeafEntry_WithCorrectKindAndDefault()
    {
        var live = SimConfig.Default();
        var defaults = SimConfig.Default();

        var entries = ConfigRegistry.Build(live, defaults);

        var seaLevel = entries.Should().ContainSingle(e => e.Key == "WorldGen.Ocean.DefaultSeaLevel").Subject;
        seaLevel.Kind.Should().Be(ConfigValueKind.Float);
        seaLevel.Default.Should().Be(defaults.WorldGen.Ocean.DefaultSeaLevel);
        seaLevel.Get().Should().Be(live.WorldGen.Ocean.DefaultSeaLevel);
        seaLevel.IsModified.Should().BeFalse();
    }

    [Fact]
    public void Build_SkipsAncestryRegistryAndCollectionProperties()
    {
        var entries = ConfigRegistry.Build(SimConfig.Default(), SimConfig.Default());

        entries.Should().NotContain(e => e.Group == nameof(SimConfig.AncestryRegistry));
        entries.Should().NotContain(e => e.Path.Contains("FirstNames") || e.Path.Contains("Suffixes"));
    }

    [Fact]
    public void Entry_Set_WritesToLiveInstance_NotDefaultSnapshot()
    {
        var live = SimConfig.Default();
        var defaults = SimConfig.Default();
        var entries = ConfigRegistry.Build(live, defaults);
        var seaLevel = entries.Single(e => e.Key == "WorldGen.Ocean.DefaultSeaLevel");

        seaLevel.Set((object)0.9f);

        live.WorldGen.Ocean.DefaultSeaLevel.Should().Be(0.9f);
        defaults.WorldGen.Ocean.DefaultSeaLevel.Should().Be((float)seaLevel.Default);
        seaLevel.IsModified.Should().BeTrue();
    }

    [Fact]
    public void Build_ProducesUniqueKeysAcrossEntireConfig()
    {
        var entries = ConfigRegistry.Build(SimConfig.Default(), SimConfig.Default());

        entries.Should().NotBeEmpty();
        entries.Select(e => e.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Build_IsIndependentOfWhichInstanceIsPassedAsDefaults()
    {
        var live = SimConfig.Default();
        var defaults = SimConfig.Default();
        defaults.Character.InitialCount = 999;

        var entries = ConfigRegistry.Build(live, defaults);
        var initialCount = entries.Single(e => e.Key == "Character.InitialCount");

        initialCount.Default.Should().Be(999);
        initialCount.Get().Should().Be(live.Character.InitialCount);
        initialCount.IsModified.Should().BeTrue();
    }
}
