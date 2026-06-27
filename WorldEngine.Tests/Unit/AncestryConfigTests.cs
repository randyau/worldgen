using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;

namespace WorldEngine.Tests.Unit;

/// <summary>Tests for M3.5 AncestryConfig cultural descriptor fields and CivTracker naming helpers.</summary>
public class AncestryConfigTests
{
    private static AncestryRegistry LoadRegistry() =>
        AncestryLoader.LoadOrDefault();

    // ─── 3.5.1.1 Field loading ────────────────────────────────────────────────

    [Fact]
    public void AncestryConfig_LoadsNewFields_FromToml()
    {
        var registry = LoadRegistry();

        var dwarf = registry.Get("dwarf");
        dwarf.Should().NotBeNull();
        dwarf!.ArchitecturalStyle.Should().Be("hewn-stone");
        dwarf.SettlementDescriptor.Should().Be("hold");
        dwarf.CivNameSuffix.Should().Be("Hold");
        dwarf.ArtisticTraditions.Should().Contain("metalwork");
        dwarf.BiomeAdaptations.Should().NotBeEmpty();
        dwarf.ImprovementDescriptors.Should().NotBeEmpty();

        var elf = registry.Get("elf");
        elf.Should().NotBeNull();
        elf!.ArchitecturalStyle.Should().Be("woven-wood");
        elf.SettlementDescriptor.Should().Be("grove");
        elf.CivNameSuffix.Should().Be("Covenant");
        elf.ArtisticTraditions.Should().Contain("tapestry");

        var human = registry.Get("human");
        human.Should().NotBeNull();
        human!.SettlementDescriptor.Should().Be("town");
        human.CivNameSuffix.Should().Be("Domain");
    }

    // ─── 3.5.1.2 All ancestries populated ────────────────────────────────────

    [Fact]
    public void AncestryConfig_AllAncestriesHaveSettlementDescriptor()
    {
        var registry = LoadRegistry();

        foreach (var anc in registry.All)
        {
            anc.SettlementDescriptor.Should()
                .NotBeNullOrEmpty($"ancestry '{anc.Id}' must have a settlement_descriptor");
        }
    }

    [Fact]
    public void AncestryConfig_AllAncestriesHaveArchitecturalStyle()
    {
        var registry = LoadRegistry();

        foreach (var anc in registry.All)
        {
            anc.ArchitecturalStyle.Should()
                .NotBeNullOrEmpty($"ancestry '{anc.Id}' must have an architectural_style");
        }
    }

    [Fact]
    public void AncestryConfig_AllAncestriesHaveCivNameSuffix()
    {
        var registry = LoadRegistry();

        foreach (var anc in registry.All)
        {
            anc.CivNameSuffix.Should()
                .NotBeNullOrEmpty($"ancestry '{anc.Id}' must have a civ_name_suffix");
        }
    }

    [Fact]
    public void AncestryConfig_AllAncestriesHaveArtisticTraditions()
    {
        var registry = LoadRegistry();

        foreach (var anc in registry.All)
        {
            anc.ArtisticTraditions.Should()
                .NotBeEmpty($"ancestry '{anc.Id}' must have at least one artistic_tradition");
        }
    }

    // ─── 3.5.2 ApplyCulturalSettlementName ───────────────────────────────────

    [Fact]
    public void ApplyCulturalSettlementName_ReplacesDomainSuffix_ForDwarf()
    {
        var registry = LoadRegistry();

        string result = CivTracker.ApplyCulturalSettlementName("Iron Domain", "dwarf", registry);

        result.Should().Be("Iron hold");
    }

    [Fact]
    public void ApplyCulturalSettlementName_LeavesNonDomainNames_Unchanged()
    {
        var registry = LoadRegistry();

        string result = CivTracker.ApplyCulturalSettlementName("Ironhold", "dwarf", registry);

        result.Should().Be("Ironhold");
    }

    [Fact]
    public void ApplyCulturalSettlementName_ReturnsBaseName_WhenAncestryUnknown()
    {
        var registry = LoadRegistry();

        string result = CivTracker.ApplyCulturalSettlementName("Some Domain", "unknown_id", registry);

        result.Should().Be("Some Domain");
    }

    [Fact]
    public void ApplyCulturalSettlementName_ReturnsBaseName_WhenRegistryNull()
    {
        string result = CivTracker.ApplyCulturalSettlementName("Some Domain", "dwarf", null);

        result.Should().Be("Some Domain");
    }

    // ─── 3.5.2 GetCivNameSuffix ──────────────────────────────────────────────

    [Fact]
    public void GetCivNameSuffix_ReturnsCulturalSuffix_ForKnownAncestry()
    {
        var registry = LoadRegistry();

        CivTracker.GetCivNameSuffix("elf",      registry).Should().Be("Covenant");
        CivTracker.GetCivNameSuffix("dwarf",    registry).Should().Be("Hold");
        CivTracker.GetCivNameSuffix("orc",      registry).Should().Be("Warband");
        CivTracker.GetCivNameSuffix("halfling", registry).Should().Be("Shire");
        CivTracker.GetCivNameSuffix("dark_elf", registry).Should().Be("Ascendancy");
        CivTracker.GetCivNameSuffix("human",    registry).Should().Be("Domain");
    }

    [Fact]
    public void GetCivNameSuffix_FallsBackToDomain_WhenAncestryUnknown()
    {
        var registry = LoadRegistry();

        CivTracker.GetCivNameSuffix("unknown", registry).Should().Be("Domain");
        CivTracker.GetCivNameSuffix(null,      registry).Should().Be("Domain");
        CivTracker.GetCivNameSuffix("dwarf",   null     ).Should().Be("Domain");
    }

    // ─── 3.5.2 BuildCulturalProfile ──────────────────────────────────────────

    [Fact]
    public void BuildCulturalProfile_PopulatesFieldsFromAncestry()
    {
        var registry = LoadRegistry();

        var profile = CivTracker.BuildCulturalProfile(
            "dwarf", WorldEngine.Sim.Core.BiomeType.Mountain, registry, []);

        profile.AncestryId.Should().Be("dwarf");
        profile.ArchitecturalStyle.Should().Be("hewn-stone");
        profile.SettlementDescriptor.Should().Be("hold");
        profile.ArtisticTraditions.Should().Contain("metalwork");
        profile.DominantBiome.Should().Be("Mountain");
        profile.ActiveTraits.Should().BeEmpty();
    }

    [Fact]
    public void BuildCulturalProfile_HandlesUnknownAncestry_Gracefully()
    {
        var registry = LoadRegistry();

        var profile = CivTracker.BuildCulturalProfile(
            "unknown_race", WorldEngine.Sim.Core.BiomeType.Grassland, registry, ["Militaristic"]);

        profile.AncestryId.Should().Be("unknown_race");
        profile.ArchitecturalStyle.Should().BeEmpty();
        profile.ActiveTraits.Should().Contain("Militaristic");
    }
}
