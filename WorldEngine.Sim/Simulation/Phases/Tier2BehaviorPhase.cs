using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Artifacts;
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

    private void UpdateLifecycle(
        Tier2Character c, WorldState world, long tick, List<PendingEvent> pending)
    {
        c.AgeSeason++;

        if (c.AgeSeason >= c.MaxAgeSeason || c.Needs.Food <= 0f || c.Needs.Safety <= 0f)
        {
            string deathCause = c.AgeSeason >= c.MaxAgeSeason ? "old age" : "needs";
            c.IsAlive = false;

            // M13.8.1: a Tier2 can now be a rivalry target (CivTracker.ResolveRivalry) — mirror
            // CharacterBehaviorPhase.KillCharacter's cleanup so a dead Tier2 doesn't leave a
            // dangling IsRival edge inflating the surviving Tier1's CountRivals cap forever.
            foreach (var edge in world.Relationships.GetAll(c.Id).ToList())
            {
                if (edge.IsRival)
                {
                    world.Relationships.Upsert(edge with
                    {
                        Flags = edge.Flags & ~RelationshipFlags.IsRival
                    });
                }
            }

            var deathPayload = JsonSerializer.Serialize(new CharacterDeathPayload(
                c.Id.Value, c.Name, deathCause, c.AgeSeason));
            pending.Add(new PendingEvent(EventType.CharacterDied, c.Location, null, deathPayload,
                new[] { c.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Name));

            // Artifact inheritance — transfer or lose owned artifacts on death
            HandleTier2ArtifactInheritance(c, world, pending);

            // Emit DismissedFromRole when a Tier 2 specialist dies — their role ends with them.
            var dismissPayload = JsonSerializer.Serialize(new SpecialistDismissedPayload(
                c.Id.Value, c.Name, c.Livelihood.Role.ToString(), deathCause));
            pending.Add(new PendingEvent(EventType.DismissedFromRole, c.Location, null, dismissPayload,
                new[] { c.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Name));
        }
    }

    private void HandleTier2ArtifactInheritance(
        Tier2Character c, WorldState world, List<PendingEvent> pending)
    {
        var ownedArtifacts = ArtifactRegistry.OwnedByCharacter(world, c.Id).ToList();
        if (ownedArtifacts.Count == 0) return;

        TileCoord? settleTile = world.Settlements.ContainsKey(c.Location) ? c.Location
            : c.Livelihood.SettlementTile != default && world.Settlements.ContainsKey(c.Livelihood.SettlementTile)
                ? c.Livelihood.SettlementTile
            : null;

        // Use _simCfg (injected at construction) so test overrides take effect
        var artCfg = _simCfg.Artifacts;
        for (int i = 0; i < ownedArtifacts.Count; i++)
        {
            var artifact = ownedArtifacts[i];
            float lossRoll = WorldRng.FloatAt(world.WorldSeed, world.CurrentTick,
                (int)(c.Id.Value & 0xFFFF), i, S.ArtifactDeathInheritance);

            string fromOwner = artifact.Owner.Describe();
            ArtifactOwner newOwner;
            string toOwnerDesc;

            if (lossRoll < artCfg.LostOnDeathProbability || settleTile is null)
            {
                newOwner    = ArtifactOwner.Lost;
                toOwnerDesc = ArtifactOwner.Lost.Describe();
            }
            else
            {
                newOwner    = ArtifactOwner.OfSettlement(settleTile.Value);
                toOwnerDesc = newOwner.Describe();
            }

            ArtifactRegistry.SetOwner(world, artifact.Id, newOwner);

            var transPayload = JsonSerializer.Serialize(new ArtifactTransferredPayload(
                artifact.Id.Value, artifact.Name, fromOwner, toOwnerDesc, "inheritance"));
            pending.Add(new PendingEvent(EventType.ArtifactTransferred, c.Location, null, transPayload,
                new[] { artifact.Id.Value },
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
        n.Food   = Math.Min(1f, n.Food   + _cfg.Tier2AmbientFoodRecovery);   // lower food web
        n.Safety = Math.Min(1f, n.Safety + _cfg.Tier2AmbientSafetyRecovery); // ambient safety

        if (world.Settlements.ContainsKey(c.Location))
        {
            n.Belonging = Math.Min(1f, n.Belonging + _cfg.Tier2SettlementBelongingRecovery);
            n.Status    = Math.Min(1f, n.Status    + _cfg.Tier2SettlementStatusRecoveryBase * c.Personality.Diligence);
        }

        c.Needs = n;

        // M13.8.2: Notability decays each tick — it tracks recent drama exposure, not a permanent trait.
        c.Notability = Math.Max(0f, c.Notability - _cfg.Tier2NotabilityDecayRate);
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
        List<PendingEvent> pending, CreatedGoodType? good = null)
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

                // Forge a masterwork artifact and emit ArtifactCreated.
                // Quality derived from the exceptional roll (higher roll → higher quality within the masterwork band).
                float quality = Math.Clamp(
                    _cfg.MasterworkQualityBase + excepRoll * _cfg.MasterworkQualityRollScale, 0f, 1f);
                // M9 G-1: category derives from the actual good being made when one exists
                // (Artisan/Scholar); roles without a "product" (General/Governor/Merchant/
                // Physician) fall back to a role-based category — see FallbackRoleCategory.
                var cat = good is { } g
                    ? CreatedGoodTaxonomy.PickCategory(world, c.Id, g)
                    : FallbackRoleCategory(c.Livelihood.Role);
                var name = ArtifactNameGenerator.Generate(world, cat, (int)c.Id.Value);
                var artifact = ArtifactRegistry.Create(world, name, cat, world.CurrentYear,
                    creatorId:   c.Id.Value,
                    creatorName: c.Name,
                    origin:      "masterwork",
                    quality:     quality,
                    owner:       ArtifactOwner.OfCharacter(c.Id));

                var artPayload = JsonSerializer.Serialize(new ArtifactCreatedPayload(
                    artifact.Id.Value, artifact.Name, artifact.Category.ToString(),
                    c.Id.Value, c.Name, "masterwork", quality));
                pending.Add(new PendingEvent(EventType.ArtifactCreated, c.Location, null, artPayload,
                    new[] { c.Id.Value },
                    ActorId: c.Id.Value, ActorName: c.Name));
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

    // DECISION (M9 G-1): General/Governor/Merchant/Physician notable work is an *act*, not a
    // *product* — there's no CreatedGoodType to derive a category from, so these roles keep a
    // direct role→category map. General → Weapon (military), Governor → Regalia (rule),
    // Merchant → Jewelry (wealth), Physician → Relic (healing). Artisan/Scholar are no longer
    // routed here — they always pass a CreatedGoodType and use CreatedGoodTaxonomy.PickCategory.
    private static ArtifactCategory FallbackRoleCategory(Tier2Role role) => role switch
    {
        Tier2Role.General   => ArtifactCategory.Weapon,
        Tier2Role.Governor  => ArtifactCategory.Regalia,
        Tier2Role.Merchant  => ArtifactCategory.Jewelry,
        Tier2Role.Physician => ArtifactCategory.Relic,
        _                   => ArtifactCategory.Relic,
    };

    private void RunMerchant(Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        if (world.Settlements.Count < 2) return;
        float r = world.GetRandomFloat(c.Id, S.T2Merchant);
        if (r > _cfg.MerchantTradeChance) return;

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
                if (isAllyDest) opportunity += homeAmount * _cfg.MerchantAllyOpportunityBonus;

                // Demand-aware routing (M9 9.1): weight by the destination's per-capita demand
                // ratio for this resource (fresh — ResourcePressurePhase runs earlier this tick).
                // Ratio < 1 = destination is deficient → amplify; missing/vital-exempt → neutral.
                float demandWeight = 1f;
                if (dest.ResourceLedger is { } destLedger
                    && destLedger.TryGetValue(res, out float destRatio) && destRatio < 1f)
                {
                    demandWeight = Math.Min(_cfg.MerchantMaxDemandWeight, 1f / Math.Max(0.05f, destRatio));
                }

                // Specialization export bonus (M9 9.2): home settlements trade best in what
                // they're known for producing.
                float specWeight = string.Equals(res, home.Specialization, StringComparison.OrdinalIgnoreCase)
                    ? 1f + home.SpecializationStrength * _cfg.MerchantSpecializationBonusScale
                    : 1f;

                opportunity *= demandWeight * specWeight;

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
            // bonus_trade_income (M9 9.1, Scholar Mathematics discovery): scales the transfer
            // fraction for the merchant's home settlement, capped so it can't drain a store in one trade.
            float tradeIncomeMult = 1f + Math.Min(_cfg.TradeIncomeBonusCap,
                home.GetStore("bonus_trade_income") * _cfg.TradeIncomeBonusScale);
            float available = home.GetStore(bestResource);
            float transfer  = available * _cfg.MerchantTradeTransfer * tradeIncomeMult;
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

        c.Needs = c.Needs with { Status = Math.Min(1f, c.Needs.Status + _cfg.MerchantTradeStatusGain) };

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
            if (world.GetEntity(allyId) is Tier1Character ally && ally.CivId == dest.CivId)
                return true;
        }
        return false;
    }

    private void RunScholar(Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        float r = world.GetRandomFloat(c.Id, S.T2Scholar);
        float discoveryChance = _cfg.ScholarDiscoveryChance * c.Personality.Rationality;
        if (r > discoveryChance) return;

        // Pick discovery type weighted by personality
        var goods = CreatedGoodTaxonomy.DiscoveryGoods;
        int typeIndex = (int)(world.GetRandomFloat(c.Id, S.T2Scholar + 1) * goods.Length) % goods.Length;
        var discovery = goods[typeIndex];
        string bonusKey = CreatedGoodTaxonomy.DiscoveryBonusKeys[discovery];

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

        // Increment civ-level discovery counter for cultural trait evaluation
        if (c.Livelihood.SettlementTile != default
            && world.Settlements.TryGetValue(c.Livelihood.SettlementTile, out var scholarHome)
            && world.Civilizations.TryGetValue(scholarHome.CivId, out var scholarCiv))
        {
            scholarCiv.TotalScholarDiscoveries++;
        }

        // Notable event: only when cooldown has cleared (most scholarly work is routine)
        var payload = JsonSerializer.Serialize(new ScholarDiscoveryPayload(
            c.Id.Value, c.Name, discovery.ToString(), bonusKey, _cfg.ScholarDiscoveryBonusAmount));
        TryEmitNotableWork(c, world, tick, EventType.ScholarDiscovery,
            payload, [c.Id.Value], null, pending, discovery);
    }

    private void RunGeneral(Tier2Character c, WorldState world)
    {
        // Ambient: slightly boost Safety need of nearby Tier1 ally
        if (c.Livelihood.EmployerId is { } eid
            && world.GetEntity(eid) is Entities.Characters.Tier1Character employer)
        {
            if (employer.Location == c.Location)
            {
                employer.Needs = employer.Needs with
                    { Safety = Math.Min(1f, employer.Needs.Safety + _cfg.GeneralGuardSafetyBonus) };
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

            int healed = (int)(t1.MaxHealth * _cfg.PhysicianHealFraction);
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

    private void RunArtisan(Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        // Artisans work every tick (ambient economic contribution), but notable craftsmanship
        // is occasional. The exceptional (masterwork) path is once per lifetime.
        float r = world.GetRandomFloat(c.Id, S.T2General);
        if (r > _cfg.ArtisanCraftChance) return;  // most ticks produce silent routine goods

        var goods = CreatedGoodTaxonomy.ArtisanGoods;
        int goodIndex = (int)(world.GetRandomFloat(c.Id, S.T2General + 1) * goods.Length) % goods.Length;
        var goodType = goods[goodIndex];

        // Ambient bonus: slightly raise settlement Status recovery via crafted goods
        if (world.Settlements.TryGetValue(c.Location, out var homeStub))
        {
            var stores = homeStub.ResourceStores is null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(homeStub.ResourceStores, StringComparer.OrdinalIgnoreCase);
            stores["bonus_civ_cohesion"] = (stores.TryGetValue("bonus_civ_cohesion", out var cur) ? cur : 0f) + _cfg.ArtisanCohesionBonus;
            world.Settlements[c.Location] = homeStub with { ResourceStores = stores };
        }

        var payload = JsonSerializer.Serialize(new ArtisanCraftedPayload(
            c.Id.Value, c.Name, goodType.ToString()));
        TryEmitNotableWork(c, world, tick, EventType.ArtisanCrafted,
            payload, [c.Id.Value], null, pending, goodType);
    }

    // ─── Crystallization ──────────────────────────────────────────────────────

    private void TryCrystallize(
        Tier2Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        if (c.Personality.Ambition < _cfg.Tier2CrystalAmbitionThreshold) return;

        // M13.8.2: a Tier2 recently pulled into Tier1-driven drama (Notability) can satisfy the
        // gate on its own, without needing high settlement Status too — "currently prominent in
        // the community" and "recently touched by Tier1 drama" are distinct paths to the same roll.
        bool statusGate = c.Needs.Status >= _cfg.Tier2CrystalStatusThreshold
                        || c.Notability   >= _cfg.Tier2CrystalNotabilityThreshold;
        if (!statusGate) return;

        float chance = _cfg.Tier2CrystalChance + c.Notability * _cfg.Tier2CrystalNotabilityChanceBonus;
        float r = world.GetRandomFloat(c.Id, S.T2General);
        if (r > chance) return;

        PromoteToTier1(c, world, _simCfg, pending);
    }

    /// <summary>
    /// Promotes a Tier2 character to a full Tier1 hero, removing them from the Tier2 population and
    /// spawning a matching-name Tier1 in their place. Extracted from <see cref="TryCrystallize"/> so
    /// M13.8.1's marriage-to-Tier2 path (<c>CivTracker.ResolveMarriage</c>) can trigger the same
    /// promotion directly — a Tier1 proposing marriage to a Tier2 promotes them first, then the
    /// marriage resolves as an ordinary Tier1-Tier1 marriage — without duplicating the spawn logic.
    /// </summary>
    public static Tier1Character PromoteToTier1(
        Tier2Character c, WorldState world, SimConfig simCfg, List<PendingEvent> pending)
    {
        c.IsAlive = false; // remove from Tier2 list

        // Spawn a Tier1 with matching name and elevated personality. startAsAdult: true — this
        // represents a Tier2 who already existed (and already accumulated a real Tier2 age), not a
        // newborn; without it the promoted Tier1 spawns at AgeSeason 0, which broke M13.8.1's
        // marriage-to-Tier2 path (ResolveMarriage's MarriageMinAgeSeasons check would immediately
        // fail right after promotion). Same reasoning CharacterFactory.Spawn's own doc comment gives
        // for civ-founding/succession-backfill spawns.
        var promoted = CharacterFactory.Spawn(
            location:   c.Location,
            biome:      (BiomeType)world.TileGrid.GetTile(c.Location).BiomeType,
            worldSeed:  world.WorldSeed,
            entitySeq:  (int)(c.Id.Value & 0x7FFFFFFF),
            config:     simCfg,
            birthYear:  world.CurrentYear,
            startAsAdult: true);

        int promotedOrdinal = world.ClaimNameOrdinal(promoted.Identity.Name);
        if (promotedOrdinal > 0)
            promoted.Identity = promoted.Identity with { NameOrdinal = promotedOrdinal };

        world.Entities.Add(promoted);

        // Carry over accumulated Trust/Fear/rivalry/Bond history onto the new EntityId — without
        // this, promotion silently resets every relationship the Tier2 had built up back to a blank
        // edge (see docs/phases/m13_8_tier2_relationship_exposure.md).
        world.Relationships.RekeyEntity(c.Id, promoted.Id);

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

        return promoted;
    }
}
