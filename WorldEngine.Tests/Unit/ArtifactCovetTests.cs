using FluentAssertions;
using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;
using WorldEngine.Tests.Helpers;

namespace WorldEngine.Tests.Unit;

/// <summary>
/// Unit tests for M5 W2 — Covet &amp; Goal-seeking.
/// Covers: covet goal forms only above threshold, not for owned artifacts;
/// claiming a Lost artifact transfers ownership and resolves the goal; reproducibility.
/// </summary>
public class ArtifactCovetTests
{
    // ─── World builder ────────────────────────────────────────────────────────

    private static WorldState BuildWorld(int seed = 42, Action<SimConfig>? configureSimCfg = null)
    {
        var cfg = new WorldConfig { Seed = seed, WidthKm = 500, HeightKm = 400, TileWidthKm = 10 };
        var sim = TestSimConfig.Default();
        configureSimCfg?.Invoke(sim);
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

    /// <summary>
    /// Builds a world with CovetAmbitionThreshold=0 so the stochastic guard reduces to
    /// roll ≤ (Ambition * quality) — highly predictable for high-Ambition test characters.
    /// </summary>
    private static WorldState BuildWorldCovetEasy(int seed = 42) =>
        BuildWorld(seed, sim =>
        {
            sim.Artifacts.CovetAmbitionThreshold = 0f;   // any Ambition qualifies
            sim.Artifacts.CovetMaxGoals          = 10;   // room for multiple in cap test
        });

    private static Tier1Character MakeCharacter(WorldState world, EntityId id, float ambition, TileCoord? tile = null)
    {
        // Build a character with a specific Ambition value; all other traits set to mid-range
        var personality = new PersonalityVector(
            Ambition: ambition, Greed: 0.5f, Aggression: 0.5f, Compassion: 0.5f,
            Curiosity: 0.5f, Creativity: 0.5f, Rationality: 0.5f, Wonder: 0.5f,
            Loyalty: 0.5f, Sociability: 0.5f, Honesty: 0.5f, Stability: 0.5f);
        var aptitude = new AptitudeVector(
            Diligence: 0.5f, Focus: 0.5f, Perfectionism: 0.5f,
            Composure: 0.5f, Acuity: 0.5f, Ingenuity: 0.5f);
        var skills  = SkillVector.Default;
        var coord   = tile ?? new TileCoord(5, 5);
        var identity = new IdentityData(
            Name: "TestChar", Epithet: "", AncestryId: "",
            MotherId: null, FatherId: null, CivId: default,
            BirthYear: 1, BirthSeason: 0);
        int maxHealth = world.SimConfig.Character.MaxHealth;

        var c = new Tier1Character(id, coord, personality, aptitude, skills, identity,
            maxHealth: maxHealth, maxAgeSeason: 200);
        world.Entities.Add(c);
        return c;
    }

    private static Artifact PlaceArtifact(
        WorldState world, float quality,
        ArtifactOwner? owner = null)
    {
        return ArtifactRegistry.Create(
            world, "The Bright Shard", ArtifactCategory.Weapon,
            year: 1, creatorId: 0, creatorName: "Battle",
            origin: "battle", quality: quality,
            owner: owner ?? ArtifactOwner.Lost);
    }

    // ─── Test: covet goal forms only at/above CovetThreshold ─────────────────

    [Fact]
    public void CovetGoal_DoesNotFormBelowQualityThreshold()
    {
        // Use "easy" world: CovetAmbitionThreshold=0 so any Ambition qualifies.
        // This isolates the quality threshold check.
        var world = BuildWorldCovetEasy();
        var cfg   = world.SimConfig;
        float threshold = cfg.Artifacts.CovetThreshold; // default 0.6

        var charId = new EntityId(1001);
        var c = MakeCharacter(world, charId, ambition: 0.99f);

        // Artifact BELOW threshold — should not trigger covet regardless of RNG
        PlaceArtifact(world, quality: threshold - 0.01f, ArtifactOwner.Lost);

        for (int tick = 1; tick <= 5; tick++)
        {
            var pending = new List<PendingEvent>();
            GoalManager.UpdateGoals(c, world, currentTick: tick, cfg.Character, pending);
        }

        c.Goals.Should().NotContain(g => g.Type == GoalType.CovetArtifact,
            "artifact quality is below covet_threshold");
    }

    [Fact]
    public void CovetGoal_FormsWhenArtifactQualityAboveThreshold()
    {
        // Use "easy" world: CovetAmbitionThreshold=0 so roll ≤ Ambition*quality ≈ 0.99*0.95=0.94.
        // With seed 42 the RNG roll will almost certainly be below that.
        var world = BuildWorldCovetEasy();
        var cfg   = world.SimConfig;

        var charId = new EntityId(1002);
        var c      = MakeCharacter(world, charId, ambition: 0.99f);
        var artifact = PlaceArtifact(world, quality: 0.95f, ArtifactOwner.Lost);

        bool formed = false;
        for (int tick = 1; tick <= 5 && !formed; tick++)
        {
            var pending = new List<PendingEvent>();
            GoalManager.UpdateGoals(c, world, currentTick: tick, cfg.Character, pending);
            formed = c.Goals.Any(g => g.Type == GoalType.CovetArtifact
                                   && g.CovetedArtifactId == artifact.Id);
        }

        formed.Should().BeTrue(
            "high-Ambition character with easy config should covet a high-quality artifact");
    }

    [Fact]
    public void CovetGoal_DoesNotFormForOwnedArtifact()
    {
        var world  = BuildWorldCovetEasy();
        var cfg    = world.SimConfig;
        var charId = new EntityId(1003);
        var c      = MakeCharacter(world, charId, ambition: 0.99f);

        // Artifact owned by this character — should not be coveted
        var owned = PlaceArtifact(world, quality: 0.9f, ArtifactOwner.OfCharacter(charId));

        for (int tick = 1; tick <= 5; tick++)
        {
            var pending = new List<PendingEvent>();
            GoalManager.UpdateGoals(c, world, currentTick: tick, cfg.Character, pending);
        }

        c.Goals.Should().NotContain(g =>
            g.Type == GoalType.CovetArtifact && g.CovetedArtifactId == owned.Id,
            "characters do not covet their own artifacts");
    }

    [Fact]
    public void CovetGoal_DoesNotFormForLowAmbitionCharacter()
    {
        // Standard config (CovetAmbitionThreshold = 0.55); Ambition = 0.2 → below threshold
        var world  = BuildWorld();
        var cfg    = world.SimConfig;
        var charId = new EntityId(1004);
        var c = MakeCharacter(world, charId, ambition: 0.2f);

        PlaceArtifact(world, quality: 0.95f, ArtifactOwner.Lost);

        for (int tick = 1; tick <= 5; tick++)
        {
            var pending = new List<PendingEvent>();
            GoalManager.UpdateGoals(c, world, currentTick: tick, cfg.Character, pending);
        }

        c.Goals.Should().NotContain(g => g.Type == GoalType.CovetArtifact,
            "character below CovetAmbitionThreshold should not form covet goals");
    }

    // ─── Test: CovetGoal cap ────────────────────────────────────────────────

    [Fact]
    public void CovetGoal_RespectsCap()
    {
        // Easy world with a cap of 2 (default), but we place 10 qualifying artifacts
        var world  = BuildWorldCovetEasy(seed: 7);
        var cfg    = world.SimConfig;
        int cap    = cfg.Artifacts.CovetMaxGoals;  // 10 in easy world
        var charId = new EntityId(1005);
        var c      = MakeCharacter(world, charId, ambition: 0.99f);

        // Override cap to 2 to test the limit
        cfg.Artifacts.CovetMaxGoals = 2;

        // Place many more qualifying artifacts than the cap
        for (int i = 0; i < 10; i++)
            PlaceArtifact(world, quality: 0.95f, ArtifactOwner.Lost);

        for (int tick = 1; tick <= 5; tick++)
        {
            var pending = new List<PendingEvent>();
            GoalManager.UpdateGoals(c, world, currentTick: tick, cfg.Character, pending);
        }

        c.Goals.Count(g => g.Type == GoalType.CovetArtifact)
            .Should().BeInRange(0, 2, "covet goals capped at CovetMaxGoals (2)");
    }

    // ─── Test: artifact claim transfers ownership and resolves goal ───────────

    [Fact]
    public void ClaimLostArtifact_TransfersOwnershipAndResolvesGoal()
    {
        var world  = BuildWorld();
        var cfg    = world.SimConfig;
        var charId = new EntityId(2001);
        var c      = MakeCharacter(world, charId, ambition: 0.99f);

        var artifact = PlaceArtifact(world, quality: 0.9f, ArtifactOwner.Lost);

        // Manually inject a covet goal targeting this artifact
        var covetGoal = new GoalData
        {
            Type              = GoalType.CovetArtifact,
            Object            = GoalObject.Artifact,
            CovetedArtifactId = artifact.Id,
            Priority          = 0.8f,
            Intensity         = 0.8f,
            StaleSince        = 1,
            FormedTick        = 1,
        };
        c.Goals.Add(covetGoal);

        // Call the WorldState overload — should claim the Lost artifact
        var pending = new List<PendingEvent>();
        GoalManager.UpdateGoals(c, world, currentTick: 2, cfg.Character, pending);

        // Ownership should have transferred
        world.Artifacts[artifact.Id].Owner.Kind.Should().Be(ArtifactOwnerKind.Character);
        world.Artifacts[artifact.Id].Owner.CharacterId.Should().Be(charId.Value);

        // ArtifactTransferred event should have been emitted
        pending.Should().Contain(p => p.Type == EventType.ArtifactTransferred,
            "ArtifactTransferred event must be emitted on claim");
        var xfer = pending.First(p => p.Type == EventType.ArtifactTransferred);
        var xferPayload = JsonSerializer.Deserialize<JsonElement>(xfer.PayloadJson);
        xferPayload.GetProperty("Reason").GetString().Should().Be("claim");

        // GoalResolved event should have been emitted
        pending.Should().Contain(p => p.Type == EventType.GoalResolved,
            "GoalResolved event must be emitted when covet goal completes");

        // Goal should be marked complete
        covetGoal.IsComplete.Should().BeTrue("goal completes when artifact is claimed");
        covetGoal.Progress.Should().Be(1f);
    }

    [Fact]
    public void ClaimDoesNotFire_WhenArtifactNotLost()
    {
        var world  = BuildWorld();
        var cfg    = world.SimConfig;
        var charId = new EntityId(2002);
        var c      = MakeCharacter(world, charId, ambition: 0.99f);

        // Artifact owned by a settlement (not Lost) — claim should not fire
        var settleTile = new TileCoord(3, 3);
        var artifact   = PlaceArtifact(world, quality: 0.9f, ArtifactOwner.OfSettlement(settleTile));

        var covetGoal = new GoalData
        {
            Type              = GoalType.CovetArtifact,
            Object            = GoalObject.Artifact,
            CovetedArtifactId = artifact.Id,
            Priority          = 0.8f,
            Intensity         = 0.8f,
            StaleSince        = 1,
            FormedTick        = 1,
        };
        c.Goals.Add(covetGoal);

        var pending = new List<PendingEvent>();
        GoalManager.UpdateGoals(c, world, currentTick: 2, cfg.Character, pending);

        // Owner should still be the settlement
        world.Artifacts[artifact.Id].Owner.Kind.Should().Be(ArtifactOwnerKind.Settlement,
            "artifacts owned by settlements are not claimable by walking up to them");

        pending.Should().NotContain(p => p.Type == EventType.ArtifactTransferred,
            "no ArtifactTransferred event if artifact is not Lost");
    }

    // ─── Test: destroyed artifact completes the covet goal without claim ─────

    [Fact]
    public void DestroyedArtifact_CompletesCovetGoalWithoutTransfer()
    {
        var world  = BuildWorld();
        var cfg    = world.SimConfig;
        var charId = new EntityId(2003);
        var c      = MakeCharacter(world, charId, ambition: 0.99f);

        var artifact = PlaceArtifact(world, quality: 0.9f, ArtifactOwner.Lost);

        var covetGoal = new GoalData
        {
            Type              = GoalType.CovetArtifact,
            Object            = GoalObject.Artifact,
            CovetedArtifactId = artifact.Id,
            Priority          = 0.8f,
            Intensity         = 0.8f,
            StaleSince        = 1,
            FormedTick        = 1,
        };
        c.Goals.Add(covetGoal);

        // Destroy the artifact before the claim resolves
        ArtifactRegistry.Destroy(world, artifact.Id, year: 2);

        var pending = new List<PendingEvent>();
        GoalManager.UpdateGoals(c, world, currentTick: 2, cfg.Character, pending);

        // Goal should be complete (artifact gone), no transfer event
        covetGoal.IsComplete.Should().BeTrue("covet goal completes when artifact is destroyed");
        pending.Should().NotContain(p => p.Type == EventType.ArtifactTransferred,
            "destroyed artifact should not generate a claim transfer");
    }

    // ─── Test: reproducibility (same seed → same covet decisions) ────────────

    [Fact]
    public void CovetDecisions_AreDeterministicGivenSameSeed()
    {
        // Reproducibility: the covet formation roll depends only on worldSeed, CurrentTick,
        // characterId, and a salt derived from artifact category+quality (NOT the ArtifactId,
        // which is a global counter and differs across independent world instantiations).
        const int seed     = 99999;
        const int charSeed = 77;

        static (int covetGoalCount, int goalFormedCount) RunOnce(int seed, int charSeed)
        {
            // CovetAmbitionThreshold=0 → roll ≤ Ambition*quality, deterministic
            var world = BuildWorldCovetEasy(seed);
            var cfg   = world.SimConfig;
            var charId = new EntityId(charSeed);
            var c      = MakeCharacter(world, charId, ambition: 0.99f);
            // Artifact properties (not Id) determine the RNG salt, so two runs with same
            // name/category/quality produce identical rolls.
            ArtifactRegistry.Create(
                world, "Starshard", ArtifactCategory.Relic,
                year: 1, creatorId: 0, creatorName: "",
                origin: "battle", quality: 0.85f, owner: ArtifactOwner.Lost);

            var pending = new List<PendingEvent>();
            for (int tick = 1; tick <= 5; tick++)
                GoalManager.UpdateGoals(c, world, tick, cfg.Character, pending);

            return (
                c.Goals.Count(g => g.Type == GoalType.CovetArtifact),
                pending.Count(p => p.Type == EventType.GoalFormed && p.PayloadJson.Contains("CovetArtifact")));
        }

        var (covetA, eventsA) = RunOnce(seed, charSeed);
        var (covetB, eventsB) = RunOnce(seed, charSeed);

        covetA.Should().Be(covetB, "same seed must produce identical covet goal count");
        eventsA.Should().Be(eventsB, "same seed must produce identical GoalFormed events");
    }
}
