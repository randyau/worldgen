using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
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
            RunRoleBehavior(c, world, pending, tick);
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
            var payload = JsonSerializer.Serialize(new CharacterDeathPayload(
                c.Id.Value, c.Name,
                c.AgeSeason >= c.MaxAgeSeason ? "old age" : "needs",
                c.AgeSeason));
            pending.Add(new PendingEvent(EventType.CharacterDied, c.Location, null, payload,
                new[] { c.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Name));
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
        Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        switch (c.Livelihood.Role)
        {
            case Tier2Role.Merchant:
                RunMerchant(c, world, pending, tick); break;
            case Tier2Role.Scholar:
                RunScholar(c, world, pending, tick); break;
            case Tier2Role.General:
                RunGeneral(c, world); break;
            case Tier2Role.Physician:
                RunPhysician(c, world, pending, tick); break;
            case Tier2Role.Artisan:
                RunArtisan(c, world, pending, tick); break;
            // Governor is fully ambient — effect is captured in needs recovery above
        }
    }

    // Returns true and emits a notable event if the creator's cooldown has cleared,
    // then rolls the exceptional (masterwork) check.
    private bool TryEmitNotableWork(
        Tier2Character c, WorldState world, long tick,
        EventType eventType, string payload, long[]? primaryIds, long[]? secondaryIds,
        List<PendingEvent> pending)
    {
        if (tick - c.LastNotableWorkTick <= _cfg.Tier2NotableCooldownTicks) return false;

        c.LastNotableWorkTick = (int)tick;
        pending.Add(new PendingEvent(eventType, c.Location, null, payload,
            primaryIds, secondaryIds,
            ActorId: c.Id.Value, ActorName: c.Name));

        // Exceptional (masterwork) check — once per lifetime
        if (!c.HasMasterwork)
        {
            float excepRoll = world.GetRandomFloat(c.Id, GetExceptionalSalt(c.Livelihood.Role));
            if (excepRoll < _cfg.Tier2ExceptionalWorkChance)
            {
                c.HasMasterwork = true;
                // V2: ARTIFACT — when the artifact system is live, emit ArtifactCreated here
                // using the same payload decorated with isExceptional=true
            }
        }
        return true;
    }

    private static int GetExceptionalSalt(Tier2Role role) => role switch
    {
        Tier2Role.Artisan   => S.T2ArtisanExcep,
        Tier2Role.Scholar   => S.T2ScholarExcep,
        Tier2Role.Merchant  => S.T2MerchantExcep,
        Tier2Role.Physician => S.T2PhysicianExcep,
        _                   => S.T2ArtisanExcep,
    };

    private const float MerchantTradeTransfer = 0.1f;  // fraction of surplus transferred per trade

    private void RunMerchant(Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
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

            var homeStores = home.ResourceStores;
            if (homeStores is null) continue;

            foreach (var (res, homeAmount) in homeStores)
            {
                if (homeAmount <= 0f) continue;
                float destAmount  = dest.GetStore(res);
                float opportunity = homeAmount - destAmount;
                if (isAllyDest) opportunity += homeAmount * 0.3f;
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

        // Transfer resources (always, silent)
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

        // Notable event: only when cooldown has cleared (most trades are silent)
        var payload = JsonSerializer.Serialize(new MerchantTradePayload(
            c.Id.Value, c.Name, bestResource ?? "general",
            bestDest.Value.X, bestDest.Value.Y));
        TryEmitNotableWork(c, world, tick, EventType.MerchantTradeCompleted,
            payload, [c.Id.Value], null, pending);
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

    private void RunScholar(Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        float r = world.GetRandomFloat(c.Id, S.T2Scholar);
        float discoveryChance = _cfg.ScholarDiscoveryChance * c.Personality.Rationality;
        if (r > discoveryChance) return;

        // Pick discovery type weighted by personality
        int typeCount = Enum.GetValues<DiscoveryType>().Length;
        int typeIndex = (int)(world.GetRandomFloat(c.Id, S.T2Scholar + 1) * typeCount) % typeCount;
        var discovery = (DiscoveryType)typeIndex;
        string bonusKey = DiscoveryBonusKey[typeIndex];

        // Apply discovery bonus silently (always)
        if (c.Livelihood.SettlementTile != default
            && world.Settlements.TryGetValue(c.Livelihood.SettlementTile, out var homeStub))
        {
            var stores = homeStub.ResourceStores is null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(homeStub.ResourceStores, StringComparer.OrdinalIgnoreCase);
            stores[bonusKey] = (stores.TryGetValue(bonusKey, out var cur) ? cur : 0f) + _cfg.ScholarDiscoveryBonusAmount;
            world.Settlements[c.Livelihood.SettlementTile] = homeStub with { ResourceStores = stores };
        }

        // Notable event: only when cooldown has cleared (most scholarly work is routine)
        var payload = JsonSerializer.Serialize(new ScholarDiscoveryPayload(
            c.Id.Value, c.Name, discovery.ToString(), bonusKey, _cfg.ScholarDiscoveryBonusAmount));
        TryEmitNotableWork(c, world, tick, EventType.ScholarDiscovery,
            payload, [c.Id.Value], null, pending);
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

    private void RunPhysician(Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        // 1. Heal the nearest injured Tier1 character in the same tile (always, silent).
        // Notable event fires only when cooldown allows — most healing goes unrecorded.
        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Entities.Characters.Tier1Character t1) continue;
            if (t1.Health >= t1.MaxHealth) continue;

            int healed = (int)(t1.MaxHealth * 0.1f);
            t1.Health = Math.Min(t1.MaxHealth, t1.Health + healed);

            var payload = JsonSerializer.Serialize(new PhysicianHealedPayload(
                c.Id.Value, c.Name, t1.Id.Value, t1.Identity.Name,
                healed, t1.Health <= t1.MaxHealth / 4));
            TryEmitNotableWork(c, world, tick, EventType.PhysicianHealed,
                payload, [c.Id.Value], [t1.Id.Value], pending);
            break; // one patient per tick
        }

        // 2. Reduce disease burden on the physician's home settlement (always, silent)
        if (c.Livelihood.SettlementTile == default) return;
        if (!world.Settlements.TryGetValue(c.Livelihood.SettlementTile, out var stub)) return;
        if (!stub.IsInfected) return;
        float healRate = _cfg.PhysicianSettlementHealRate * c.Personality.Rationality;
        world.Settlements[c.Livelihood.SettlementTile] = stub with
            { Health = (int)Math.Min(100f, stub.Health + healRate) };
    }

    private static readonly string[] ArtisanGoodType = [
        "textiles", "pottery", "metalwork", "woodcraft", "leatherwork", "stonework",
    ];

    private void RunArtisan(Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        // Artisans work every tick (ambient economic contribution), but notable craftsmanship
        // is occasional. The exceptional (masterwork) path is once per lifetime.
        float r = world.GetRandomFloat(c.Id, S.T2General);
        if (r > 0.25f) return;  // most ticks produce silent routine goods

        int goodCount = ArtisanGoodType.Length;
        int goodIndex = (int)(world.GetRandomFloat(c.Id, S.T2General + 1) * goodCount) % goodCount;
        string goodType = ArtisanGoodType[goodIndex];

        // Ambient bonus: slightly raise settlement Status recovery via crafted goods
        if (world.Settlements.TryGetValue(c.Location, out var homeStub))
        {
            var stores = homeStub.ResourceStores is null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(homeStub.ResourceStores, StringComparer.OrdinalIgnoreCase);
            stores["bonus_civ_cohesion"] = (stores.TryGetValue("bonus_civ_cohesion", out var cur) ? cur : 0f) + 0.01f;
            world.Settlements[c.Location] = homeStub with { ResourceStores = stores };
        }

        var payload = JsonSerializer.Serialize(new ArtisanCraftedPayload(
            c.Id.Value, c.Name, goodType));
        TryEmitNotableWork(c, world, tick, EventType.ArtisanCrafted,
            payload, [c.Id.Value], null, pending);
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

        pending.Add(new PendingEvent(EventType.CharacterCrystallized, c.Location, null,
            JsonSerializer.Serialize(new CharacterCrystallizedPayload(
                c.Id.Value, c.Name, promoted.Id.Value, promoted.Identity.Name)),
            new[] { promoted.Id.Value }, new[] { c.Id.Value },
            ActorId: promoted.Id.Value, ActorName: promoted.Identity.Name));
        pending.Add(new PendingEvent(EventType.CharacterBorn, c.Location, null,
            JsonSerializer.Serialize(new CharacterBornPayload(
                promoted.Id.Value, promoted.Identity.Name, promoted.Identity.Epithet,
                promoted.Personality.Ambition, promoted.Personality.Aggression,
                Source: "crystallized")),
            new[] { promoted.Id.Value },
            ActorId: promoted.Id.Value, ActorName: promoted.Identity.Name));
    }
}
