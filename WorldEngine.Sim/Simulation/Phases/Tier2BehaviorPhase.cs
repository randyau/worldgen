using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Phase 5b — updates Tier 2 characters each tick.
/// Needs decay, role behavior (fixed per Tier2Role), lifecycle, crystallization.
/// </summary>
public sealed class Tier2BehaviorPhase
{
    private readonly CharacterSimConfig _cfg;
    private readonly SimConfig _simCfg;

    private const int SaltCrystal    = 900;
    private const int SaltScholar    = 910;
    private const int SaltMerchant   = 920;

    public Tier2BehaviorPhase(SimConfig cfg)
    {
        _simCfg = cfg;
        _cfg    = cfg.Character;
    }

    public List<PendingEvent> Execute(WorldState world, long tick)
    {
        var pending = new List<PendingEvent>();
        var chars = world.Entities.Tier2Chars.ToList();

        foreach (var c in chars)
        {
            if (!c.IsAlive) continue;
            UpdateLifecycle(c, world, tick, pending);
            if (!c.IsAlive) continue;
            UpdateNeeds(c, world);
            RunRoleBehavior(c, world, pending);
            TryCrystallize(c, world, pending, tick);
        }

        foreach (var c in chars.Where(ch => !ch.IsAlive))
            world.Entities.Remove(c.Id);

        return pending;
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private static void UpdateLifecycle(
        Tier2Character c, WorldState world, long tick, List<PendingEvent> pending)
    {
        c.AgeSeason++;

        if (c.AgeSeason >= c.MaxAgeSeason || c.Needs.Food <= 0f || c.Needs.Safety <= 0f)
        {
            c.IsAlive = false;
            var payload = JsonSerializer.Serialize(new
            {
                characterId   = c.Id.Value,
                characterName = c.Name,
                role          = c.Livelihood.Role.ToString(),
                ageSeason     = c.AgeSeason
            });
            pending.Add(new PendingEvent(EventType.CharacterDied, c.Location, null, payload,
                new[] { c.Id.Value }));
        }
    }

    // ─── Needs ────────────────────────────────────────────────────────────────

    private void UpdateNeeds(Tier2Character c, WorldState world)
    {
        var n = c.Needs;
        n.Food      = Math.Max(0f, n.Food      - _cfg.Tier2NeedsDecayFood);
        n.Safety    = Math.Max(0f, n.Safety    - _cfg.Tier2NeedsDecaySafety);
        n.Belonging = Math.Max(0f, n.Belonging - _cfg.Tier2NeedsDecayBelonging);
        n.Status    = Math.Max(0f, n.Status    - _cfg.Tier2NeedsDecayStatus);

        // Recovery stubs
        n.Food   = Math.Min(1f, n.Food   + 0.07f); // lower food web
        n.Safety = Math.Min(1f, n.Safety + 0.05f); // ambient safety

        if (world.Settlements.ContainsKey(c.Location))
        {
            n.Belonging = Math.Min(1f, n.Belonging + 0.05f);
            n.Status    = Math.Min(1f, n.Status    + 0.03f * c.Personality.Diligence);
        }

        c.Needs = n;
    }

    // ─── Role Behavior ────────────────────────────────────────────────────────

    private void RunRoleBehavior(
        Tier2Character c, WorldState world, List<PendingEvent> pending)
    {
        switch (c.Livelihood.Role)
        {
            case Tier2Role.Merchant:
                RunMerchant(c, world, pending); break;
            case Tier2Role.Scholar:
                RunScholar(c, world, pending); break;
            case Tier2Role.General:
                RunGeneral(c, world); break;
            case Tier2Role.Physician:
                RunPhysician(c, world, pending); break;
            // Governor and Artisan are ambient — effect is captured in needs recovery above
        }
    }

    private void RunMerchant(Tier2Character c, WorldState world, List<PendingEvent> pending)
    {
        // Trade fires when a second settlement exists
        if (world.Settlements.Count < 2) return;
        float r = world.GetRandomFloat(c.Id, SaltMerchant);
        if (r > 0.15f) return; // ~15% chance per season

        // Pick a distinct destination settlement
        TileCoord? dest = null;
        foreach (var kv in world.Settlements)
        {
            if (kv.Key != c.Livelihood.SettlementTile) { dest = kv.Key; break; }
        }
        if (dest is null) return;

        c.Needs = c.Needs with { Status = Math.Min(1f, c.Needs.Status + 0.05f) };

        var payload = JsonSerializer.Serialize(new
        {
            merchantId = c.Id.Value,
            name       = c.Name,
            fromTile   = new[] { c.Livelihood.SettlementTile.X, c.Livelihood.SettlementTile.Y },
            toTile     = new[] { dest.Value.X, dest.Value.Y }
        });
        pending.Add(new PendingEvent(EventType.MerchantTradeCompleted, c.Location, null, payload,
            new[] { c.Id.Value }));
    }

    private void RunScholar(Tier2Character c, WorldState world, List<PendingEvent> pending)
    {
        float r = world.GetRandomFloat(c.Id, SaltScholar);
        float discoveryChance = 0.04f * c.Personality.Rationality;
        if (r > discoveryChance) return;

        var payload = JsonSerializer.Serialize(new
        {
            scholarId   = c.Id.Value,
            scholarName = c.Name,
            location    = new[] { c.Location.X, c.Location.Y }
        });
        pending.Add(new PendingEvent(EventType.ScholarDiscovery, c.Location, null, payload,
            new[] { c.Id.Value }));
    }

    private static void RunGeneral(Tier2Character c, WorldState world)
    {
        // Ambient: slightly boost Safety need of nearby Tier1 ally
        if (c.Livelihood.EmployerId is { } eid
            && world.GetEntity(eid) is Entities.Characters.Tier1Character employer)
        {
            if (employer.Location == c.Location)
            {
                employer.Needs = employer.Needs with
                    { Safety = Math.Min(1f, employer.Needs.Safety + 0.03f) };
            }
        }
    }

    private void RunPhysician(Tier2Character c, WorldState world, List<PendingEvent> pending)
    {
        // Heal the nearest injured Tier1 character in the same tile
        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Entities.Characters.Tier1Character t1) continue;
            if (t1.Health >= t1.MaxHealth) continue;

            int healed = (int)(t1.MaxHealth * 0.1f);
            t1.Health = Math.Min(t1.MaxHealth, t1.Health + healed);

            var payload = JsonSerializer.Serialize(new
            {
                physicianId = c.Id.Value,
                patientId   = t1.Id.Value,
                healed
            });
            pending.Add(new PendingEvent(EventType.PhysicianHealed, c.Location, null, payload,
                new[] { c.Id.Value, t1.Id.Value }));
            break; // one patient per tick
        }
    }

    // ─── Crystallization ──────────────────────────────────────────────────────

    private void TryCrystallize(
        Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        if (c.Personality.Ambition < 0.8f || c.Needs.Status < 0.7f) return;
        float r = world.GetRandomFloat(c.Id, SaltCrystal);
        if (r > _cfg.Tier2CrystalChance) return;

        c.IsAlive = false; // remove from Tier2 list

        // Spawn a Tier1 with matching name and elevated personality
        var promoted = CharacterFactory.Spawn(
            location:   c.Location,
            worldSeed:  world.WorldSeed,
            entitySeq:  (int)(c.Id.Value & 0x7FFFFFFF),
            config:     _simCfg,
            birthYear:  world.CurrentYear);

        world.Entities.Add(promoted);

        var payload = JsonSerializer.Serialize(new
        {
            oldCharacterId  = c.Id.Value,
            oldName         = c.Name,
            newCharacterId  = promoted.Id.Value,
            newName         = promoted.Identity.Name,
            location        = new[] { c.Location.X, c.Location.Y }
        });
        pending.Add(new PendingEvent(EventType.CharacterCrystallized, c.Location, null, payload,
            new[] { c.Id.Value, promoted.Id.Value }));
        pending.Add(new PendingEvent(EventType.CharacterBorn, c.Location, null,
            JsonSerializer.Serialize(new
            {
                characterId = promoted.Id.Value,
                name        = promoted.Identity.Name,
                epithet     = promoted.Identity.Epithet,
                location    = new[] { c.Location.X, c.Location.Y }
            }),
            new[] { promoted.Id.Value }));
    }
}
