using System.Text.Json;
using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation;

/// <summary>
/// Resolves God Mode authoring commands against WorldState, then injects the resulting
/// PendingEvents into PhaseRunner so they flow through normal event generation and get
/// stamped IsGodMode = true by the ≥9000 range detection in RunEventGeneration.
/// </summary>
internal static class AuthoringResolver
{
    internal static void Resolve(ICommand cmd, WorldState world, PhaseRunner phaseRunner)
    {
        switch (cmd)
        {
            case AuthorPlaceArtifact a:   ResolveArtifact(a, world, phaseRunner);   break;
            case AuthorTriggerDisaster d: ResolveDisaster(d, world, phaseRunner);   break;
            case AuthorSpawnCharacter s:  ResolveSpawn(s, world, phaseRunner);      break;
            case AuthorNudgeCharacter n:  ResolveNudge(n, world, phaseRunner);      break;
        }
    }

    // ── Place Artifact ────────────────────────────────────────────────────────

    private static void ResolveArtifact(AuthorPlaceArtifact cmd, WorldState world, PhaseRunner phaseRunner)
    {
        var (valid, reason) = AuthoringValidator.ValidateCoord(cmd.Coord, world);
        if (!valid) { LogRejection(nameof(AuthorPlaceArtifact), reason!); return; }

        string name = cmd.Name ?? ArtifactNameGenerator.Generate(world, cmd.Category, world.Artifacts.Count);
        float quality = 0.85f; // DECISION: god-placed artifacts are always high-quality legendary items

        var artifact = ArtifactRegistry.Create(
            world,
            name:         name,
            cat:          cmd.Category,
            year:         world.CurrentYear,
            creatorId:    0L,
            creatorName:  "The Divine",
            origin:       $"Placed by God Mode at {cmd.Coord}",
            quality:      quality,
            owner:        ArtifactOwner.Lost);

        var payload = JsonSerializer.Serialize(
            new GodModeArtifactPayload(artifact.Id.Value, artifact.Name,
                cmd.Category.ToString(), quality));

        phaseRunner.InjectPendingEvent(new PendingEvent(
            EventType.GodModeArtifactPlaced, cmd.Coord, null, payload,
            ActorName: "The Divine"));
    }

    // ── Trigger Disaster ─────────────────────────────────────────────────────

    private static void ResolveDisaster(AuthorTriggerDisaster cmd, WorldState world, PhaseRunner phaseRunner)
    {
        var (valid, reason) = AuthoringValidator.ValidateDisasterApplicable(cmd.Coord, cmd.Type, world);
        if (!valid) { LogRejection(nameof(AuthorTriggerDisaster), reason!); return; }

        var dcfg = world.SimConfig.Disasters;
        var (intensity, ticks) = cmd.Type switch
        {
            DisasterType.Wildfire      => (dcfg.WildfireIntensity,   dcfg.WildfireMaxTicks),
            DisasterType.Flood         => (dcfg.FloodOriginIntensity, dcfg.FloodOriginTicks),
            DisasterType.VolcanicAsh   => (dcfg.VolcanicAshIntensity, -1),
            DisasterType.SeismicDamage => (dcfg.EarthquakeIntensity,  dcfg.EarthquakeDecayTicks),
            _                          => (1.0f, 10),
        };

        AddActiveDisaster(world, cmd.Coord, new ActiveDisaster(cmd.Type, intensity, ticks, new EventId(0)));

        var payload = JsonSerializer.Serialize(
            new GodModeDisasterPayload(cmd.Type.ToString(), intensity));

        phaseRunner.InjectPendingEvent(new PendingEvent(
            EventType.GodModeDisasterTriggered, cmd.Coord, null, payload,
            ActorName: "The Divine"));
    }

    private static void AddActiveDisaster(WorldState world, TileCoord coord, ActiveDisaster disaster)
    {
        if (!world.ActiveTileDisasters.TryGetValue(coord, out var list))
            world.ActiveTileDisasters[coord] = list = new List<ActiveDisaster>();
        list.Add(disaster);
        var tile = world.TileGrid.GetTile(coord);
        tile.DynFlags |= TileDynFlags.HasActiveDisaster;
    }

