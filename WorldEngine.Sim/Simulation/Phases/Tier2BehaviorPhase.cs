using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.World;
using S = WorldEngine.Sim.Simulation.SimRngSalts;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Phase 5b — updates Tier 2 characters each tick.
/// Needs decay, role behavior (fixed per Tier2Role), lifecycle, crystallization.
/// </summary>
public sealed class Tier2BehaviorPhase
{
    private readonly CharacterSimConfig _cfg;
    private readonly SimConfig _simCfg;

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

        // Grief: notify any Tier1 ruler who had a Bond goal targeting a Tier2 that died.
        var deadTier2 = chars.Where(ch => !ch.IsAlive).ToList();
        foreach (var dead in deadTier2)
        {
            var mourners = new List<(EntityId, float)>();
            GoalManager.ApplyGriefToMourners(dead.Id, dead.Name, world, _cfg, mourners, pending);
            foreach (var (mournerId, _) in mourners)
            {
                if (world.GetEntity(mournerId) is Tier1Character mourner && mourner.IsAlive)
                    GoalManager.EmitGriefEvent(mourner, dead.Id, dead.Name, pending);
            }
            world.Entities.Remove(dead.Id);
        }

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

    private const float MerchantTradeTransfer = 0.1f;  // fraction of surplus transferred per trade

    private void RunMerchant(Tier2Character c, WorldState world, List<PendingEvent> pending)
    {
        if (world.Settlements.Count < 2) return;
        float r = world.GetRandomFloat(c.Id, S.T2Merchant);
        if (r > 0.15f) return;

        var homeTile = c.Livelihood.SettlementTile;
        if (!world.Settlements.TryGetValue(homeTile, out var home)) return;

        // Find the best complementary destination: where home has surplus stores, dest has less.
        // Uses ResourceStores (persistent) not ResourceLedger (ephemeral per-tick ratios).
        TileCoord? bestDest     = null;
        string?    bestResource = null;
        float      bestScore    = 0f;

        foreach (var (destTile, dest) in world.Settlements)
        {
            if (destTile == homeTile) continue;
            bool isAllyDest = IsAlliedWithDestination(home, dest, world);

            // Score each resource by (homeStores - destStores); higher = better trade opportunity
            var homeStores = home.ResourceStores;
            if (homeStores is null) continue;

            foreach (var (res, homeAmount) in homeStores)
            {
                if (homeAmount <= 0f) continue;
                float destAmount  = dest.GetStore(res);
                float opportunity = homeAmount - destAmount;
                if (isAllyDest) opportunity += homeAmount * 0.3f; // prefer allied routes
                if (opportunity > bestScore)
                {
                    bestScore    = opportunity;
                    bestDest     = destTile;
                    bestResource = res;
                }
            }
        }

        // Fallback: pick any settlement when stores are all empty
        if (bestDest is null)
        {
            foreach (var kv in world.Settlements)
                if (kv.Key != homeTile) { bestDest = kv.Key; break; }
        }
        if (bestDest is null) return;

        // Transfer from home ResourceStores to destination ResourceStores — this is now persistent.
        if (bestResource is not null && world.Settlements.TryGetValue(bestDest.Value, out var destStub))
        {
            float available = home.GetStore(bestResource);
            float transfer  = available * MerchantTradeTransfer;
            if (transfer > 0f)
            {
                var newHomeStores = home.ResourceStores is null
                    ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, float>(home.ResourceStores, StringComparer.OrdinalIgnoreCase);
                newHomeStores[bestResource] = Math.Max(0f, available - transfer);

                var newDestStores = destStub.ResourceStores is null
                    ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, float>(destStub.ResourceStores, StringComparer.OrdinalIgnoreCase);
                newDestStores[bestResource] = destStub.GetStore(bestResource) + transfer;

                world.Settlements[homeTile]       = home     with { ResourceStores = newHomeStores };
                world.Settlements[bestDest.Value] = destStub with { ResourceStores = newDestStores };
            }
        }

        c.Needs = c.Needs with { Status = Math.Min(1f, c.Needs.Status + 0.05f) };

        var payload = JsonSerializer.Serialize(new
        {
            merchantId   = c.Id.Value,
            name         = c.Name,
            fromTile     = new[] { homeTile.X, homeTile.Y },
            toTile       = new[] { bestDest.Value.X, bestDest.Value.Y },
            tradedResource = bestResource ?? "general"
        });
        pending.Add(new PendingEvent(EventType.MerchantTradeCompleted, c.Location, null, payload,
            new[] { c.Id.Value }));
    }

    // Checks if home founder has an ally whose CivId matches the destination settlement's civ.
    // No civ-level alliance concept yet; this proxies it via the founder's personal relationships.
    private static bool IsAlliedWithDestination(SettlementStub home, SettlementStub dest, WorldState world)
    {
        if (!home.CivId.IsValid || !dest.CivId.IsValid || home.CivId == dest.CivId) return false;
        foreach (var edge in world.Relationships.GetAll(home.FounderId).Where(e => e.IsAlly))
        {
            var allyId = edge.From == home.FounderId ? edge.To : edge.From;
            if (world.GetEntity(allyId) is Tier1Character ally && ally.Identity.CivId == dest.CivId)
                return true;
        }
        return false;
    }

