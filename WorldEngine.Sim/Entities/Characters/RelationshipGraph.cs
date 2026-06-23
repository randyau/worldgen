using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Centralized relationship store. NOT on entity objects.
/// Canonical key: (Min(a,b), Max(a,b)) — independent of query direction.
/// </summary>
public sealed class RelationshipGraph
{
    private readonly Dictionary<(EntityId, EntityId), RelationshipEdge> _edges = [];

    private static (EntityId, EntityId) Key(EntityId a, EntityId b) =>
        a.Value < b.Value ? (a, b) : (b, a);

    public RelationshipEdge? Get(EntityId a, EntityId b) =>
        _edges.TryGetValue(Key(a, b), out var e) ? e : null;

    public void Upsert(RelationshipEdge edge)
    {
        var key = Key(edge.From, edge.To);
        // Preserve canonical From/To ordering
        var canonical = edge.From.Value < edge.To.Value
            ? edge
            : edge with { From = edge.To, To = edge.From };
        _edges[key] = canonical;
    }

    public void Remove(EntityId a, EntityId b) => _edges.Remove(Key(a, b));

    public IEnumerable<RelationshipEdge> GetAll(EntityId id) =>
        _edges.Values.Where(e => e.From == id || e.To == id);

    public IEnumerable<RelationshipEdge> AllEdges => _edges.Values;

    /// <summary>Convenience: ensure a neutral edge exists between two characters.</summary>
    public RelationshipEdge GetOrCreate(EntityId a, EntityId b)
    {
        var existing = Get(a, b);
        if (existing != null) return existing;
        var edge = new RelationshipEdge(a, b, Trust: 0f, Fear: 0f, Debt: 0f, Flags: RelationshipFlags.None);
        Upsert(edge);
        return edge;
    }
}
