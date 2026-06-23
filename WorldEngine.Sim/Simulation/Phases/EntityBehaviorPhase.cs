using System.Text.Json;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// SimPhase 4 — EntityBehavior.
/// Each season tick: update beast needs/lifecycle, emit commands, resolve them.
/// Beast emergence schedule is checked annually.
/// </summary>
public sealed class EntityBehaviorPhase
{
    private readonly BeastCatalog _catalog;
    private readonly int _starvationHealthLoss;

    private const int SaltReproduction = 300;
    private const int SaltEmergeTile   = 301;

    public EntityBehaviorPhase(BeastCatalog catalog, int starvationHealthLoss = 5)
    {
        _catalog             = catalog;
        _starvationHealthLoss = starvationHealthLoss;
    }

    public void RunTick(WorldState world, List<PendingEvent> pending, bool isAnnualTick)
    {
        ProcessEmergenceSchedule(world, pending, isAnnualTick);
        UpdateBeastLifecycles(world, pending);
        EmitAndResolveCommands(world, pending);
        RemoveDeadEntities(world);
    }

    // ─── Emergence ────────────────────────────────────────────────────────────

    private void ProcessEmergenceSchedule(
        WorldState world, List<PendingEvent> pending, bool isAnnualTick)
    {
        if (!isAnnualTick || world.BeastEmergenceSchedule.Count == 0) return;

        var toRemove = new List<(int, string)>();
        foreach (var entry in world.BeastEmergenceSchedule)
        {
            if (world.CurrentYear < entry.EmergenceYear) continue;
            toRemove.Add(entry);

            var species = _catalog.Get(entry.SpeciesId);
            if (species is null) continue;

            var validTiles = CollectValidTiles(world, species);
            if (validTiles.Count == 0) continue;

            long seq = world.CurrentTick;
            int tileIdx = world.GetRandomInt(new EntityId(seq), 0, validTiles.Count, SaltEmergeTile);
            var tile = validTiles[Math.Clamp(tileIdx, 0, validTiles.Count - 1)];

            var beast = BeastFactory.Spawn(species, tile, world.WorldSeed, seq, forceLegendary: true);
            world.Entities.Add(beast);

            var payload = JsonSerializer.Serialize(new
            {
                beastId   = beast.Id.Value,
                name      = beast.Name,
                speciesId = beast.SpeciesId,
                location  = new[] { tile.X, tile.Y }
            });
            pending.Add(new PendingEvent(EventType.BeastAwakened, tile, null, payload));
        }

        foreach (var e in toRemove)
            world.BeastEmergenceSchedule.Remove(e);
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void UpdateBeastLifecycles(WorldState world, List<PendingEvent> pending)
    {
        // Snapshot the list — Reproduce() adds to Entities.Beasts during iteration
        var beasts = world.Entities.Beasts.ToList();
        foreach (var beast in beasts)
        {
            if (!beast.IsAlive) continue;

            beast.AgeSeason++;

            // Food depletion — reduced in hibernation
            float depletion = beast.Hibernates && world.CurrentSeason == Season.Winter
                ? beast.FoodDepletion * 0.2f
                : beast.FoodDepletion;
            beast.FoodNeed = Math.Max(0f, beast.FoodNeed - depletion);

            // Starvation
            if (beast.FoodNeed < 0.2f)
                beast.Health -= _starvationHealthLoss;

            // Old age
            if (beast.AgeSeason >= beast.MaxAgeSeason)
            {
                KillBeast(beast, "Age", null, pending, world);
                continue;
            }

            // Health death (starvation cumulative)
            if (beast.Health <= 0)
            {
                KillBeast(beast, "Starvation", null, pending, world);
                continue;
            }

            // Reproduction
            if (beast.AgeSeason >= beast.ReproductionMinAge
                && beast.FoodNeed >= beast.ReproductionFoodThreshold
                && world.Entities.CountBySpecies(beast.SpeciesId) < GetMaxPerWorld(beast.SpeciesId))
            {
                float roll = world.GetRandomFloat(beast.Id, SaltReproduction);
                if (roll < beast.ReproductionChance)
                    Reproduce(beast, world, pending);
            }
        }
    }

    private int GetMaxPerWorld(string speciesId) =>
        _catalog.Get(speciesId)?.MaxPerWorld ?? int.MaxValue;

    private void Reproduce(LegendaryBeast parent, WorldState world, List<PendingEvent> pending)
    {
        var species = _catalog.Get(parent.SpeciesId);
        if (species is null) return;

        long seq = world.CurrentTick + parent.Id.Value;
        var child = BeastFactory.Spawn(species, parent.HomeTile, world.WorldSeed, seq);
        world.Entities.Add(child);

        var payload = JsonSerializer.Serialize(new
        {
            parentId  = parent.Id.Value,
            childId   = child.Id.Value,
            childName = child.Name,
            speciesId = child.SpeciesId
        });
        pending.Add(new PendingEvent(EventType.BeastReproduced, parent.HomeTile, null, payload));
    }

    // ─── Command emit + resolve ───────────────────────────────────────────────

    private void EmitAndResolveCommands(WorldState world, List<PendingEvent> pending)
    {
        // Collect all commands before resolving any (emit → resolve separation)
        var commands = new List<(LegendaryBeast beast, ICommand cmd)>();
        foreach (var beast in world.Entities.Beasts)
        {
            if (!beast.IsAlive) continue;
            foreach (var cmd in beast.EmitCommands(world, SimPhase.EntityBehavior))
                commands.Add((beast, cmd));
        }

        // Resolve in order
        foreach (var (beast, cmd) in commands)
        {
            if (!beast.IsAlive) continue;
            Resolve(beast, cmd, world, pending, commands);
        }
    }

    private void Resolve(
        LegendaryBeast beast,
        ICommand cmd,
        WorldState world,
        List<PendingEvent> pending,
        List<(LegendaryBeast, ICommand)> allCommands)
    {
        switch (cmd)
        {
            case MoveToTile move:
                if (move.EntityId == beast.Id)
                    MoveEntity(beast, move.Destination, world);
                break;

            case Graze:
                var species = _catalog.Get(beast.SpeciesId);
                if (species != null)
                    beast.FoodNeed = Math.Min(1f, beast.FoodNeed + species.FoodFromGraze);
                break;

            case Rest:
                // No state change — placeholder for future fatigue system
                break;

            case Flee flee:
                ResolveFleeCommand(beast, flee, world);
                break;

            case Attack attack:
                if (attack.Attacker == beast.Id)
                    ResolveCombat(beast, attack.Target, world, pending);
                break;
        }
    }

    private static void MoveEntity(LegendaryBeast beast, TileCoord dest, WorldState world)
    {
        var old = beast.Location;
        beast.Location = dest;
        world.Entities.UpdateLocation(beast.Id, old, dest);
    }

    private void ResolveFleeCommand(LegendaryBeast beast, Flee flee, WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        int bestDistSq = -1;
        TileCoord? best = null;

        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };
        for (int i = 0; i < 4; i++)
        {
            int nx = ((beast.Location.X + dx[i]) % w + w) % w;
            int ny = Math.Clamp(beast.Location.Y + dy[i], 0, h - 1);
            var cand = new TileCoord(nx, ny);
            if (!world.IsLand(cand)) continue;

            int ddx = cand.X - flee.AwayFrom.X, ddy = cand.Y - flee.AwayFrom.Y;
            int distSq = ddx * ddx + ddy * ddy;
            if (distSq > bestDistSq) { bestDistSq = distSq; best = cand; }
        }

        if (best.HasValue)
            MoveEntity(beast, best.Value, world);
    }

