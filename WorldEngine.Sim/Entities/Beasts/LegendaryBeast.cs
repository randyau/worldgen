using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Beasts;

/// <summary>
/// A named, tracked beast entity. "Legendary" refers to the entity tier (named, historically
/// significant), not necessarily to IsLegendary which marks a legendary specimen.
/// All beasts — from a common wolf to a Dragon — are instances of this class.
/// </summary>
public sealed class LegendaryBeast : SimEntity
{
    public override EntityKind Kind => EntityKind.LegendaryBeast;
    public TileCoord HomeTile { get; }

    public string SpeciesId { get; }
    public string Name { get; }
    public bool IsLegendary { get; }

    // Combat stats
    public int Strength { get; }
    public int Speed { get; }
    public float Aggression { get; }
    public int TerritoryRadius { get; }
    public string[] Abilities { get; }  // tags — V2 mechanical effects

    // Needs
    public float FoodNeed { get; internal set; } = 0.8f;
    public float SafetyNeed { get; internal set; } = 1.0f;

    // Lifecycle
    public float FoodDepletion { get; }
    public float FoodFromHunt { get; }
    public float FoodFromGraze { get; }
    public float ReproductionChance { get; }
    public int ReproductionMinAge { get; }
    public float ReproductionFoodThreshold { get; }
    public bool Hibernates { get; }
    public bool PrefersCompany { get; }

    public LegendaryBeast(
        EntityId id,
        string speciesId,
        string name,
        TileCoord location,
        bool isLegendary,
        int maxHealth,
        int strength,
        int speed,
        float aggression,
        int territoryRadius,
        string[] abilities,
        int maxAgeSeason,
        float foodDepletion,
        float foodFromHunt,
        float foodFromGraze,
        float reproductionChance,
        int reproductionMinAge,
        float reproductionFoodThreshold,
        bool hibernates,
        bool prefersCompany)
        : base(id, location, maxHealth, maxAgeSeason)
    {
        SpeciesId                 = speciesId;
        Name                      = name;
        HomeTile                  = location;
        IsLegendary               = isLegendary;
        Strength                  = strength;
        Speed                     = speed;
        Aggression                = aggression;
        TerritoryRadius           = territoryRadius;
        Abilities                 = abilities;
        FoodDepletion             = foodDepletion;
        FoodFromHunt              = foodFromHunt;
        FoodFromGraze             = foodFromGraze;
        ReproductionChance        = reproductionChance;
        ReproductionMinAge        = reproductionMinAge;
        ReproductionFoodThreshold = reproductionFoodThreshold;
        Hibernates                = hibernates;
        PrefersCompany            = prefersCompany;
    }

    protected override string SnapshotName         => Name;
    protected override string SnapshotSpeciesId    => SpeciesId;
    protected override bool   SnapshotIsLegendary  => IsLegendary;
    protected override float  SnapshotFoodFraction => FoodNeed;

