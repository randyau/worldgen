using System.Reflection;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// M7's "bias-not-override" spotlight policy (UtilityScorer.ApplySpotlightBias) had zero test
/// coverage. The method is `private static` and operates on a hand-built candidate list rather
/// than the full BuildCandidates() pipeline, so reconstructing a realistic scenario through the
/// public SelectAction() API would mean fighting BestAdjacentTile/wanderlust/softmax randomness
/// just to get specific candidates into the list. Since ApplySpotlightBias is a pure function
/// (mutates the list it's given, no other side effects), reflection is the direct and simplest
/// way to unit test it in isolation at the granularity the audit called for.
/// </summary>
public class UtilityScorerSpotlightBiasTests
{
    private const float ExpectedBias = 3.0f; // UtilityScorer.SpotlightIntentBias — kept in sync manually; if this drifts, the assertions below will fail loudly

    private static WorldState BuildWorld(int seed = 1)
    {
        var cfg = new WorldConfig { Seed = seed, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
        var sim = TestSimConfig.Default();
        var ctx = new WorldGenContext(cfg, sim);
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.River     = new RiverLayer().Generate(ctx);
        ctx.Magic     = new MagicLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        ctx.Biome     = new BiomeLayer().Generate(ctx);
        ctx.Resource  = new ResourceLayer().Generate(ctx);
        ctx.Poi       = new PoiCandidateLayer().Generate(ctx);
        return TileGridAssembler.Assemble(ctx);
    }

    private static TileCoord FindLandTileWithLandNeighbor(WorldState world, out TileCoord neighborStep)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };

