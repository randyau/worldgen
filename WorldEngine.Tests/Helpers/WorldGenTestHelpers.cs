using System;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Tests.Helpers;

/// <summary>
/// Shared land-tile lookup and Tier1/Tier2 spawn helpers, consolidated out of ~40 near-identical
/// private per-file copies (M15.9 test-scaffolding dedup). Covers every real variant seen across
/// the test suite via optional parameters; call sites that used a genuinely different search
/// algorithm (not just different parameters) were left as local helpers — see
/// UtilityScorerSpotlightBiasTests.FindLandTileWithLandNeighbor and AuthoringTests.FindOceanTile.
/// </summary>
public static class WorldGenTestHelpers
{
    /// <summary>
    /// Scans the tile grid for a land tile, optionally excluding a radius around one or two other
    /// tiles (for tests that need two settlements apart) and/or skipping volcanic tiles.
    /// </summary>
    /// <param name="fullRange">
    /// When false (default), the top and bottom rows (y=0 and y=h-1) are skipped — most tests use
    /// this to avoid pole-adjacent edge artifacts. A few tests scan every row; pass true for those.
    /// </param>
    public static TileCoord FindLandTile(
        WorldState world,
        TileCoord? exclude = null,
        int minDist = 0,
        TileCoord? exclude2 = null,
        bool nonVolcanic = false,
        bool fullRange = false)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        int yStart = fullRange ? 0 : 1;
        int yEnd   = fullRange ? h : h - 1;
        for (int y = yStart; y < yEnd; y++)
        for (int x = 0; x < w; x++)
        {
            var c = new TileCoord(x, y);
            if (!world.IsLand(c)) continue;
            if (nonVolcanic && world.TileGrid.GetTile(c).StaticFlags.HasFlag(TileStaticFlags.IsVolcanic)) continue;
            if (exclude is { } e)
            {
                int dx = c.X - e.X, dy = c.Y - e.Y;
                if (dx * dx + dy * dy < minDist * minDist) continue;
            }
            if (exclude2 is { } e2)
            {
                int dx2 = c.X - e2.X, dy2 = c.Y - e2.Y;
                if (dx2 * dx2 + dy2 * dy2 < minDist * minDist) continue;
            }
            return c;
        }
        throw new InvalidOperationException("No suitable land tile found");
    }

    /// <summary>Spawns an adult Tier1Character at the given tile and adds it to the world.</summary>
    public static Tier1Character SpawnAt(WorldState world, TileCoord tile, long seedOffset)
    {
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;
        var c = CharacterFactory.Spawn(tile, biome, world.WorldSeed, seedOffset, world.SimConfig, world.CurrentYear, startAsAdult: true);
        world.Entities.Add(c);
        return c;
    }

    /// <summary>Spawns a Tier1Character and establishes it as the ruler/founder of a brand-new civ.</summary>
    public static (Tier1Character ruler, CivId civId) SpawnRuler(WorldState world, TileCoord tile, long seedOffset, string civName)
    {
        var ruler = SpawnAt(world, tile, seedOffset);
        var civId = new CivId(world.NextCivId++);
        world.Civilizations[civId] = new Civilization(civId, civName, ruler.Id, tile, world.CurrentYear);
        CivTracker.SetCharacterCiv(ruler, civId, OrganizationRole.Leader, world);
        return (ruler, civId);
    }

    /// <summary>Spawns a Tier1Character and enrolls it as a rank-and-file member of an existing civ.</summary>
    public static Tier1Character SpawnMember(WorldState world, TileCoord tile, long seedOffset, CivId civId)
    {
        var c = SpawnAt(world, tile, seedOffset);
        CivTracker.SetCharacterCiv(c, civId, OrganizationRole.Member, world);
        return c;
    }

    /// <summary>Spawns a bare-bones Tier2Character (Merchant livelihood) at the given tile.</summary>
    public static Tier2Character SpawnTier2At(WorldState world, TileCoord tile, string name)
    {
        var c = new Tier2Character(EntityId.New(), tile, name, PersonalityVector6.Default,
            new LivelihoodData(Tier2Role.Merchant, null, tile, 0.5f), maxHealth: 100, maxAgeSeason: 800);
        world.Entities.Add(c);
        return c;
    }
}
