using FluentAssertions;
using WorldEngine.Sim.Config;
using WorldEngine.Tests.Helpers;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using Xunit;

namespace WorldEngine.Tests.Unit;

public sealed class CharacterTests
{
    private static SimConfig DefaultConfig() => SimConfigLoader.LoadOrCreateDefault();

    // ─── CharacterFactory ─────────────────────────────────────────────────────

    [Fact]
    public void Factory_ProducesCharacterWithValidRanges()
    {
        var cfg = DefaultConfig();
        var c = CharacterFactory.Spawn(new TileCoord(5, 5), worldSeed: 42, entitySeq: 10_000, cfg, birthYear: 1);

        c.Location.Should().Be(new TileCoord(5, 5));
        c.IsAlive.Should().BeTrue();
        c.Health.Should().BeInRange(1, cfg.Character.MaxHealth);

        // All personality traits in [0.1, 0.9]
        var p = c.Personality;
        new[] { p.Ambition, p.Greed, p.Aggression, p.Compassion,
                p.Curiosity, p.Creativity, p.Rationality, p.Wonder,
                p.Loyalty, p.Sociability, p.Honesty, p.Stability }
            .Should().AllSatisfy(v => v.Should().BeInRange(0.1f, 0.9f));

        // All aptitude traits in [0.1, 0.9]
        var a = c.Aptitude;
        new[] { a.Diligence, a.Focus, a.Perfectionism, a.Composure, a.Acuity, a.Ingenuity }
            .Should().AllSatisfy(v => v.Should().BeInRange(0.1f, 0.9f));

        // All starting skills in [0.01, 0.2]
        var s = c.Skills;
        new[] { s.Combat, s.Leadership, s.Administration, s.Diplomacy,
                s.Crafting, s.Knowledge, s.Stealth, s.Piety }
            .Should().AllSatisfy(v => v.Should().BeInRange(0.01f, 0.2f));
    }

    [Fact]
    public void Factory_IsDeterministic()
    {
        var cfg = DefaultConfig();
        var c1 = CharacterFactory.Spawn(new TileCoord(3, 7), worldSeed: 12345, entitySeq: 10_000, cfg, birthYear: 1);
        var c2 = CharacterFactory.Spawn(new TileCoord(3, 7), worldSeed: 12345, entitySeq: 10_000, cfg, birthYear: 1);

        c1.Personality.Should().Be(c2.Personality);
        c1.Aptitude.Should().Be(c2.Aptitude);
        c1.Identity.Name.Should().Be(c2.Identity.Name);
        c1.MaxAgeSeason.Should().Be(c2.MaxAgeSeason);
    }

    [Fact]
    public void Factory_DifferentSeqsProduceDifferentPersonalities()
    {
        var cfg = DefaultConfig();
        var c1 = CharacterFactory.Spawn(new TileCoord(0, 0), worldSeed: 111, entitySeq: 10_000, cfg, birthYear: 1);
        var c2 = CharacterFactory.Spawn(new TileCoord(0, 0), worldSeed: 111, entitySeq: 10_001, cfg, birthYear: 1);

        // Two different entity sequences shouldn't produce identical personalities
        c1.Personality.Should().NotBe(c2.Personality);
    }

    [Fact]
    public void Factory_AgeIsWithinConfiguredBounds()
    {
        var cfg = DefaultConfig();
        for (int i = 0; i < 50; i++)
        {
            var c = CharacterFactory.Spawn(new TileCoord(0, 0), worldSeed: i * 17, entitySeq: 10_000 + i, cfg, birthYear: 1);
            c.MaxAgeSeason.Should().BeInRange(cfg.Character.MaxAgeSeasonsMin, cfg.Character.MaxAgeSeasonsMax);
        }
    }

    // ─── NeedsVector ──────────────────────────────────────────────────────────

    [Fact]
    public void NeedsVector_MostUrgentUnmet_ReturnsLowest()
    {
        var n = new NeedsVector(0.8f, 0.1f, 0.7f, 0.6f, 0.5f, 0.4f, 0.3f);
        n.MostUrgentUnmet().Should().Be("Food"); // Food = 0.1, below threshold
    }

    [Fact]
    public void NeedsVector_MostUrgentUnmet_ReturnsNullWhenAllSatisfied()
    {
        var n = NeedsVector.Default;
        n.MostUrgentUnmet().Should().BeNull();
    }

    // ─── RelationshipGraph ────────────────────────────────────────────────────

    [Fact]
    public void RelationshipGraph_GetOrCreate_ReturnsDefaultEdge()
    {
        var graph = new RelationshipGraph();
        var a = new EntityId(1);
        var b = new EntityId(2);

        var edge = graph.GetOrCreate(a, b);
        edge.Trust.Should().Be(0f);
        edge.IsAlly.Should().BeFalse();
    }

    [Fact]
    public void RelationshipGraph_CanonicalKey_IsSymmetric()
    {
        var graph = new RelationshipGraph();
        var a = new EntityId(1);
        var b = new EntityId(2);

        graph.Upsert(graph.GetOrCreate(a, b) with { Trust = 0.5f });

        graph.Get(a, b)!.Trust.Should().Be(0.5f);
        graph.Get(b, a)!.Trust.Should().Be(0.5f); // same edge, canonical key
    }

    [Fact]
    public void RelationshipGraph_AllianceFlags_Work()
    {
        var graph = new RelationshipGraph();
        var a = new EntityId(3);
        var b = new EntityId(4);

        var edge = graph.GetOrCreate(a, b);
        graph.Upsert(edge with { Flags = edge.Flags | RelationshipFlags.IsAlly });

        graph.Get(a, b)!.IsAlly.Should().BeTrue();
        graph.Get(a, b)!.IsRival.Should().BeFalse();
    }

    // ─── CharacterSpawner ─────────────────────────────────────────────────────

    [Fact]
    public void CharacterSpawner_SpawnAll_ReturnsBornEvents()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 99);
        var cfg   = DefaultConfig();
        cfg.Character.MinFertilityToSettle = 0; // any land tile qualifies in the test world

        var events = CharacterSpawner.SpawnAll(world, cfg);

        events.Should().NotBeEmpty();
        events.Should().AllSatisfy(e => e.Type.Should().Be(EventType.CharacterBorn));
    }

    [Fact]
    public void CharacterSpawner_SpawnAll_IsDeterministic()
    {
        var cfg1 = DefaultConfig();
        var cfg2 = DefaultConfig();
        cfg1.Character.MinFertilityToSettle = 0;
        cfg2.Character.MinFertilityToSettle = 0;

        var world1 = WorldTestHelper.CreateSmallWorld(seed: 77);
        var world2 = WorldTestHelper.CreateSmallWorld(seed: 77);

        CharacterSpawner.SpawnAll(world1, cfg1);
        CharacterSpawner.SpawnAll(world2, cfg2);

        world1.Entities.Characters.Count.Should().Be(world2.Entities.Characters.Count);
        world1.Entities.Characters.Count.Should().BeGreaterThan(0);
        world1.Entities.Characters[0].Personality.Should()
              .Be(world2.Entities.Characters[0].Personality);
    }
}