        for (int y = 1; y < h - 1; y++)
            for (int x = 0; x < w; x++)
            {
                var c = new TileCoord(x, y);
                if (!world.IsLand(c)) continue;
                for (int i = 0; i < 4; i++)
                {
                    int nx = ((c.X + dx[i]) % w + w) % w;
                    int ny = Math.Clamp(c.Y + dy[i], 0, h - 1);
                    var n = new TileCoord(nx, ny);
                    if (world.IsLand(n))
                    {
                        neighborStep = n;
                        return c;
                    }
                }
            }
        throw new InvalidOperationException("No land tile with a land neighbor found — widen the search or change seed.");
    }

    private static Tier1Character MakeTier1(TileCoord loc, EntityId id) => new(
        id, loc,
        PersonalityVector.Default, AptitudeVector.Default, SkillVector.Default,
        new IdentityData("Test", "the Tester", "test", null, null, default, 0, 0),
        100, 200);

    /// <summary>Mirrors UtilityScorer's private static StepToward exactly (4-neighbor, min squared
    /// distance among land tiles) so the test can compute the expected biased destination
    /// independently, without reflecting a second private method.</summary>
    private static TileCoord StepToward(TileCoord from, TileCoord to, WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };
        TileCoord? best = null;
        int bestDistSq = int.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            int nx = ((from.X + dx[i]) % w + w) % w;
            int ny = Math.Clamp(from.Y + dy[i], 0, h - 1);
            var cand = new TileCoord(nx, ny);
            if (!world.IsLand(cand)) continue;
            int ddx = cand.X - to.X, ddy = cand.Y - to.Y;
            int distSq = ddx * ddx + ddy * ddy;
            if (distSq < bestDistSq) { bestDistSq = distSq; best = cand; }
        }
        return best!.Value;
    }

    private static void ApplySpotlightBias(List<UtilityScorer.ScoredAction> candidates, WorldState world, Tier1Character c)
    {
        var method = typeof(UtilityScorer).GetMethod("ApplySpotlightBias", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("UtilityScorer.ApplySpotlightBias must exist as a private static method — if this fails, the method was renamed/removed and this test needs updating alongside it");
        method!.Invoke(null, new object[] { candidates, world, c });
    }

    [Fact]
    public void MoveTarget_BiasesOnlyTheMatchingMoveAction()
    {
        var world = BuildWorld();
        var start = FindLandTileWithLandNeighbor(world, out var matchingStep);
        var character = MakeTier1(start, new EntityId(920));
        world.Entities.Add(character);

        // Spotlight intent: move toward a far-off target — StepToward resolves the single best
        // adjacent land tile toward it, independent of which exact target we pick, as long as
        // `matchingStep` really is that best step (guaranteed since FindLandTileWithLandNeighbor
        // found `start`'s actual land neighbor and we point the intent straight through it).
        var farTarget = new TileCoord(matchingStep.X, matchingStep.Y);
        world.SpotlightCharacterId = character.Id;
        world.SpotlightIntent      = new SpotlightIntent { MoveTarget = farTarget };

        var matchingMove    = new WorldEngine.Sim.Entities.MoveToTile(character.Id, matchingStep);
        var nonMatchingRest = new WorldEngine.Sim.Entities.Rest(character.Id);
        var candidates = new List<UtilityScorer.ScoredAction>
        {
            new(matchingMove, 1.0f),
            new(nonMatchingRest, 1.0f),
        };

        ApplySpotlightBias(candidates, world, character);

        candidates[0].Score.Should().BeApproximately(ExpectedBias, 0.001f,
            "a MoveToTile whose destination is the resolved step toward the spotlight move target must be biased 3.0x");
        candidates[1].Score.Should().Be(1.0f, "Rest does not match the move intent and must be left unbiased");
    }

    [Fact]
    public void MoveTarget_DoesNotBiasAMoveToADifferentDestination()
    {
        var world = BuildWorld();
        var start = FindLandTileWithLandNeighbor(world, out var matchingStep);
        var character = MakeTier1(start, new EntityId(921));
        world.Entities.Add(character);

        world.SpotlightCharacterId = character.Id;
        world.SpotlightIntent      = new SpotlightIntent { MoveTarget = matchingStep };

        // A MoveToTile to the character's own current tile can never equal the resolved step
        // (StepToward only ever returns a distinct adjacent tile), so this is guaranteed non-matching.
        var nonMatchingMove = new WorldEngine.Sim.Entities.MoveToTile(character.Id, start);
        var candidates = new List<UtilityScorer.ScoredAction> { new(nonMatchingMove, 1.0f) };

        ApplySpotlightBias(candidates, world, character);

        candidates[0].Score.Should().Be(1.0f, "a MoveToTile to a destination other than the resolved step must not be biased");
    }

    [Fact]
    public void GoalIntent_FoundCity_BiasesEstablishSettlementAndMoveToTile_ButNotRest()
    {
        var world = BuildWorld();
        var start = FindLandTileWithLandNeighbor(world, out var step);
        var character = MakeTier1(start, new EntityId(922));
        world.Entities.Add(character);

        world.SpotlightCharacterId = character.Id;
        world.SpotlightIntent      = new SpotlightIntent { GoalIntent = GoalType.FoundCity };

        var settle = new WorldEngine.Sim.Entities.EstablishSettlement(character.Id, start);
        var move   = new WorldEngine.Sim.Entities.MoveToTile(character.Id, step);
        var rest   = new WorldEngine.Sim.Entities.Rest(character.Id);
        var candidates = new List<UtilityScorer.ScoredAction> { new(settle, 1.0f), new(move, 1.0f), new(rest, 1.0f) };

        ApplySpotlightBias(candidates, world, character);

        candidates[0].Score.Should().BeApproximately(ExpectedBias, 0.001f, "EstablishSettlement matches GoalType.FoundCity");
        candidates[1].Score.Should().BeApproximately(ExpectedBias, 0.001f, "MoveToTile also matches GoalType.FoundCity");
        candidates[2].Score.Should().Be(1.0f, "Rest does not match GoalType.FoundCity");
    }

    [Fact]
    public void NoActiveIntent_LeavesAllScoresUnchanged()
    {
        var world = BuildWorld();
        var start = FindLandTileWithLandNeighbor(world, out var step);
        var character = MakeTier1(start, new EntityId(923));
        world.Entities.Add(character);

        world.SpotlightCharacterId = character.Id;
        world.SpotlightIntent      = new SpotlightIntent(); // no MoveTarget, no GoalIntent

        var move = new WorldEngine.Sim.Entities.MoveToTile(character.Id, step);
        var rest = new WorldEngine.Sim.Entities.Rest(character.Id);
        var candidates = new List<UtilityScorer.ScoredAction> { new(move, 1.0f), new(rest, 1.0f) };

        ApplySpotlightBias(candidates, world, character);

        candidates[0].Score.Should().Be(1.0f);
        candidates[1].Score.Should().Be(1.0f);
    }
}
