using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;
using Xunit;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M12 phase 12.1: alliance is now an Organization-to-Organization fact instead of one derived
/// from the ruler pair's personal RelationshipEdge — regression coverage for the
/// "assassinate the ruler, alliance evaporates" fragility the roadmap's design decision 1 names.
/// </summary>
public class OrganizationDiplomacyTests
{
    private static (WorldState world, Civilization civ, Tier1Character ruler) PlantCiv(WorldState world, long seedOffset)
    {
        TileCoord tile = default;
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c)) continue;
            if (world.TileGrid.GetTile(c).Fertility < 10) continue;
            if (world.Settlements.ContainsKey(c)) continue;
            tile = c;
            goto Found;
        }
        Found:
        var biome   = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var founder = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 1L + seedOffset, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(founder);

        int savedMinDist = world.SimConfig.Character.GlobalSettlementMinDist;
        world.SimConfig.Character.GlobalSettlementMinDist = 0;
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, tile), world, pending, world.SimConfig.SettlementNames);
        world.SimConfig.Character.GlobalSettlementMinDist = savedMinDist;

        var civ = world.Civilizations[world.Settlements[tile].CivId];
        return (world, civ, founder);
    }

    [Fact]
    public void AllyWith_BetweenRulers_FormsMutualOrgAlliance()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var (_, civ1, ruler1) = PlantCiv(world, seedOffset: 0L);
        var (_, civ2, ruler2) = PlantCiv(world, seedOffset: 1L);

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new AllyWith(ruler1.Id, ruler2.Id), world, pending);

        var org1 = world.Organizations[civ1.OrgId!.Value];
        var org2 = world.Organizations[civ2.OrgId!.Value];
        org1.IsAllyOf(org2.Id).Should().BeTrue();
        org2.IsAllyOf(org1.Id).Should().BeTrue();
    }

    [Fact]
    public void Alliance_SurvivesRulerSuccession()
    {
        var world = WorldTestHelper.CreateSmallWorld(seed: 42);
        var (_, civ1, ruler1) = PlantCiv(world, seedOffset: 0L);
        var (_, civ2, ruler2) = PlantCiv(world, seedOffset: 1L);

        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new AllyWith(ruler1.Id, ruler2.Id), world, pending);

        var org1 = world.Organizations[civ1.OrgId!.Value];
        var org2 = world.Organizations[civ2.OrgId!.Value];
        org1.IsAllyOf(org2.Id).Should().BeTrue("sanity check: alliance formed");

        // Force succession: ruler1 is one tick from dying of old age; a fresh adult member
        // takes the throne with no personal RelationshipEdge to ruler2 at all.
        ruler1.AgeSeason = ruler1.MaxAgeSeason - 1;
        var biome = (BiomeType)world.TileGrid.GetTile(ruler1.Location).BiomeType;
        var successor = CharacterFactory.Spawn(ruler1.Location, biome, world.WorldSeed, 900L, world.SimConfig, world.CurrentYear, startAsAdult: true);
        successor.Identity = successor.Identity with { CivId = civ1.Id };
        successor.Skills = successor.Skills with { Leadership = 1.0f };
        civ1.Members.Add(successor.Id);
        world.Entities.Add(successor);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        phase.Execute(world, world.CurrentTick, isAnnualTick: false);

        civ1.RulerId.Should().Be(successor.Id, "sanity check: succession occurred");
        org1.LeaderId.Should().Be(successor.Id, "Organization.LeaderId must track civ.RulerId across succession");
        world.Relationships.Get(successor.Id, ruler2.Id).Should().BeNull(
            "the new ruler has no personal relationship with the allied civ's ruler at all");

        org1.IsAllyOf(org2.Id).Should().BeTrue(
            "the alliance is an org-to-org fact independent of which character holds the leader seat, " +
            "so it must not evaporate just because the ruler died");
    }
}
