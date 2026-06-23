using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;

namespace WorldEngine.Sim.Entities;

/// <summary>
/// Canonical store for all live entities. Owned by the sim thread.
/// Maintains a spatial index (tile → entity set) for fast proximity lookups.
/// </summary>
public sealed class EntityRegistry
{
    private readonly Dictionary<EntityId, IEntity> _all = new();
    private readonly Dictionary<TileCoord, HashSet<EntityId>> _spatial = new();
    private readonly List<LegendaryBeast>  _beasts      = new();
    private readonly List<Tier1Character>  _characters  = new();
    private readonly List<Tier2Character>  _tier2chars  = new();

    public IReadOnlyList<LegendaryBeast>  Beasts      => _beasts;
    public IReadOnlyList<Tier1Character>  Characters  => _characters;
    public IReadOnlyList<Tier2Character>  Tier2Chars  => _tier2chars;
    public IReadOnlyDictionary<EntityId, IEntity> All => _all;
    public int Count => _all.Count;

    public void Add(IEntity entity)
    {
        _all[entity.Id] = entity;
        AddToSpatial(entity.Id, entity.Location);
        if (entity is LegendaryBeast b)      _beasts.Add(b);
        else if (entity is Tier1Character c) _characters.Add(c);
        else if (entity is Tier2Character c2) _tier2chars.Add(c2);
    }

    public void Remove(EntityId id)
    {
        if (!_all.TryGetValue(id, out var entity)) return;
        RemoveFromSpatial(id, entity.Location);
        _all.Remove(id);
        if (entity is LegendaryBeast b)      _beasts.Remove(b);
        else if (entity is Tier1Character c) _characters.Remove(c);
        else if (entity is Tier2Character c2) _tier2chars.Remove(c2);
    }

    public IEntity? Get(EntityId id) => _all.GetValueOrDefault(id);

    public IEnumerable<IEntity> GetAt(TileCoord coord) =>
        _spatial.TryGetValue(coord, out var ids)
            ? ids.Select(id => _all[id])
            : Enumerable.Empty<IEntity>();

    public EntityId[] GetIdsAt(TileCoord coord) =>
        _spatial.TryGetValue(coord, out var ids)
            ? ids.ToArray()
            : Array.Empty<EntityId>();

    public IEnumerable<IEntity> GetInRadius(TileCoord center, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            var coord = new TileCoord(center.X + dx, center.Y + dy);
            foreach (var e in GetAt(coord))
                yield return e;
        }
    }

    /// <summary>Updates spatial index when an entity moves. Must be called on every location change.</summary>
    public void UpdateLocation(EntityId id, TileCoord oldCoord, TileCoord newCoord)
    {
        RemoveFromSpatial(id, oldCoord);
        AddToSpatial(id, newCoord);
    }

    public int CountBySpecies(string speciesId) =>
        _beasts.Count(b => b.SpeciesId == speciesId && b.IsAlive);

    private void AddToSpatial(EntityId id, TileCoord coord)
    {
        if (!_spatial.TryGetValue(coord, out var set))
            _spatial[coord] = set = new HashSet<EntityId>();
        set.Add(id);
    }

    private void RemoveFromSpatial(EntityId id, TileCoord coord)
    {
        if (!_spatial.TryGetValue(coord, out var set)) return;
        set.Remove(id);
        if (set.Count == 0) _spatial.Remove(coord);
    }
}
