using WorldEngine.Sim.Config;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M15.x namespace expansion: syllable-based given-name/surname generation replacing the old
/// flat first-name-list lookup that visibly repeated once population climbed into the thousands
/// over millennia-long runs.
/// </summary>
public class NameGeneratorTests
{
    private static AncestryRegistry LoadRegistry() => AncestryLoader.LoadOrDefault();
    private static readonly CharacterNamesConfig Fallback = new();

    [Fact]
    public void GenerateGivenName_SameSeedAndSeq_ProducesSameName()
    {
        var human = LoadRegistry().GetOrHuman("human");

        string a = NameGenerator.GenerateGivenName(human, Fallback, worldSeed: 42, seq: 7, salt: 440);
        string b = NameGenerator.GenerateGivenName(human, Fallback, worldSeed: 42, seq: 7, salt: 440);

        a.Should().Be(b);
    }

    [Fact]
    public void GenerateSurname_SameSeedAndSeq_ProducesSameName()
    {
        var human = LoadRegistry().GetOrHuman("human");

        string a = NameGenerator.GenerateSurname(human, Fallback, worldSeed: 42, seq: 7, salt: 444);
        string b = NameGenerator.GenerateSurname(human, Fallback, worldSeed: 42, seq: 7, salt: 444);

        a.Should().Be(b);
    }

    [Fact]
    public void GenerateGivenName_AcrossManySeqValues_YieldsFarFewerCollisionsThanOldFlatPool()
    {
        var human = LoadRegistry().GetOrHuman("human");

        var names = Enumerable.Range(0, 2000)
            .Select(seq => NameGenerator.GenerateGivenName(human, Fallback, worldSeed: 1, seq: seq, salt: 440))
            .ToList();

        // The old flat pool had ~50 entries — 2000 rolls would collide constantly (~40x each).
        // The syllable generator should yield hundreds of distinct combinations.
        names.Distinct().Count().Should().BeGreaterThan(200);
    }

    [Fact]
    public void GenerateSurname_CombinedWithGivenName_FurtherExpandsNamespace()
    {
        var human = LoadRegistry().GetOrHuman("human");

        var fullNames = Enumerable.Range(0, 2000)
            .Select(seq =>
                NameGenerator.GenerateGivenName(human, Fallback, worldSeed: 1, seq: seq, salt: 440) + " " +
                NameGenerator.GenerateSurname(human, Fallback, worldSeed: 1, seq: seq, salt: 444))
            .ToList();

        fullNames.Distinct().Count().Should().BeGreaterThan(1000);
    }

    [Fact]
    public void AllAncestries_HaveNonEmptySyllablePools()
    {
        var registry = LoadRegistry();

        foreach (var anc in registry.All)
        {
            anc.NameOnsets.Should().NotBeEmpty($"ancestry '{anc.Id}' must have name_onsets");
            anc.NameCodas.Should().NotBeEmpty($"ancestry '{anc.Id}' must have name_codas");
            anc.SurnameOnsets.Should().NotBeEmpty($"ancestry '{anc.Id}' must have surname_onsets");
            anc.SurnameCodas.Should().NotBeEmpty($"ancestry '{anc.Id}' must have surname_codas");
            anc.Epithets.Should().NotBeEmpty($"ancestry '{anc.Id}' must have epithets");
        }
    }

    [Fact]
    public void CharacterFactory_SpawnChild_InheritsMothersSurname()
    {
        var config = TestSimConfig.Default();
        var mother = CharacterFactory.Spawn(new WorldEngine.Sim.Core.TileCoord(0, 0), worldSeed: 5, entitySeq: 1, config, birthYear: 0, startAsAdult: true);
        var father = CharacterFactory.Spawn(new WorldEngine.Sim.Core.TileCoord(0, 0), worldSeed: 5, entitySeq: 2, config, birthYear: 0, startAsAdult: true);

        var child = CharacterFactory.SpawnChild(mother, father, new WorldEngine.Sim.Core.TileCoord(0, 0),
            WorldEngine.Sim.Core.BiomeType.Grassland, worldSeed: 5, entitySeq: 3, config, birthYear: 10);

        child.Identity.Surname.Should().Be(mother.Identity.Surname);
    }
}
