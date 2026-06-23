using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Phase 5 — updates all Tier 1 characters each tick:
/// needs decay, goal management, action selection (utility scoring),
/// lifecycle (aging, death), command resolution (settlement, war, etc.).
/// </summary>
public sealed class CharacterBehaviorPhase
{
    private readonly CharacterSimConfig _cfg;
    private readonly SimConfig _simCfg;

    public CharacterBehaviorPhase(SimConfig cfg)
    {
        _simCfg = cfg;
        _cfg    = cfg.Character;
    }

    public List<PendingEvent> Execute(WorldState world, long tick)
    {
        var pending = new List<PendingEvent>();

        // Snapshot to avoid modify-during-iteration
        var characters = world.Entities.Characters.ToList();
        foreach (var c in characters)
        {
            if (!c.IsAlive) continue;
            UpdateLifecycle(c, world, tick, pending);
            if (!c.IsAlive) continue;
            c.TicksInCurrentTile++;
            NeedsUpdater.Update(c, world, _cfg);
            GoalManager.UpdateGoals(c, world, tick);
            ApplyTerritorialPressure(c, world, tick);
            CheckBeastEncounters(c, world, pending, tick);
            var cmd = UtilityScorer.SelectAction(c, world, _cfg);
            if (cmd != null)
                ResolveCommand(cmd, c, world, pending, tick);
        }

        // Remove dead characters from registry
        foreach (var c in characters.Where(ch => !ch.IsAlive))
            world.Entities.Remove(c.Id);

        // Spawn next-generation heroes from stable settlements
        TrySpawnCivBorn(world, pending, tick);

        return pending;
    }

    // ─── Civ-born character generation ───────────────────────────────────────

    private const int SaltCivBirth = 1000;

