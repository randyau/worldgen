using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Economy;
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

        // M14 14.2 — per-tick trade-route severance/reopen check + caravan arrival resolution.
        // Runs once per tick (not per character): a route's state can change with no character
        // acting this tick (war breaking out, a cooldown elapsing, a caravan's ETA arriving).
        RunTradeRoutes(world, tick, pending);

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

            // M14 14.3 (reachability fix) — a dead Tier2 merchant's earned Wealth (14.1/14.2's only
            // Wealth source) previously simply vanished: decision 5's death disposition (WealthDrop
            // + inheritance) was scoped to Tier1Character.TransferWealthOnDeath only, and
            // LivelihoodData.EmployerId (the only Tier1 reference on a Tier2) is never actually
            // assigned anywhere in the codebase (grepped — no assignment site exists), so there is
            // no reliable link to hand it to directly. Left as-is, this made 14.3's purchase gate
            // structurally unreachable: an instrument-first run (5 seeds, 300 years) confirmed zero
            // living Tier1Character ever held nonzero Wealth, so "buyer's Wealth >= price" could
            // never pass for any buyer, regardless of price/willingness tuning. Fix: extend the
            // *existing* WealthDrop mechanism (decision 5) to Tier2 death too — dropped at the
            // merchant's home SettlementTile (where a civ's Tier1 ruler/founder is most likely to
            // pass through) rather than their possibly-mid-caravan current Location, then claimed
            // by ClaimWealthDrops exactly like a Tier1's own drop. Same opportunistic co-location
            // mechanic already established for Tier1 deaths, not a new mechanism.
            if (dead.Wealth > 0f)
            {
                var dropTile = dead.Livelihood.SettlementTile != default ? dead.Livelihood.SettlementTile : dead.Location;
                world.WealthDrops.Add(new WealthDrop(dropTile, dead.Wealth, (int)tick));
                dead.AddWealth(-dead.Wealth);
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

        // Transfer resources (always, silent) — or, once a persistent TradeRoute exists for this
        // pair, commit the goods to the route's caravan instead of resolving this same tick.
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
                var routeKey = TradeRouteKey.Of(homeTile, bestDest.Value);

                if (world.TradeRoutes.TryGetValue(routeKey, out var route) && route.Status == TradeRouteStatus.Active)
                {
                    // M14 14.2 — an active persistent route replaces the one-shot instant path
                    // below: goods commit to the route's caravan slot and travel real ticks
                    // instead of resolving this tick (see DispatchCaravanOnRoute and
                    // RunTradeRoutes's per-tick arrival check). Scope decision: at most one
                    // caravan in flight per route at a time — if the slot is occupied, this
                    // merchant simply makes no trade this tick rather than queuing a second one.
                    if (route.InTransit is not null) return;
                    DispatchCaravanOnRoute(c, world, home, homeTile, bestDest.Value, bestResource, transfer, tick, route);
                }
                else
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

                    // M14 14.1 — price and pay for the goods just physically transferred above. Built
                    // as a plain-data ICommand (CLAUDE.md pattern #1/#5) and resolved immediately by
                    // this phase (see CompleteMerchantTrade's doc comment for why this doesn't route
                    // through CivTracker.Resolve/CharacterBehaviorPhase.ResolveCommand).
                    var tradeCmd = new CompleteMerchantTrade(c.Id, homeTile, bestDest.Value, bestResource, transfer);
                    ResolveMerchantTrade(tradeCmd, world, _simCfg.Economy, pending);

                    // M14 14.2 — this one-shot trade is also the route-formation trigger: no
                    // TradeRoute exists yet for this pair (checked above), so count it toward
                    // graduation instead.
                    MaybeFormTradeRoute(c, world, routeKey, pending);
                }
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

    // ─── M14 14.2 — persistent trade routes / caravan transit ──────────────────────────────────

    /// <summary>
    /// Commits <paramref name="quantity"/> units of <paramref name="resource"/> to
    /// <paramref name="route"/>'s caravan slot: the home settlement's ResourceStores are debited
    /// now (goods physically leave), and delivery + priced payment resolve later, at
    /// <see cref="Caravan.ArrivalTick"/>, in <see cref="ResolveCaravanArrival"/> — mirrors the
    /// existing instant path's "debit home now" step but defers the destination credit and
    /// CompleteMerchantTrade payment until arrival.
    /// </summary>
    private void DispatchCaravanOnRoute(
        Tier2Character c, WorldState world, SettlementStub home, TileCoord homeTile, TileCoord destTile,
        string resource, float quantity, long tick, TradeRoute route)
    {
        var newHomeStores = home.ResourceStores is null
            ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, float>(home.ResourceStores, StringComparer.OrdinalIgnoreCase);
        float available = home.GetStore(resource);
        newHomeStores[resource] = Math.Max(0f, available - quantity);
        world.Settlements[homeTile] = home with { ResourceStores = newHomeStores };

        long duration = ComputeCaravanDuration(homeTile, destTile, _simCfg);
        route.InTransit = new Caravan(c.Id, homeTile, destTile, resource, quantity, tick, tick + duration);
    }

    /// <summary>
    /// Distance-derived transit duration (decision: straight-line Euclidean distance over
    /// EconomyConfig.CaravanSpeedTilesPerYear, converted to ticks via SimLoopConfig.TicksPerYear —
    /// the same distance/speed→duration shape CivTracker.Diplomacy uses for emissary ArrivalYear,
    /// the closest existing travel-time precedent). Always at least 1 tick.
    /// </summary>
    private static long ComputeCaravanDuration(TileCoord a, TileCoord b, SimConfig simCfg)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float speedTilesPerTick = simCfg.Economy.CaravanSpeedTilesPerYear / Math.Max(1, simCfg.SimLoop.TicksPerYear);
        return Math.Max(1L, (long)MathF.Ceiling(dist / Math.Max(0.0001f, speedTilesPerTick)));
    }

    /// <summary>
    /// Route-formation trigger (decision: RunMerchant's existing one-shot scan graduates a
    /// settlement pair into a persistent TradeRoute after EconomyConfig.TradeRouteFormationThreshold
    /// successful trades). Resolved as a FormTradeRoute command immediately by this phase, same
    /// exemption as CompleteMerchantTrade — see that command's doc comment.
    /// </summary>
    private static void MaybeFormTradeRoute(Tier2Character c, WorldState world, TradeRouteKey key, List<PendingEvent> pending)
    {
        int count = world.TradeRouteFormationProgress.GetValueOrDefault(key, 0) + 1;
        world.TradeRouteFormationProgress[key] = count;
        if (count < world.SimConfig.Economy.TradeRouteFormationThreshold) return;

        world.TradeRouteFormationProgress.Remove(key);
        var cmd = new FormTradeRoute(c.Id, key.TileA, key.TileB);
        ResolveFormTradeRoute(cmd, world, pending);
    }

    private static void ResolveFormTradeRoute(FormTradeRoute cmd, WorldState world, List<PendingEvent> pending)
    {
        var key = TradeRouteKey.Of(cmd.TileA, cmd.TileB);
        if (world.TradeRoutes.ContainsKey(key)) return; // already exists — nothing to do

        world.TradeRoutes[key] = new TradeRoute(key, world.CurrentYear);

        var payload = JsonSerializer.Serialize(new TradeRouteFormedPayload(
            key.TileA.X, key.TileA.Y, key.TileB.X, key.TileB.Y, Reopened: false));
        pending.Add(new PendingEvent(EventType.TradeRouteFormed, key.TileA, null, payload,
            new[] { cmd.MerchantId.Value }, ActorId: cmd.MerchantId.Value));
    }

    /// <summary>
    /// Per-tick sweep over every persistent TradeRoute (called once per Tier2BehaviorPhase.Execute,
    /// not per character): checks war/lost-endpoint severance and reopen-cooldown expiry, and
    /// resolves any in-transit caravan whose ArrivalTick has been reached.
    /// </summary>
    private void RunTradeRoutes(WorldState world, long tick, List<PendingEvent> pending)
    {
        var cfg = _simCfg.Economy;
        foreach (var route in world.TradeRoutes.Values)
        {
            bool endpointsGone = !world.Settlements.ContainsKey(route.TileA) || !world.Settlements.ContainsKey(route.TileB);
            bool atWar = !endpointsGone && AreEndpointCivsAtWar(world, route.TileA, route.TileB);

            if (route.Status == TradeRouteStatus.Active && (endpointsGone || atWar))
            {
                SeverRoute(route, tick, endpointsGone ? "settlement-lost" : "war", pending);
            }
            else if (route.Status == TradeRouteStatus.Severed && !endpointsGone && !atWar
                     && route.SeveredSinceTick >= 0
                     && tick - route.SeveredSinceTick >= cfg.TradeRouteReopenCooldownTicks)
            {
                ReopenRoute(route, pending);
            }

            if (!endpointsGone && route.InTransit is { } caravan && caravan.ArrivalTick <= tick)
            {
                ResolveCaravanArrival(world, route, caravan, tick, pending);
                route.InTransit = null;
            }
        }
    }

    private void ResolveCaravanArrival(WorldState world, TradeRoute route, Caravan caravan, long tick, List<PendingEvent> pending)
    {
        var cfg = _simCfg.Economy;
        bool atWar = AreEndpointCivsAtWar(world, route.TileA, route.TileB);

        // M14 14.2 — each roll is checked once at arrival (see EconomyConfig.CaravanInterceptionChance's
        // doc comment for why this isn't stacked per-tick during transit). Interception only applies
        // under war; disaster and piracy are ambient risks independent of war state. All three share
        // one consequence path (CaravanRaided, Cause distinguishes them) and one severance counter —
        // a deliberate scope-narrowing: the phase doc allows folding piracy into interception when a
        // separate naval/bandit infrastructure doesn't exist; here all three collapse to the same
        // "the caravan didn't arrive" outcome rather than three independently-tracked mechanics.
        string? lossCause = null;
        if (atWar && world.GetRandomFloat(caravan.MerchantId, SimRngSalts.CaravanInterception) < cfg.CaravanInterceptionChance)
            lossCause = "war";
        else if (world.GetRandomFloat(caravan.MerchantId, SimRngSalts.CaravanDisaster) < cfg.CaravanDisasterChance)
            lossCause = "disaster";
        else if (world.GetRandomFloat(caravan.MerchantId, SimRngSalts.CaravanPiracy) < cfg.CaravanPiracyChance)
            lossCause = "piracy";

        if (lossCause is not null)
        {
            route.ConsecutiveCaravanLosses++;
            var lostPayload = JsonSerializer.Serialize(new CaravanRaidedPayload(
                caravan.MerchantId.Value, caravan.Resource, caravan.Quantity, lossCause,
                caravan.HomeTile.X, caravan.HomeTile.Y, caravan.DestTile.X, caravan.DestTile.Y));
            pending.Add(new PendingEvent(EventType.CaravanRaided, caravan.DestTile, null, lostPayload,
                new[] { caravan.MerchantId.Value }, ActorId: caravan.MerchantId.Value));

            if (route.Status == TradeRouteStatus.Active && route.ConsecutiveCaravanLosses >= cfg.TradeRouteSeverThreshold)
                SeverRoute(route, tick, "losses", pending);
            return;
        }

        route.ConsecutiveCaravanLosses = 0;

        // Physical delivery first (matches the instant path's ordering: transfer the good, then
        // price/pay for it), then the same CompleteMerchantTrade pricing/home-cut/Wealth-credit
        // mechanics 14.1 already built.
        if (world.Settlements.TryGetValue(caravan.DestTile, out var destStub))
        {
            var newDestStores = destStub.ResourceStores is null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(destStub.ResourceStores, StringComparer.OrdinalIgnoreCase);
            newDestStores[caravan.Resource] = destStub.GetStore(caravan.Resource) + caravan.Quantity;
            world.Settlements[caravan.DestTile] = destStub with { ResourceStores = newDestStores };
        }

        var tradeCmd = new CompleteMerchantTrade(caravan.MerchantId, caravan.HomeTile, caravan.DestTile, caravan.Resource, caravan.Quantity);
        ResolveMerchantTrade(tradeCmd, world, cfg, pending);
    }

    private static void SeverRoute(TradeRoute route, long tick, string cause, List<PendingEvent> pending)
    {
        route.Status = TradeRouteStatus.Severed;
        route.SeveredSinceTick = tick;
        var payload = JsonSerializer.Serialize(new TradeRouteSeveredPayload(
            route.TileA.X, route.TileA.Y, route.TileB.X, route.TileB.Y, cause));
        pending.Add(new PendingEvent(EventType.TradeRouteSevered, route.TileA, null, payload));
    }

    private static void ReopenRoute(TradeRoute route, List<PendingEvent> pending)
    {
        route.Status = TradeRouteStatus.Active;
        route.ConsecutiveCaravanLosses = 0;
        route.SeveredSinceTick = -1;
        var payload = JsonSerializer.Serialize(new TradeRouteFormedPayload(
            route.TileA.X, route.TileA.Y, route.TileB.X, route.TileB.Y, Reopened: true));
        pending.Add(new PendingEvent(EventType.TradeRouteFormed, route.TileA, null, payload));
    }

    private static bool AreEndpointCivsAtWar(WorldState world, TileCoord tileA, TileCoord tileB)
    {
        if (!world.Settlements.TryGetValue(tileA, out var a) || !world.Settlements.TryGetValue(tileB, out var b))
            return false;
        if (a.CivId == b.CivId) return false;
        if (!world.Civilizations.TryGetValue(a.CivId, out var civA)) return false;
        return civA.IsAtWarWith(b.CivId);
    }

    /// <summary>
    /// Resolves a CompleteMerchantTrade command: prices the traded good at the destination
    /// (PricingService.EffectivePrice, decisions 7/8), has the destination pay in precious-
    /// commodity value out of its own ResourceStores (floored at zero — a settlement genuinely
    /// short on gold/silver/gems simply pays less, or nothing, a natural no-new-code scarcity
    /// gate rather than a hard precondition on the trade itself), routes
    /// EconomyConfig.MerchantHomeCutFraction of the paid value back to the merchant's home
    /// settlement in the same commodities just debited (Opus-review addition, keeps precious-
    /// commodity totals exactly conserved), and credits the remainder to the merchant's personal
    /// Wealth (decision 9: unconditionally personal until 14.4's Guild-member routing exists).
    /// </summary>
    private static void ResolveMerchantTrade(
        CompleteMerchantTrade cmd, WorldState world, EconomyConfig cfg, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.MerchantId) is not Tier2Character merchant || !merchant.IsAlive) return;
        if (!world.Settlements.TryGetValue(cmd.DestTile, out var dest)) return;
        if (!world.Settlements.TryGetValue(cmd.HomeTile, out var home)) return;
        if (cmd.Quantity <= 0f) return;

        float unitPrice  = PricingService.EffectivePrice(dest, cmd.Resource, cfg, world.GlobalPriceIndex);
        float totalValue = unitPrice * cmd.Quantity;
        if (totalValue <= 0f) return;

        var destStores = dest.ResourceStores is null
            ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, float>(dest.ResourceStores, StringComparer.OrdinalIgnoreCase);

        // Destination pays what it can, commodity by commodity, never going negative — a
        // destination with no precious-commodity reserves simply can't pay (natural scarcity
        // gate; the physical-goods transfer already happened above and is unaffected).
        // DECISION (14.3 instrument-first fix): exclude cmd.Resource itself from the payable set —
        // otherwise, once MoneyEquivalentCommodities was broadened beyond gold/silver/gems to
        // include commonly-mined iron/copper (see that config field's doc comment for why), the
        // destination could immediately "pay" using the very units of cmd.Resource this same trade
        // just physically delivered to it a few lines above, self-referentially undercutting the
        // goods transfer the merchant was supposedly delivering. Caught by two existing 14.1/M9
        // regression tests (MerchantTradeWealthTests/EconomicDepthTests) once the broadened list
        // made a traded commodity double as its own payment currency.
        float remaining = totalValue;
        var debited = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var commodity in cfg.MoneyEquivalentCommodities)
        {
            if (remaining <= 0f) break;
            if (string.Equals(commodity, cmd.Resource, StringComparison.OrdinalIgnoreCase)) continue;
            float available = destStores.TryGetValue(commodity, out float a) ? a : 0f;
            if (available <= 0f) continue;
            float unitValue = cfg.GetBaseValue(commodity);
            if (unitValue <= 0f) continue;

            float availableValue = available * unitValue;
            float takeValue = Math.Min(availableValue, remaining);
            float takeUnits = takeValue / unitValue;

            destStores[commodity] = Math.Max(0f, available - takeUnits);
            debited[commodity] = takeUnits;
            remaining -= takeValue;
        }

        float paidValue = totalValue - remaining;
        if (paidValue <= 0f) return; // couldn't pay anything — trade completes with no Wealth transfer

        world.Settlements[cmd.DestTile] = dest with { ResourceStores = destStores };

        // Home-settlement recirculation cut (Opus-review addition): return a fraction of the
        // paid value to the merchant's home settlement, in the exact same commodities just
        // debited from the destination — keeps every commodity's total exactly conserved.
        if (cfg.MerchantHomeCutFraction > 0f && debited.Count > 0)
        {
            var homeStores = home.ResourceStores is null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(home.ResourceStores, StringComparer.OrdinalIgnoreCase);
            foreach (var (commodity, units) in debited)
            {
                float homeCutUnits = units * cfg.MerchantHomeCutFraction;
                homeStores[commodity] = (homeStores.TryGetValue(commodity, out float h) ? h : 0f) + homeCutUnits;
            }
            world.Settlements[cmd.HomeTile] = home with { ResourceStores = homeStores };
        }

        float merchantShare = paidValue * (1f - cfg.MerchantHomeCutFraction);
        merchant.AddWealth(merchantShare);

        var payload = JsonSerializer.Serialize(new TradePaidPayload(
            merchant.Id.Value, merchant.Name, cmd.Resource, cmd.Quantity,
            paidValue, merchantShare, cmd.DestTile.X, cmd.DestTile.Y));
        pending.Add(new PendingEvent(EventType.TradePaid, cmd.HomeTile, null, payload,
            new[] { merchant.Id.Value }, ActorId: merchant.Id.Value, ActorName: merchant.Name));
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
