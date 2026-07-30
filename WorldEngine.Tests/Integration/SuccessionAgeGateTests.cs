using FluentAssertions;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Simulation.Phases;
using WorldEngine.Sim.World;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Integration;

/// <summary>
/// Regression coverage for the "infant monarch" bug: succession used to promote the
/// highest-scoring living civ member with no age check at all, so a same-tick newborn
/// could inherit the throne. Fixed by gating candidates on CharacterSimConfig.MinRulerAgeSeasons
/// (CharacterBehaviorPhase.KillCharacter) and by starting "leader emerges from the population"
/// spawns (civ founding, secession, ruler backfill) at a randomized adult age instead of
/// AgeSeason 0 (CharacterFactory.Spawn's startAsAdult flag).
/// </summary>
public class SuccessionAgeGateTests
{
    private static TileCoord FindLandTile(WorldState world)
    {
        for (int y = 1; y < world.TileGrid.TileHeight - 1; y++)
        for (int x = 0; x < world.TileGrid.TileWidth; x++)
        {
            var c = new TileCoord(x, y);
            if (world.IsLand(c)) return c;
        }
        throw new InvalidOperationException("No land tile found");
    }

    private static (WorldState world, Civilization civ, TileCoord tile) PlantCiv(int seed)
    {
        var world = WorldTestHelper.CreateSmallWorld(seed);
        var tile  = FindLandTile(world);
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;

        var founder = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 1L, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(founder);
        var pending = new List<PendingEvent>();
        CivTracker.Resolve(new EstablishSettlement(founder.Id, tile), world, pending, world.SimConfig.SettlementNames);
        var civ = world.Civilizations[world.Settlements[tile].CivId];

        return (world, civ, tile);
    }

    [Fact]
    public void Succession_SkipsInfantMember_PromotesEligibleAdultInstead()
    {
        var (world, civ, tile) = PlantCiv(seed: 7);
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;

        // Ruler is one tick from dying of old age.
        var ruler = world.GetEntity(civ.RulerId) as Tier1Character;
        ruler!.AgeSeason = ruler.MaxAgeSeason - 1;

        // Infant member: outscores the adult on the succession heuristic, but is far below
        // MinRulerAgeSeasons — must be skipped.
        // Leadership is set to the extremes (1.0 vs 0.0) so the infant outscores the adult on
        // the succession heuristic `(Aggression + Leadership) * 0.5` regardless of Aggression's
        // seeded-random value (clamped to [0.1, 0.9], so a 1.0-point Leadership gap always wins).
        var infant = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 2L, world.SimConfig, world.CurrentYear);
        infant.Identity = infant.Identity with { CivId = civ.Id };
        infant.AgeSeason = 0;
        infant.Skills = infant.Skills with { Leadership = 1.0f };
        civ.Members.Add(infant.Id);
        world.Entities.Add(infant);

        // Adult member: clears MinRulerAgeSeasons but scores lower — should still win.
        var adult = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 3L, world.SimConfig, world.CurrentYear, startAsAdult: true);
        adult.Identity = adult.Identity with { CivId = civ.Id };
        adult.Skills = adult.Skills with { Leadership = 0.0f };
        civ.Members.Add(adult.Id);
        world.Entities.Add(adult);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        phase.Execute(world, world.CurrentTick, isAnnualTick: false);

        civ.RulerId.Should().Be(adult.Id,
            "the infant scores higher but is below MinRulerAgeSeasons and must be skipped");
    }

    [Fact]
    public void Succession_NoEligibleMembers_LeavesRulerIdUnchanged()
    {
        var (world, civ, tile) = PlantCiv(seed: 11);
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;

        var ruler = world.GetEntity(civ.RulerId) as Tier1Character;
        ruler!.AgeSeason = ruler.MaxAgeSeason - 1;
        var deadRulerId = civ.RulerId;

        // Only an infant remains — no successor should be assigned this tick (falls through to
        // the existing succession-crisis path instead of crowning a newborn).
        var infant = CharacterFactory.Spawn(tile, biome, world.WorldSeed, 2L, world.SimConfig, world.CurrentYear);
        infant.Identity = infant.Identity with { CivId = civ.Id };
        infant.AgeSeason = 0;
        civ.Members.Add(infant.Id);
        world.Entities.Add(infant);

        var phase = new CharacterBehaviorPhase(world.SimConfig);
        phase.Execute(world, world.CurrentTick, isAnnualTick: false);

        civ.TotalSuccessions.Should().Be(0, "no member cleared MinRulerAgeSeasons, so no succession should occur");
        civ.RulerId.Should().Be(deadRulerId, "RulerId is left pointing at the deceased ruler until the succession-crisis path resolves it");
    }
}
