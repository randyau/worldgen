using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

public sealed class Tier2CharacterTests
{
    private static SimConfig DefaultConfig() => SimConfigLoader.LoadOrCreateDefault();

    // ─── Tier2Spawner ─────────────────────────────────────────────────────────

    [Fact]
    public void Tier2Spawner_EmptySettlements_ReturnsNoEvents()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var cfg   = DefaultConfig();
        // no settlements at world start — spawner should return empty
        var events = Tier2Spawner.SpawnAll(world, cfg);
        events.Should().BeEmpty();
        world.Entities.Tier2Chars.Should().BeEmpty();
    }

    [Fact]
    public void Tier2Spawner_WithSettlement_SpawnsCharacters()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var cfg   = DefaultConfig();
        cfg.Character.Tier2PerPopulation = 25; // lower threshold to guarantee at least 2 per 50-pop stub

        // Inject a stub settlement so spawner has something to work with
        var tile = FindFirstLandTile(world);
        var civ  = new CivId(1);
        world.NextCivId = 2;
        world.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), tile, 1);
        world.Settlements[tile]  = new SettlementStub(new EntityId(1), civ, tile, 1, 50, 100);

        var events = Tier2Spawner.SpawnAll(world, cfg);

        events.Should().NotBeEmpty();
        world.Entities.Tier2Chars.Should().HaveCountGreaterThan(0);
        events.Should().AllSatisfy(e => e.Type.Should().Be(EventType.CharacterBorn));
    }

    [Fact]
    public void Tier2Spawner_WithSettlement_IsDeterministic()
    {
        var world1 = WorldTestHelper.CreateSmallWorld(seed: 55);
        var world2 = WorldTestHelper.CreateSmallWorld(seed: 55);
        var cfg1   = DefaultConfig();
        var cfg2   = DefaultConfig();
        cfg1.Character.Tier2PerPopulation = 25;
        cfg2.Character.Tier2PerPopulation = 25;

        var tile = FindFirstLandTile(world1);
        var civ  = new CivId(1);
        foreach (var w in new[] { world1, world2 })
        {
            w.NextCivId = 2;
            w.Civilizations[civ] = new Civilization(civ, "TestCiv", new EntityId(1), tile, 1);
            w.Settlements[tile]  = new SettlementStub(new EntityId(1), civ, tile, 1, 50, 100);
        }

        Tier2Spawner.SpawnAll(world1, cfg1);
        Tier2Spawner.SpawnAll(world2, cfg2);

        world1.Entities.Tier2Chars.Count.Should().Be(world2.Entities.Tier2Chars.Count);
        world1.Entities.Tier2Chars[0].Name.Should()
              .Be(world2.Entities.Tier2Chars[0].Name);
    }

    // ─── NeedsVector4 ─────────────────────────────────────────────────────────

    [Fact]
    public void NeedsVector4_AnyUrgent_DetectsLowFood()
    {
        var n = new NeedsVector4(0.1f, 0.8f, 0.6f, 0.5f);
        n.AnyUrgent.Should().BeTrue();
    }

    [Fact]
    public void NeedsVector4_AnyUrgent_FalseWhenAllSatisfied()
    {
        var n = NeedsVector4.Default;
        n.AnyUrgent.Should().BeFalse();
    }

    // ─── PersonalityVector6 ───────────────────────────────────────────────────

    [Fact]
    public void PersonalityVector6_DefaultValues_AreHalf()
    {
        var p = PersonalityVector6.Default;
        new[] { p.Ambition, p.Loyalty, p.Diligence,
                p.Sociability, p.Cunning, p.Rationality }
            .Should().AllBeEquivalentTo(0.5f);
    }

    // ─── EntityRegistry Tier2 support ────────────────────────────────────────

    [Fact]
    public void EntityRegistry_AddRemoveTier2_UpdatesTier2List()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 1);
        var tile  = FindFirstLandTile(world);

        var c = new Tier2Character(
            EntityId.New(), tile, "Mira",
            PersonalityVector6.Default,
            new LivelihoodData(Tier2Role.Merchant, null, tile, 0.5f),
            maxHealth: 100, maxAgeSeason: 80);

        world.Entities.Add(c);
        world.Entities.Tier2Chars.Should().Contain(c);
        world.Entities.All.Should().ContainKey(c.Id);

        world.Entities.Remove(c.Id);
        world.Entities.Tier2Chars.Should().NotContain(c);
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static TileCoord FindFirstLandTile(Sim.World.WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        for (int y = 1; y < h - 1; y++)
        for (int x = 0; x < w; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        return new TileCoord(0, 0);
    }
}