    public override IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase)
    {
        if (!IsAlive || phase != SimPhase.EntityBehavior) yield break;

        // Priority 1: hibernation
        if (Hibernates && world.CurrentSeason == Season.Winter)
        {
            yield return new Rest(Id);
            yield break;
        }

        // Priority 2: safety threat — flee from aggressive entities nearby
        var threat = FindThreat(world);
        if (threat.HasValue)
        {
            yield return new Flee(Id, threat.Value);
            yield break;
        }

        // Priority 3: starving — move toward food
        if (FoodNeed < 0.2f)
        {
            var foodTile = FindFertileTile(world);
            if (foodTile.HasValue && foodTile.Value != Location)
                yield return new MoveToTile(Id, foodTile.Value);
            else
                yield return new Rest(Id);
            yield break;
        }

        // Priority 4: hunt — attack nearby huntable target
        if (FoodNeed < 0.7f && Aggression > 0.4f)
        {
            var prey = FindPrey(world);
            if (prey.HasValue)
            {
                yield return new Attack(Id, prey.Value);
                yield break;
            }
        }

        // Priority 5: graze — restore food on fertile tile
        if (FoodFromGraze > 0f && FoodNeed < 0.8f)
        {
            var tile = world.GetTile(Location);
            if (tile.Fertility > 80)
            {
                yield return new Graze(Id);
                yield break;
            }
        }

        // Priority 6: return home if outside territory
        int distSq = DistanceSq(Location, HomeTile);
        if (distSq > TerritoryRadius * TerritoryRadius)
        {
            var step = StepToward(Location, HomeTile, world);
            yield return new MoveToTile(Id, step);
            yield break;
        }

        // Priority 7: wander within territory
        var wander = PickWanderTile(world);
        if (wander.HasValue)
        {
            yield return new MoveToTile(Id, wander.Value);
            yield break;
        }

        yield return new Rest(Id);
    }

    // ─── Behaviour helpers ─────────────────────────────────────────────────

    private TileCoord? FindThreat(IWorldStateReadOnly world)
    {
        foreach (var coord in AdjacentAndSelf(world))
        foreach (var e in world.GetEntitiesAt(coord))
        {
            if (e.Id == Id || !e.IsAlive) continue;
            if (e is LegendaryBeast other && other.Aggression > 0.4f && other.SpeciesId != SpeciesId)
                return coord;
        }
        return null;
    }

    private EntityId? FindPrey(IWorldStateReadOnly world)
    {
        foreach (var coord in AdjacentAndSelf(world))
        foreach (var e in world.GetEntitiesAt(coord))
        {
            if (e.Id == Id || !e.IsAlive) continue;
            if (e is LegendaryBeast other && other.SpeciesId != SpeciesId)
                return other.Id;
        }
        return null;
    }

    private TileCoord? FindFertileTile(IWorldStateReadOnly world)
    {
        TileCoord? best = null;
        int bestFertility = -1;
        foreach (var coord in AdjacentCoords(world))
        {
            if (!world.IsLand(coord)) continue;
            int f = world.GetTile(coord).Fertility;
            if (f > bestFertility) { bestFertility = f; best = coord; }
        }
        return best;
    }

    private TileCoord? PickWanderTile(IWorldStateReadOnly world)
    {
        // Collect valid adjacent tiles within territory
        var candidates = new List<TileCoord>();
        foreach (var coord in AdjacentCoords(world))
        {
            if (!world.IsLand(coord)) continue;
            if (DistanceSq(coord, HomeTile) > TerritoryRadius * TerritoryRadius) continue;
            // Avoid tiles already occupied by same-species packmates if possible
            bool occupied = PrefersCompany
                ? false
                : world.GetEntitiesAt(coord).Any(e => e is LegendaryBeast b && b.SpeciesId == SpeciesId);
            if (!occupied) candidates.Add(coord);
        }
        if (candidates.Count == 0) return null;
        // Use entity Id + AgeSeason as a stable low-budget pick (not WorldRng — wander doesn't need reproducibility)
        return candidates[(int)((Id.Value + AgeSeason) % candidates.Count)];
    }

    private TileCoord StepToward(TileCoord from, TileCoord to, IWorldStateReadOnly world)
    {
        int dx = Math.Sign(to.X - from.X);
        int dy = Math.Sign(to.Y - from.Y);
        var step = new TileCoord(from.X + dx, from.Y + dy);
        return world.IsLand(step) ? step : from;
    }

    private IEnumerable<TileCoord> AdjacentAndSelf(IWorldStateReadOnly world)
    {
        yield return Location;
        foreach (var c in AdjacentCoords(world))
            yield return c;
    }

    private IEnumerable<TileCoord> AdjacentCoords(IWorldStateReadOnly world)
    {
        int w = world.Config.TileWidth, h = world.Config.TileHeight;
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };
        for (int i = 0; i < 4; i++)
        {
            int nx = ((Location.X + dx[i]) % w + w) % w;
            int ny = Math.Clamp(Location.Y + dy[i], 0, h - 1);
            yield return new TileCoord(nx, ny);
        }
    }

    private static int DistanceSq(TileCoord a, TileCoord b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
