using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Artifacts;

/// <summary>
/// Static operations helper for the artifact registry on <see cref="WorldState"/>.
/// All methods mutate <c>world.Artifacts</c> only — they do NOT emit events.
/// Callers are responsible for emitting the corresponding <see cref="WorldEngine.Sim.Events"/> payload.
/// </summary>
public static class ArtifactRegistry
{
    /// <summary>
    /// Creates a new artifact, assigns a fresh <see cref="ArtifactId"/>, inserts it into the
    /// world registry, and returns the record.
    /// </summary>
    public static Artifact Create(
        WorldState w,
        string name,
        ArtifactCategory cat,
        int year,
        long creatorId,
        string creatorName,
        string origin,
        float quality,
        ArtifactOwner owner)
    {
        var artifact = new Artifact(
            Id:           ArtifactId.New(),
            Name:         name,
            Category:     cat,
            CreatedYear:  year,
            CreatorId:    creatorId,
            CreatorName:  creatorName,
            Origin:       origin,
            Quality:      quality,
            Owner:        owner);

        w.Artifacts[artifact.Id] = artifact;
        return artifact;
    }

    /// <summary>
    /// Replaces the owner of an existing artifact in-place. No-op if the artifact does not
    /// exist or is already destroyed.
    /// </summary>
    public static void SetOwner(WorldState w, ArtifactId id, ArtifactOwner owner)
    {
        if (w.Artifacts.TryGetValue(id, out var existing) && !existing.IsDestroyed)
            w.Artifacts[id] = existing with { Owner = owner };
    }

    /// <summary>
    /// Marks an artifact as destroyed and records the year. No-op if already destroyed.
    /// </summary>
    public static void Destroy(WorldState w, ArtifactId id, int year)
    {
        if (w.Artifacts.TryGetValue(id, out var existing) && !existing.IsDestroyed)
            w.Artifacts[id] = existing with { IsDestroyed = true, DestroyedYear = year };
    }

    /// <summary>Returns all artifacts that have not been destroyed.</summary>
    public static IEnumerable<Artifact> Active(WorldState w) =>
        w.Artifacts.Values.Where(a => !a.IsDestroyed);

    /// <summary>Returns all active artifacts currently owned by the given character.</summary>
    public static IEnumerable<Artifact> OwnedByCharacter(WorldState w, EntityId id) =>
        Active(w).Where(a =>
            a.Owner.Kind == ArtifactOwnerKind.Character &&
            a.Owner.CharacterId == id.Value);

    /// <summary>Returns all active artifacts whose owner is the settlement at the given tile.</summary>
    public static IEnumerable<Artifact> InSettlement(WorldState w, TileCoord t) =>
        Active(w).Where(a =>
            a.Owner.Kind == ArtifactOwnerKind.Settlement &&
            a.Owner.SettlementTile == t);
}