    private void ResolveCombat(
        LegendaryBeast attacker,
        EntityId targetId,
        WorldState world,
        List<PendingEvent> pending)
    {
        if (world.GetEntity(targetId) is not LegendaryBeast target) return;
        if (!target.IsAlive) return;

        var combatCfg = _catalog.CombatConfig;

        // Fire encounter event at start of combat
        FireEncounterEvent(attacker, target, pending);

        // Collect gang members (same species as attacker on same tile, up to max_gang_size)
        var gang = new List<LegendaryBeast> { attacker };
        foreach (var e in world.GetEntitiesAt(attacker.Location))
        {
            if (gang.Count >= combatCfg.MaxGangSize) break;
            if (e is LegendaryBeast b && b.Id != attacker.Id
                && b.SpeciesId == attacker.SpeciesId && b.IsAlive)
                gang.Add(b);
        }

        bool targetKilled = false;

        // Multi-round combat loop
        for (int round = 0; round < combatCfg.MaxRoundsPerTick; round++)
        {
            if (!attacker.IsAlive || !target.IsAlive) break;

            // Gang attacks target
            foreach (var member in gang)
            {
                if (!member.IsAlive) continue;
                float atkRoll = member.Strength * world.GetRandomFloat(member.Id, round * 10);
                float defRoll = (target.Health / (float)Math.Max(1, target.MaxHealth))
                                * world.GetRandomFloat(target.Id, round * 10 + 1);
                if (atkRoll > defRoll)
                    target.Health -= member.Strength;
            }
            if (target.Health <= 0)
            {
                KillBeast(target, "Combat", attacker, pending, world);
                targetKilled = true;
                break;
            }

            // Target retaliates against a random gang member
            var retTarget = gang[round % gang.Count];
            float retAtk = target.Strength * world.GetRandomFloat(target.Id, round * 10 + 2);
            float retDef = (retTarget.Health / (float)Math.Max(1, retTarget.MaxHealth))
                           * world.GetRandomFloat(retTarget.Id, round * 10 + 3);
            if (retAtk > retDef)
                retTarget.Health -= target.Strength;
            if (retTarget.Health <= 0)
            {
                KillBeast(retTarget, "Combat", target, pending, world);
                gang.Remove(retTarget);
            }

            // Retreat check
            if (target.Health > 0
                && target.Health < target.MaxHealth * combatCfg.RetreatHealthFraction)
            {
                ResolveFleeCommand(target, new Flee(target.Id, attacker.Location), world);
                break;
            }
        }

        // Winning gang members gain food from the kill (each uses their own FoodFromHunt stat)
        if (targetKilled)
        {
            foreach (var member in gang)
            {
                if (member.IsAlive)
                    member.FoodNeed = Math.Min(1f, member.FoodNeed + member.FoodFromHunt);
            }
        }
    }