    private void TrySpawnCivBorn(WorldState world, List<PendingEvent> pending, long tick)
    {
        foreach (var kvp in world.Settlements)
        {
            var stub = kvp.Value;
            if (stub.Population < _cfg.CivBirthMinPop) continue;
            if (!world.Civilizations.TryGetValue(stub.CivId, out var civ)) continue;
            if (civ.IsCollapsed) continue;

            // Probability scales with population above minimum
            float popFactor = Math.Min(3f, (float)stub.Population / _cfg.CivBirthMinPop);
            float chance    = _cfg.CivBirthChancePerSeason * popFactor;

            float r = WorldRng.FloatAt(world.WorldSeed, (int)tick, (int)(stub.FounderId.Value & 0x7FFFFFFF), 0, SaltCivBirth);
            if (r > chance) continue;

            // Unique entitySeq derived from tick + tile to stay deterministic
            long seq  = (50_000L + tick * 997L + kvp.Key.X * 31 + kvp.Key.Y) & 0x7FFFFFFF;
            var born  = CharacterFactory.Spawn(kvp.Key, world.WorldSeed, seq, _simCfg, world.CurrentYear);
            born.Identity = born.Identity with { CivId = stub.CivId };
            civ.Members.Add(born.Id);
            world.Entities.Add(born);

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                characterId = born.Id.Value,
                name        = born.Identity.Name,
                epithet     = born.Identity.Epithet,
                civId       = stub.CivId.Value,
                location    = new[] { born.Location.X, born.Location.Y },
                ambition    = born.Personality.Ambition,
                aggression  = born.Personality.Aggression
            });
            pending.Add(new PendingEvent(EventType.CharacterBorn, born.Location, null, payload,
                new[] { born.Id.Value }));
        }
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void UpdateLifecycle(
        Tier1Character c, WorldState world, long tick, List<PendingEvent> pending)
    {
        c.AgeSeason++;

        // Health regeneration
        if (c.Health < _cfg.MaxHealth)
            c.Health = Math.Min(_cfg.MaxHealth, c.Health + _cfg.HealthPerSeasonHeal);

        // Death by old age
        if (c.AgeSeason >= c.MaxAgeSeason)
        {
            KillCharacter(c, world, "old age", pending);
            return;
        }

        // Death from unmet needs (starvation, etc.)
        if (c.Needs.Food <= 0f || c.Needs.Safety <= 0f)
        {
            KillCharacter(c, world,
                c.Needs.Food <= 0f ? "starvation" : "violence", pending);
        }
    }

    private static void KillCharacter(
        Tier1Character c, WorldState world, string cause, List<PendingEvent> pending)
    {
        c.IsAlive = false;

        var payload = JsonSerializer.Serialize(new
        {
            characterId   = c.Id.Value,
            characterName = c.Identity.Name,
            cause,
            ageSeason     = c.AgeSeason,
            tile          = new[] { c.Location.X, c.Location.Y }
        });
        pending.Add(new PendingEvent(EventType.CharacterDied, c.Location, null, payload,
            new[] { c.Id.Value }));

        // Handle succession if they founded a civilization
        if (c.Identity.CivId.IsValid
            && world.Civilizations.TryGetValue(c.Identity.CivId, out var civ))
        {
            civ.Members.Remove(c.Id);
            if (civ.Members.Count == 0 && !civ.IsCollapsed)
            {
                civ.IsCollapsed = true;
                var civPayload = JsonSerializer.Serialize(new
                {
                    civId   = civ.Id.Value,
                    civName = civ.Name,
                    year    = world.CurrentYear
                });
                pending.Add(new PendingEvent(
                    EventType.CivilizationCollapsed, civ.CapitalTile, null, civPayload));
            }
        }
    }

    // ─── Command resolution ────────────────────────────────────────────────────

    private void ResolveCommand(
        ICommand cmd,
        Tier1Character c,
        WorldState world,
        List<PendingEvent> pending,
        long tick)
    {
        switch (cmd)
        {
            case MoveToTile move:
                ResolveMove(c, move.Destination, world);
                break;
            case Rest:
                ResolveRest(c);
                break;
            case EstablishSettlement:
            case AllyWith:
            case DeclareRivalry:
            case DeclareWar:
            case RaidSettlement:
            case Negotiate:
                CivTracker.Resolve(cmd, world, pending);
                break;
        }
    }

    private static void ResolveMove(Tier1Character c, TileCoord dest, WorldState world)
    {
        world.Entities.UpdateLocation(c.Id, c.Location, dest);
        c.Location = dest;
        c.TicksInCurrentTile = 0;
    }

    private static void ResolveRest(Tier1Character c)
    {
        // Resting restores needs slightly above stub ambient recovery
        c.Needs = c.Needs with
        {
            Safety  = Math.Min(1f, c.Needs.Safety  + 0.05f),
            Food    = Math.Min(1f, c.Needs.Food    + 0.05f),
            Shelter = Math.Min(1f, c.Needs.Shelter + 0.03f)
        };
    }

    // ─── Territorial pressure ────────────────────────────────────────────────

    /// <summary>
    /// Aggressive founders who see foreign chars on their settlement tile slowly
    /// develop negative trust with them — the seed of rivalry and eventual war.
    /// </summary>
    private void ApplyTerritorialPressure(Tier1Character c, WorldState world, long tick)
    {
        if (c.Personality.Aggression < _cfg.TerritorialAggressionMin) return;
        if (!world.Settlements.ContainsKey(c.Location)) return;

        // Only applies to the founding char (or any char with high aggression at their own settlement)
        bool atOwnSettlement = world.Settlements.TryGetValue(c.Location, out var stub)
            && stub.CivId == c.Identity.CivId;
        if (!atOwnSettlement) return;

        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            if (other.Identity.CivId == c.Identity.CivId) continue; // same civ — no pressure

            var rel = world.Relationships.GetOrCreate(c.Id, other.Id);
            if (rel.IsAlly || rel.IsRival || rel.IsAtWar) continue; // relationship already decided

            // Drain trust by a small amount each tick — enough to reach -0.1 within ~5 years of contact
            world.Relationships.Upsert(rel with
            {
                Trust = Math.Max(-0.5f, rel.Trust - _cfg.TerritorialTrustDrain)
            });
        }
    }

    // ─── Beast encounters ────────────────────────────────────────────────────

    private const int SaltBeastEncounter = 800;

    /// <summary>
    /// When a predatory beast and a character share a tile, there is a chance
    /// the beast attacks. Characters can be wounded or killed; this creates the
    /// adventure/survival events the history log needs.
    /// </summary>
    private void CheckBeastEncounters(
        Tier1Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Entities.Beasts.LegendaryBeast beast || !beast.IsAlive) continue;
            if (beast.Aggression < _cfg.BeastEncounterAggressionMin) continue;

            float roll = WorldRng.FloatAt(world.WorldSeed, tick,
                                          (int)(beast.Id.Value & 0x7FFFFFFF),
                                          (int)(c.Id.Value & 0x7FFFFFFF),
                                          SaltBeastEncounter);
            if (roll > _cfg.BeastEncounterChance) continue;

            int damage = Math.Max(1, (int)(beast.Strength * _cfg.BeastDamageMultiplier));
            c.Health -= damage;

            var payload = JsonSerializer.Serialize(new
            {
                characterId   = c.Id.Value,
                characterName = c.Identity.Name,
                beastId       = beast.Id.Value,
                beastName     = beast.Name,
                damage,
                charHealthAfter = c.Health,
                tile = new[] { c.Location.X, c.Location.Y }
            });
            pending.Add(new PendingEvent(EventType.BeastAttackedChar, c.Location, null, payload,
                new[] { c.Id.Value, beast.Id.Value }));

            if (c.Health <= 0)
                KillCharacter(c, world, $"killed by {beast.Name}", pending);
        }
    }
}