    // ── Spawn Character ───────────────────────────────────────────────────────

    private static void ResolveSpawn(AuthorSpawnCharacter cmd, WorldState world, PhaseRunner phaseRunner)
    {
        var (valid, reason) = AuthoringValidator.ValidateLandTile(cmd.Coord, world);
        if (!valid) { LogRejection(nameof(AuthorSpawnCharacter), reason!); return; }

        long seq = (9_000_000L + world.CurrentTick * 997L + cmd.Coord.X * 31L + cmd.Coord.Y) & 0x7FFFFFFF;
        var tileData = world.TileGrid.GetTile(cmd.Coord);
        var biome    = (BiomeType)tileData.BiomeType;

        var character = CharacterFactory.Spawn(cmd.Coord, biome, world.WorldSeed, seq, world.SimConfig, world.CurrentYear);

        if (cmd.AncestryId != null)
        {
            var ancestry = world.SimConfig.AncestryRegistry.GetOrHuman(cmd.AncestryId);
            character.Identity = character.Identity with { AncestryId = ancestry.Id };
        }

        int ordinal = world.ClaimNameOrdinal(character.Identity.Name);
        if (ordinal > 0)
            character.Identity = character.Identity with { NameOrdinal = ordinal };

        world.Entities.Add(character);

        var payload = JsonSerializer.Serialize(
            new GodModeCharacterPayload(character.Id.Value, character.Identity.Name,
                character.Identity.AncestryId));

        phaseRunner.InjectPendingEvent(new PendingEvent(
            EventType.GodModeCharacterCreated, cmd.Coord, null, payload,
            new[] { character.Id.Value },
            ActorId:   character.Id.Value,
            ActorName: character.Identity.Name));
    }

    // ── Nudge Character ───────────────────────────────────────────────────────

    private static void ResolveNudge(AuthorNudgeCharacter cmd, WorldState world, PhaseRunner phaseRunner)
    {
        var (valid, reason) = AuthoringValidator.ValidateCharacterAlive(cmd.CharacterId, world);
        if (!valid) { LogRejection(nameof(AuthorNudgeCharacter), reason!); return; }

        var ch = (Tier1Character)world.GetEntity(cmd.CharacterId)!;

        switch (cmd.Nudge)
        {
            case CharacterNudge.RaiseMorale:
                ch.Wellbeing = Math.Clamp(ch.Wellbeing + 0.4f, 0f, 1f);
                break;
            case CharacterNudge.LowerMorale:
                ch.Wellbeing = Math.Clamp(ch.Wellbeing - 0.4f, 0f, 1f);
                break;
            case CharacterNudge.SetWander:
                ch.Goals.RemoveAll(g => g.Type != GoalType.Survive);
                break;
            case CharacterNudge.SetSettle:
                if (!ch.Goals.Any(g => g.Type == GoalType.FoundCity))
                    ch.Goals.Add(new GoalData
                    {
                        Type      = GoalType.FoundCity,
                        Priority  = 0.9f,
                        Intensity = 0.8f,
                        FormedTick = (int)world.CurrentTick,
                        StaleSince = (int)world.CurrentTick,
                    });
                break;
        }

        var payload = JsonSerializer.Serialize(
            new GodModeNudgePayload(ch.Id.Value, ch.Identity.Name, cmd.Nudge.ToString()));

        phaseRunner.InjectPendingEvent(new PendingEvent(
            EventType.GodModeCharacterNudged, ch.Location, null, payload,
            new[] { ch.Id.Value },
            ActorId:   ch.Id.Value,
            ActorName: ch.Identity.Name));
    }

    private static void LogRejection(string commandName, string reason)
    {
        // DECISION: authoring validation failures log to stderr rather than throwing
        // so a rejected command silently no-ops rather than crashing the sim thread.
        Console.Error.WriteLine($"[GodMode] {commandName} rejected: {reason}");
    }
}
