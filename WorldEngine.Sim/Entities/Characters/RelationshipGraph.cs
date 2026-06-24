using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Centralized relationship store. NOT on entity objects.
/// Canonical key: (Min(a,b), Max(a,b)) — independent of query direction.
/// Maintains a per-entity adjacency index so GetAll/CountAlliances are O(degree)
/// rather than O(all edges), preventing the O(n²) scan that accumulates as the
/// graph grows over long simulations.
/// </summary>
public sealed class RelationshipGraph
{
    private readonly Dictionary<(EntityId, EntityId), RelationshipEdge> _edges = [];
    // Adjacency index: entity → set of the OTHER endpoints of its edges
    private readonly Dictionary<EntityId, HashSet<EntityId>> _neighbors = [];

    private static (EntityId, EntityId) Key(EntityId a, EntityId b) =>
        a.Value < b.Value ? (a, b) : (b, a);

    public RelationshipEdge? Get(EntityId a, EntityId b) =>
        _edges.TryGetValue(Key(a, b), out var e) ? e : null;

    public void Upsert(RelationshipEdge edge)
    {
        var key = Key(edge.From, edge.To);
        var canonical = edge.From.Value < edge.To.Value
            ? edge
            : edge with { From = edge.To, To = edge.From };

        // Remove old adjacency entries if this edge already existed
        if (_edges.TryGetValue(key, out var old))
        {
            RemoveNeighbor(old.From, old.To);
            RemoveNeighbor(old.To, old.From);
        }

        _edges[key] = canonical;
        AddNeighbor(canonical.From, canonical.To);
        AddNeighbor(canonical.To, canonical.From);
    }

    public void Remove(EntityId a, EntityId b)
    {
        var key = Key(a, b);
        if (!_edges.TryGetValue(key, out var old)) return;
        RemoveNeighbor(old.From, old.To);
        RemoveNeighbor(old.To, old.From);
        _edges.Remove(key);
    }

    /// <summary>Returns all edges involving this entity. O(degree).</summary>
    public IEnumerable<RelationshipEdge> GetAll(EntityId id)
    {
        if (!_neighbors.TryGetValue(id, out var neighbors)) yield break;
        foreach (var other in neighbors)
        {
            var e = Get(id, other);
            if (e != null) yield return e;
        }
    }

    /// <summary>Count of alliance edges for this entity. O(degree).</summary>
    public int CountAlliances(EntityId id)
    {
        if (!_neighbors.TryGetValue(id, out var neighbors)) return 0;
        int count = 0;
        foreach (var other in neighbors)
        {
            var e = Get(id, other);
            if (e?.IsAlly == true) count++;
        }
        return count;
    }

    public IEnumerable<RelationshipEdge> AllEdges => _edges.Values;

    public int EdgeCount => _edges.Count;

    /// <summary>Convenience: ensure a neutral edge exists between two characters.</summary>
    public RelationshipEdge GetOrCreate(EntityId a, EntityId b)
    {
        var existing = Get(a, b);
        if (existing != null) return existing;
        var edge = new RelationshipEdge(a, b, Trust: 0f, Fear: 0f, Debt: 0f, Flags: RelationshipFlags.None);
        Upsert(edge);
        return edge;
    }

    private void AddNeighbor(EntityId owner, EntityId neighbor)
    {
        if (!_neighbors.TryGetValue(owner, out var set))
            _neighbors[owner] = set = new HashSet<EntityId>();
        set.Add(neighbor);
    }

    private void RemoveNeighbor(EntityId owner, EntityId neighbor)
    {
        if (_neighbors.TryGetValue(owner, out var set))
        {
            set.Remove(neighbor);
            if (set.Count == 0) _neighbors.Remove(owner);
        }
    }
}
