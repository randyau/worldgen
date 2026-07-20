using System.Collections.Generic;
using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Annual destruction sink for artifacts. Without a sink, the artifact stock is monotonic
/// (created but never destroyed) and grows unbounded over thousand-year histories. Each year
/// every living artifact rolls a small destruction chance — high for Lost (ownerless) items that
/// no one safeguards, very low for owned items. This drives the stock toward an equilibrium of
/// roughly (annual creation rate ÷ decay rate) rather than growing forever.
/// Runs on annual ticks only; emits <see cref="EventType.ArtifactDestroyed"/> per loss.
/// </summary>
public static class ArtifactDecayPhase
{
    public static void Execute(WorldState world, List<PendingEvent> pending, ArtifactConfig cfg)
    {
        if (world.Artifacts.Count == 0) return;

        // Collect first, mutate after — avoid modifying the dictionary while enumerating.
        List<ArtifactId>? doomed = null;
        foreach (var artifact in world.Artifacts.Values)
        {
            if (artifact.IsDestroyed) continue;

            float p = artifact.Owner.Kind == ArtifactOwnerKind.Lost
                ? cfg.LostArtifactAnnualDecay
                : cfg.OwnedArtifactAnnualDecay;
            if (p <= 0f) continue;

            float roll = world.GetRandomFloat(new EntityId(artifact.Id.Value), SimRngSalts.ArtifactDecay);
            if (roll < p)
                (doomed ??= new List<ArtifactId>()).Add(artifact.Id);
        }

        if (doomed is null) return;

        foreach (var id in doomed)
        {
            var artifact = world.Artifacts[id];
            string cause = artifact.Owner.Kind == ArtifactOwnerKind.Lost
                ? "lost to history"
                : "destroyed";
            TileCoord? loc = artifact.Owner.Kind == ArtifactOwnerKind.Settlement
                ? artifact.Owner.SettlementTile
                : null;

            ArtifactRegistry.Destroy(world, id, world.CurrentYear);

            var payload = JsonSerializer.Serialize(new ArtifactDestroyedPayload(
                id.Value, artifact.Name, cause));
            pending.Add(new PendingEvent(EventType.ArtifactDestroyed, loc, null, payload,
                new[] { id.Value }));
        }
    }
}