    private static void FireEncounterEvent(
        LegendaryBeast attacker, LegendaryBeast target, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            attackerId   = attacker.Id.Value,
            attackerName = attacker.Name,
            targetId     = target.Id.Value,
            targetName   = target.Name,
            location     = new[] { attacker.Location.X, attacker.Location.Y }
        });
        pending.Add(new PendingEvent(EventType.BeastEncountered, attacker.Location, null, payload));
    }

    // ─── Death ───────────────────────────────────────────────────────────────

    private static void KillBeast(
        LegendaryBeast beast,
        string cause,
        LegendaryBeast? killer,
        List<PendingEvent> pending,
        WorldState world)
    {
        beast.IsAlive = false;
        beast.Health  = 0;

        var eventType = killer != null ? EventType.BeastSlain : EventType.BeastDied;
        var payload = JsonSerializer.Serialize(new
        {
            beastId    = beast.Id.Value,
            name       = beast.Name,
            speciesId  = beast.SpeciesId,
            isLegendary = beast.IsLegendary,
            ageSeason  = beast.AgeSeason,
            cause      = cause,
            killerName = killer?.Name
        });
        pending.Add(new PendingEvent(eventType, beast.Location, null, payload));
    }

    private static void RemoveDeadEntities(WorldState world)
    {
        // Collect dead IDs first, then remove to avoid modifying collection during iteration
        var dead = world.Entities.Beasts
            .Where(b => !b.IsAlive)
            .Select(b => b.Id)
            .ToList();
        foreach (var id in dead)
            world.Entities.Remove(id);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static List<TileCoord> CollectValidTiles(WorldState world, BeastSpeciesConfig species)
    {
        var validBiomes = new HashSet<string>(species.Biomes, StringComparer.OrdinalIgnoreCase);
        bool anyBiome = validBiomes.Contains("any");

        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var valid = new List<TileCoord>();
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var coord = new TileCoord(x, y);
            var biome = ((BiomeType)world.TileGrid.GetTile(coord).BiomeType).ToString();
            if (!anyBiome && !validBiomes.Contains(ToSnake(biome))) continue;
            if ((BiomeType)world.TileGrid.GetTile(coord).BiomeType == BiomeType.Ocean) continue;
            valid.Add(coord);
        }
        return valid;
    }

    private static string ToSnake(string pascalCase)
    {
        // Simple BiomeType enum name → snake_case for matching TOML biome strings
        // e.g. "BorealForest" → "boreal_forest"
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascalCase.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascalCase[i]))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(pascalCase[i]));
        }
        return sb.ToString();
    }
}