    private static readonly string[] DiscoveryBonusKey = [
        "bonus_food_yield",          // Agriculture
        "bonus_disease_resistance",  // Medicine
        "bonus_navigation",          // Astronomy
        "bonus_trade_income",        // Mathematics
        "bonus_construction_speed",  // Engineering
        "bonus_civ_cohesion",        // Philosophy
        "bonus_exploration_range",   // Navigation
        "bonus_military_strength",   // Metallurgy
    ];

    private void RunScholar(Tier2Character c, WorldState world, List<PendingEvent> pending)
    {
        float r = world.GetRandomFloat(c.Id, S.T2Scholar);
        float discoveryChance = _cfg.ScholarDiscoveryChance * c.Personality.Rationality;
        if (r > discoveryChance) return;

        // Weighted by personality — rational scholars lean toward hard sciences,
        // spiritual ones toward philosophy, curious ones anywhere.
        int typeCount = Enum.GetValues<DiscoveryType>().Length;
        int typeIndex = (int)(world.GetRandomFloat(c.Id, S.T2Scholar + 1) * typeCount) % typeCount;
        var discovery = (DiscoveryType)typeIndex;
        string bonusKey = DiscoveryBonusKey[typeIndex];

        // Apply discovery bonus to the scholar's home settlement ResourceStores
        if (c.Livelihood.SettlementTile != default
            && world.Settlements.TryGetValue(c.Livelihood.SettlementTile, out var homeStub))
        {
            var stores = homeStub.ResourceStores is null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(homeStub.ResourceStores, StringComparer.OrdinalIgnoreCase);
            stores[bonusKey] = (stores.TryGetValue(bonusKey, out var cur) ? cur : 0f) + _cfg.ScholarDiscoveryBonusAmount;
            world.Settlements[c.Livelihood.SettlementTile] = homeStub with { ResourceStores = stores };
        }

        var payload = JsonSerializer.Serialize(new
        {
            scholarId     = c.Id.Value,
            scholarName   = c.Name,
            discoveryType = discovery.ToString(),
            bonusKey,
            bonusAmount   = _cfg.ScholarDiscoveryBonusAmount,
            location      = new[] { c.Location.X, c.Location.Y }
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
        // 1. Heal the nearest injured Tier1 character in the same tile
        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Entities.Characters.Tier1Character t1) continue;
            if (t1.Health >= t1.MaxHealth) continue;

            int healed = (int)(t1.MaxHealth * 0.1f);
            t1.Health = Math.Min(t1.MaxHealth, t1.Health + healed);

            var payload = JsonSerializer.Serialize(new
            {
                physicianId  = c.Id.Value,
                physicianName = c.Name,
                patientId    = t1.Id.Value,
                patientName  = t1.Identity.Name,
                healed,
                location     = new[] { c.Location.X, c.Location.Y }
            });
            pending.Add(new PendingEvent(EventType.PhysicianHealed, c.Location, null, payload,
                new[] { c.Id.Value, t1.Id.Value }));
            break; // one patient per tick
        }

        // 2. Reduce disease burden on the physician's home settlement each tick.
        // Physicians slow the spread and improve recovery odds — modeled as a
        // direct health recovery bonus on infected settlements.
        if (c.Livelihood.SettlementTile == default) return;
        if (!world.Settlements.TryGetValue(c.Livelihood.SettlementTile, out var stub)) return;
        if (!stub.IsInfected) return;
        float healRate = _cfg.PhysicianSettlementHealRate * c.Personality.Rationality;
        world.Settlements[c.Livelihood.SettlementTile] = stub with
            { Health = (int)Math.Min(100f, stub.Health + healRate) };
    }

    // ─── Crystallization ──────────────────────────────────────────────────────

    private void TryCrystallize(
        Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        if (c.Personality.Ambition < 0.8f || c.Needs.Status < 0.7f) return;
        float r = world.GetRandomFloat(c.Id, S.T2General);
        if (r > _cfg.Tier2CrystalChance) return;

        c.IsAlive = false; // remove from Tier2 list

        // Spawn a Tier1 with matching name and elevated personality
        var promoted = CharacterFactory.Spawn(
            location:   c.Location,
            biome:      (BiomeType)world.TileGrid.GetTile(c.Location).BiomeType,
            worldSeed:  world.WorldSeed,
            entitySeq:  (int)(c.Id.Value & 0x7FFFFFFF),
            config:     _simCfg,
            birthYear:  world.CurrentYear);

        int promotedOrdinal = world.ClaimNameOrdinal(promoted.Identity.Name);
        if (promotedOrdinal > 0)
            promoted.Identity = promoted.Identity with { NameOrdinal = promotedOrdinal };

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
