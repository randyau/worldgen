using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// Resolves character commands that affect civilizations, settlements, and relationships.
/// </summary>
public static class CivTracker
{
    private const int SettlementStartPop    = 50;
    private const int SettlementStartHealth = 100;
    private const int SaltRaidDamage        = 700;

    public static void Resolve(
        ICommand cmd,
        WorldState world,
        List<PendingEvent> pending,
        SettlementNamesConfig? namesConfig = null)
    {
        switch (cmd)
        {
            case EstablishSettlement es:
                ResolveEstablish(es, world, pending, namesConfig ?? new()); break;
            case AllyWith aw:
                ResolveAlly(aw, world, pending); break;
            case DeclareRivalry dr:
                ResolveRivalry(dr, world, pending); break;
            case DeclareWar dw:
                ResolveWar(dw, world, pending); break;
            case RaidSettlement rs:
                ResolveRaid(rs, world, pending); break;
            case Negotiate ng:
                ResolveNegotiate(ng, world, pending); break;
        }
    }

    // ─── Establish ────────────────────────────────────────────────────────────

    private static void ResolveEstablish(
        EstablishSettlement cmd, WorldState world, List<PendingEvent> pending,
        SettlementNamesConfig namesConfig)
    {
        if (world.Settlements.ContainsKey(cmd.Tile)) return;
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character founder) return;

        // Create settlement
        var civId = founder.Identity.CivId;
        bool newCiv = !civId.IsValid;
        if (newCiv)
        {
            civId = new CivId(world.NextCivId++);
            string civName = $"{founder.Identity.Name}'s Domain";
            var civ = new Civilization(civId, civName, founder.Id, cmd.Tile, world.CurrentYear);
            civ.Members.Add(founder.Id);
            world.Civilizations[civId] = civ;
            founder.Identity = founder.Identity with { CivId = civId, RulerOrdinal = 1 };

            FireCivFounded(civ, founder, world, pending);
        }
        else
        {
            world.Civilizations[civId].Members.Add(founder.Id);
        }

        string settlementName    = GenerateSettlementName(cmd.Tile, world, namesConfig);
        float  fertilityVariance = GenerateFertilityMultiplier(cmd.Tile, world);

        // Classify: colony if no same-civ settlement is within ColonyMinDistance tiles
        int colonyMinDist = world.SimConfig.Character.ColonyMinDistance;
        bool isColony = !newCiv && !world.Settlements.Values
            .Any(s => s.CivId == civId
                   && Math.Sqrt(Math.Pow(s.Tile.X - cmd.Tile.X, 2) + Math.Pow(s.Tile.Y - cmd.Tile.Y, 2)) < colonyMinDist);

        var stub = new SettlementStub(
            FounderId:           founder.Id,
            CivId:               civId,
            Tile:                cmd.Tile,
            FoundedYear:         world.CurrentYear,
            Population:          SettlementStartPop,
            Health:              SettlementStartHealth,
            Name:                settlementName,
            FertilityMultiplier: fertilityVariance,
            IsColony:            isColony);
        world.Settlements[cmd.Tile] = stub;
        world.AddActiveFounder(founder.Id);
        var civRecord = world.Civilizations[civId];
        if (isColony) civRecord.ColonyCount++;
        else          civRecord.SettlementCount++;
        civRecord.LastSettlementFoundedYear = world.CurrentYear;

        // Mark goal as progressed (works for both Expansion and Colonize)
        foreach (var g in founder.Goals)
            if (g.Type == GoalType.Expansion || g.Type == GoalType.Colonize)
                g.Progress = Math.Min(1f, g.Progress + 0.5f);

        FireSettlementFounded(stub, founder, world, pending);

        founder.Needs = founder.Needs with
        {
            Status  = Math.Min(1f, founder.Needs.Status  + 0.2f),
            Purpose = Math.Min(1f, founder.Needs.Purpose + 0.15f)
        };
        founder.Skills = founder.Skills with
        {
            Leadership    = Math.Min(1f, founder.Skills.Leadership    + 0.02f),
            Administration = Math.Min(1f, founder.Skills.Administration + 0.02f)
        };
    }

    // ─── Alliance ─────────────────────────────────────────────────────────────

    private static void ResolveAlly(AllyWith cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        if (rel.IsAlly) return;

        var cfg = world.SimConfig.Character;

        // Cross-civ only — same-civ relationships are just trust edges
        if (c.Identity.CivId.IsValid && target.Identity.CivId.IsValid
            && c.Identity.CivId == target.Identity.CivId) return;

        // Alliance cap
        int allianceMax = cfg.AllianceMaxBase + (int)(c.Personality.Sociability * cfg.AllianceMaxPerSociability);
        if (world.Relationships.CountAlliances(c.Id) >= allianceMax) return;

        // Enemy-of-ally: if target is allied with any of c's rivals, drain that relationship
        foreach (var bEdge in world.Relationships.GetAll(target.Id).Where(e => e.IsAlly).ToList())
        {
            var thirdId = bEdge.From == target.Id ? bEdge.To : bEdge.From;
            var cThird  = world.Relationships.Get(c.Id, thirdId);
            if (cThird?.IsRival ?? false)
            {
                world.Relationships.Upsert(cThird with
                {
                    Trust = Math.Clamp(cThird.Trust - cfg.EnemyOfAllyTrustDrain, -1f, 1f)
                });
            }
        }

        world.Relationships.Upsert(rel with
        {
            Trust = Math.Min(1f, rel.Trust + 0.3f),
            Flags = rel.Flags | RelationshipFlags.IsAlly
        });

        c.Needs      = c.Needs with { Belonging = Math.Min(1f, c.Needs.Belonging + 0.15f) };
        target.Needs = target.Needs with { Belonging = Math.Min(1f, target.Needs.Belonging + 0.1f) };
        c.Skills     = c.Skills with { Diplomacy = Math.Min(1f, c.Skills.Diplomacy + 0.02f) };

        foreach (var g in c.Goals)
            if (g.Type == GoalType.Alliance && g.TargetEntityId == target.Id)
                g.Progress = 1f;

        var payload = JsonSerializer.Serialize(new AllianceFormedPayload(
            c.Id.Value, c.Identity.Name,
            target.Id.Value, target.Identity.Name,
            c.Identity.CivId.Value, target.Identity.CivId.Value));
        pending.Add(new PendingEvent(EventType.AllianceFormed, c.Location, null, payload,
            new[] { c.Id.Value }, new[] { target.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name, CivId: c.Identity.CivId.Value));
    }

    // ─── Rivalry ──────────────────────────────────────────────────────────────

    private static void ResolveRivalry(
        DeclareRivalry cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        if (rel.IsRival) return;

        world.Relationships.Upsert(rel with
        {
            Trust = Math.Min(rel.Trust, -0.1f),
            Fear  = Math.Min(1f, rel.Fear + 0.1f),
            Flags = rel.Flags | RelationshipFlags.IsRival
        });

        var payload = JsonSerializer.Serialize(new RivalryFormedPayload(
            c.Id.Value, c.Identity.Name, target.Id.Value, target.Identity.Name));
        pending.Add(new PendingEvent(EventType.RivalryFormed, c.Location, null, payload,
            new[] { c.Id.Value }, new[] { target.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));
    }

    // ─── War ──────────────────────────────────────────────────────────────────

    private static void ResolveWar(DeclareWar cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (!c.Identity.CivId.IsValid) return;
        var declCiv = world.GetCivilization(c.Identity.CivId);
        var targCiv = world.GetCivilization(cmd.TargetCivId);
        if (declCiv == null || targCiv == null || declCiv.IsCollapsed || targCiv.IsCollapsed) return;
        StartWarBetween(declCiv, targCiv, "character_encounter", world, pending);
    }

    /// <summary>
    /// Records war on both sides, applies trust damage between rulers, and fires WarDeclared.
    /// Called from both character-command resolution and the annual border-tension check, so
    /// the logic lives here rather than being duplicated in ResolveWar.
    /// </summary>
    private static void StartWarBetween(
        Civilization declCiv, Civilization targCiv, string cause,
        WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Character;
        if (declCiv.IsAtWarWith(targCiv.Id)) return;
        if (declCiv.InPeaceCooldownWith(targCiv.Id, world.CurrentYear, cfg.PeaceCooldownYears, cfg.WarExhaustionYearsPerWar)) return;

        declCiv.WarsAgainst[targCiv.Id] = world.CurrentYear;
        targCiv.WarsAgainst[declCiv.Id] = world.CurrentYear;

        // Track war count for exhaustion scaling
        declCiv.WarHistory[targCiv.Id] = declCiv.WarHistory.GetValueOrDefault(targCiv.Id, 0) + 1;
        targCiv.WarHistory[declCiv.Id] = targCiv.WarHistory.GetValueOrDefault(declCiv.Id, 0) + 1;

        // Trust hit between current rulers even if they've never met — reputation travels
        var declRuler = world.GetEntity(declCiv.RulerId) as Tier1Character;
        var targRuler = world.GetEntity(targCiv.RulerId) as Tier1Character;
        if (declRuler != null && targRuler != null)
        {
            var rel = world.Relationships.GetOrCreate(declRuler.Id, targRuler.Id);
            bool wasAllied = rel.IsAlly;
            world.Relationships.Upsert(rel with
            {
                Trust = Math.Min(rel.Trust - 0.3f, -0.3f),
                Flags = (rel.Flags & ~RelationshipFlags.IsAlly) | RelationshipFlags.IsRival,
            });
            if (wasAllied) FireAllianceBroken(declRuler, targRuler, "war_declared", world, pending);
        }

        int warNumber = declCiv.WarHistory.GetValueOrDefault(targCiv.Id, 1);
        string causeDescription = cause switch
        {
            "character_encounter" => "a hostile encounter between their rulers",
            "border_tension"      => $"years of territorial friction ({warNumber} total war{(warNumber > 1 ? "s" : "")} between these civs)",
            _                     => cause
        };
        var eventTile = declRuler?.Location ?? declCiv.CapitalTile;
        var payload = JsonSerializer.Serialize(new WarDeclaredPayload(
            declCiv.RulerId.Value, declRuler?.Identity.Name ?? declCiv.Name,
            declCiv.Id.Value, declCiv.Name,
            targCiv.Id.Value, targCiv.Name,
            cause, causeDescription, warNumber));
        var warEntityIds = new[] { declCiv.RulerId.Value };
        var warSecondaryIds = targRuler != null ? new[] { targCiv.RulerId.Value } : null;
        pending.Add(new PendingEvent(EventType.WarDeclared, eventTile, null, payload,
            warEntityIds, warSecondaryIds,
            ActorId: declCiv.RulerId.Value, ActorName: declRuler?.Identity.Name ?? declCiv.Name,
            CivId: declCiv.Id.Value));
    }

    // ─── Raid ─────────────────────────────────────────────────────────────────

    private static void ResolveRaid(
        RaidSettlement cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character raider) return;
        if (!world.Settlements.TryGetValue(cmd.SettlementTile, out var settlement)) return;

        var raidCfg = world.SimConfig.Character;
        int damage = raidCfg.RaidDamageMin
            + (int)(world.GetRandomFloat(raider.Id, SaltRaidDamage)
                    * (raidCfg.RaidDamageMax - raidCfg.RaidDamageMin));
        int newHealth = settlement.Health - damage;

        // Raids burn granaries and loot vaults — all resource stores take proportional damage.
        float destroyFraction = Math.Min(1f, damage * world.SimConfig.ResourcePressure.StoreRaidDestructionPerDamage);
        IReadOnlyDictionary<string, float>? newStores = null;
        if (settlement.ResourceStores is { Count: > 0 } existingStores)
        {
            var damaged = new Dictionary<string, float>(existingStores, StringComparer.OrdinalIgnoreCase);
            foreach (var key in damaged.Keys.ToList())
                damaged[key] = Math.Max(0f, damaged[key] * (1f - destroyFraction));
            newStores = damaged;
        }

        raider.Skills = raider.Skills with
            { Combat = Math.Min(1f, raider.Skills.Combat + 0.02f) };
        raider.Needs = raider.Needs with
            { Status = Math.Min(1f, raider.Needs.Status + 0.1f) };

        // Named defenders fight back: any living character of the defending civ at this tile
        // wounds the raider and may be wounded in return. Health damage carries to next tick.
        var cfg = world.SimConfig.Character;
        foreach (var e in world.GetEntitiesAt(cmd.SettlementTile))
        {
            if (e is not Tier1Character defender || !defender.IsAlive) continue;
            if (defender.Identity.CivId != settlement.CivId) continue;
            if (defender.Id == raider.Id) continue;

            int counterDamage = Math.Max(1,
                (int)(defender.Skills.Combat * cfg.MaxHealth * cfg.DefenderCounterDamageMultiplier));
            int raidDamageToChar = Math.Max(1,
                (int)(raider.Skills.Combat * cfg.MaxHealth * cfg.RaiderCharDamageMultiplier));

            raider.Health   -= counterDamage;
            defender.Health -= raidDamageToChar;
            defender.Skills  = defender.Skills with
                { Combat = Math.Min(1f, defender.Skills.Combat + 0.01f) };
            break; // one named defender per raid
        }

        bool raiderWounded = raider.Health < raider.MaxHealth / 2;
        string raidOutcome = newHealth <= 0 ? "conquest" : newHealth < raidCfg.WarConquestHealthThreshold ? "critical_damage" : "damaged";
        var payload = JsonSerializer.Serialize(new BattlePayload(
            raider.Id.Value, raider.Identity.Name, damage, newHealth,
            raidOutcome, raiderWounded, (int)(raider.Health * 100f / raider.MaxHealth)));
        bool hasDefender = world.Civilizations.TryGetValue(settlement.CivId, out var defCiv2);
        var primaryIds = new[] { raider.Id.Value };
        long[]? secondaryIds = hasDefender && defCiv2!.RulerId.Value != 0
            ? new[] { defCiv2.RulerId.Value } : null;
        pending.Add(new PendingEvent(EventType.BattleOccurred, cmd.SettlementTile, null, payload,
            primaryIds, secondaryIds,
            ActorId: raider.Id.Value, ActorName: raider.Identity.Name,
            CivId: raider.Identity.CivId.IsValid ? raider.Identity.CivId.Value : 0,
            SettlementName: settlement.Name));

        if (newHealth <= 0)
        {
            bool canConquer = raider.Identity.CivId.IsValid
                           && settlement.CivId.IsValid
                           && raider.Identity.CivId != settlement.CivId;

            if (canConquer)
            {
                // Annexation: settlement survives under the raider's civ.
                // Population and health are reduced; the original founder remains in place (now a subject).
                CivId previousCivId = settlement.CivId;
                int   conqueredPop  = Math.Max(1, settlement.Population / 2);
                world.Settlements[cmd.SettlementTile] = settlement with
                {
                    CivId              = raider.Identity.CivId,
                    Health             = SettlementStartHealth / 2,
                    Population         = conqueredPop,
                    PopulationF        = 0f,
                    ConqueredYear      = world.CurrentYear,
                    ConqueredFromCivId = previousCivId.Value,
                    ResourceStores     = newStores, // granaries looted/burned during conquest
                };

                // Transfer settlement/colony count from losing civ to winning civ
                if (world.Civilizations.TryGetValue(previousCivId, out var losingCiv))
                {
                    if (settlement.IsColony) losingCiv.ColonyCount    = Math.Max(0, losingCiv.ColonyCount    - 1);
                    else                     losingCiv.SettlementCount = Math.Max(0, losingCiv.SettlementCount - 1);
                }
                if (world.Civilizations.TryGetValue(raider.Identity.CivId, out var winningCiv))
                {
                    if (settlement.IsColony) winningCiv.ColonyCount++;
                    else                     winningCiv.SettlementCount++;
                }

                var conquestEntityIds = world.Civilizations.TryGetValue(previousCivId, out var losingCivForLink)
                    ? new[] { raider.Id.Value, losingCivForLink.FounderId.Value }
                    : new[] { raider.Id.Value };
                pending.Add(new PendingEvent(EventType.SettlementConquered, cmd.SettlementTile, null,
                    JsonSerializer.Serialize(new SettlementConqueredPayload(
                        raider.Id.Value, raider.Identity.Name,
                        raider.Identity.CivId.Value, previousCivId.Value, conqueredPop)),
                    conquestEntityIds,
                    ActorId: raider.Id.Value, ActorName: raider.Identity.Name,
                    CivId: raider.Identity.CivId.Value, SettlementName: settlement.Name));

                // If the losing civ has no settlements left, it collapses.
                bool anyLeft = losingCiv != null && losingCiv.SettlementCount > 0;
                if (!anyLeft)
                    pending.Add(new PendingEvent(EventType.CivilizationCollapsed, cmd.SettlementTile, null,
                        JsonSerializer.Serialize(new CivCollapsedPayload(previousCivId.Value, "conquered")),
                        CivId: previousCivId.Value));
            }
            else
            {
                world.Settlements.Remove(cmd.SettlementTile);
                int timesSettled = RegisterRuin(cmd.SettlementTile, settlement, "destroyed", world);
                pending.Add(new PendingEvent(EventType.SettlementDestroyed, cmd.SettlementTile, null,
                    JsonSerializer.Serialize(new SettlementDestroyedPayload(
                        settlement.FounderId.Value, raider.Id.Value, raider.Identity.Name, timesSettled)),
                    new[] { raider.Id.Value }, new[] { settlement.FounderId.Value },
                    ActorId: raider.Id.Value, ActorName: raider.Identity.Name,
                    CivId: settlement.CivId.Value, SettlementName: settlement.Name));
            }
        }
        else
        {
            world.Settlements[cmd.SettlementTile] = settlement with
            {
                Health         = newHealth,
                ResourceStores = newStores
            };
        }
    }

    // ─── Negotiate ────────────────────────────────────────────────────────────

    private static void ResolveNegotiate(
        Negotiate cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        float trustGain = 0.05f + c.Skills.Diplomacy * 0.1f;
        world.Relationships.Upsert(rel with { Trust = Math.Clamp(rel.Trust + trustGain, -1f, 1f) });

        c.Skills = c.Skills with { Diplomacy = Math.Min(1f, c.Skills.Diplomacy + 0.01f) };

        var payload = JsonSerializer.Serialize(new NegotiatedPayload(
            c.Id.Value, c.Identity.Name, target.Id.Value, trustGain));
        pending.Add(new PendingEvent(EventType.Negotiated, c.Location, null, payload,
            new[] { c.Id.Value }, new[] { target.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));
    }

    // ─── Ruin registration ────────────────────────────────────────────────────

    /// <summary>
    /// Records a settlement tile as a ruin. Increments TimesSettled if the tile has been ruined before.
    /// Returns the new TimesSettled count.
    /// </summary>
    public static int RegisterRuin(
        TileCoord tile, SettlementStub stub, string cause, WorldState world)
    {
        int timesSettled = world.Ruins.TryGetValue(tile, out var existing)
            ? existing.TimesSettled + 1
            : 1;

        world.Ruins[tile] = new RuinRecord(
            Tile:           tile,
            SettlementName: stub.Name,
            OriginalCivId:  stub.CivId,
            DestroyedYear:  world.CurrentYear,
            Cause:          cause,
            TimesSettled:   timesSettled);

        world.RemoveActiveFounder(stub.FounderId);

        if (world.Civilizations.TryGetValue(stub.CivId, out var civ))
        {
            if (stub.IsColony) civ.ColonyCount    = Math.Max(0, civ.ColonyCount    - 1);
            else               civ.SettlementCount = Math.Max(0, civ.SettlementCount - 1);
        }

        return timesSettled;
    }

    // ─── Annual diplomacy maintenance ─────────────────────────────────────────

    /// <summary>
    /// Called once per year (Spring).
    /// 1. Dissolves alliances where trust has fallen below the floor.
    /// 2. Expires wars that have lasted beyond MaxWarDurationYears.
    /// 3. Prunes relationship edges where both characters are dead (keeps graph lean).
    /// </summary>
    public static void RunAnnualDiplomacy(WorldState world, List<PendingEvent> pending)
    {
        var cfg       = world.SimConfig.Character;
        var toProcess = world.Relationships.AllEdges.ToList(); // snapshot before mutations

        foreach (var edge in toProcess)
        {
            bool aAlive = world.GetEntity(edge.From) is Tier1Character;
            bool bAlive = world.GetEntity(edge.To)   is Tier1Character;

            // 1. Prune stale edges where both chars are dead
            if (!aAlive && !bAlive)
            {
                world.Relationships.Remove(edge.From, edge.To);
                continue;
            }

            // 2. Alliance dissolution on trust decay
            if (edge.IsAlly && edge.Trust < cfg.AllianceTrustFloor)
            {
                world.Relationships.Upsert(edge with
                {
                    Flags = edge.Flags & ~RelationshipFlags.IsAlly
                });

                if (aAlive && bAlive &&
                    world.GetEntity(edge.From) is Tier1Character a &&
                    world.GetEntity(edge.To)   is Tier1Character b)
                    FireAllianceBroken(a, b, "trust_decay", world, pending);
            }

            // War expiry is handled at civ level below; no character-level war state here.
        }

        // 3. Border tension: accumulate territorial pressure; declare war if threshold crossed
        RunBorderTension(world, pending);

        // 4. Succession crisis: if no living ruler exists AND no succession occurred this year,
        //    flag distant settlements. (Normal succession is handled immediately in KillCharacter.)
        foreach (var civ in world.Civilizations.Values)
        {
            if (civ.IsCollapsed || civ.SuccessionCrisisEndYear != int.MinValue) continue;
            bool rulerAlive = world.GetEntity(civ.RulerId) is Tier1Character rc && rc.IsAlive;
            if (rulerAlive) continue;
            // Ruler is dead with no successor found yet (KillCharacter would have set RulerId if a member existed)
            bool anyLivingMember = civ.Members.Any(m => world.GetEntity(m) is Tier1Character mc && mc.IsAlive);
            if (anyLivingMember) continue; // succession will happen; don't double-fire crisis

            civ.SuccessionCrisisEndYear = world.CurrentYear + cfg.SuccessionCrisisYears;
            pending.Add(new PendingEvent(EventType.SuccessionCrisis, civ.CapitalTile, null,
                JsonSerializer.Serialize(new SuccessionCrisisPayload(
                    civ.Id.Value, civ.Name, civ.SuccessionCrisisEndYear)),
                CivId: civ.Id.Value));
        }

        // 5. Civilisation floor: spawn new founders if active civ count falls below threshold
        RunCivFloorSpawns(world, pending, world.SimConfig);

        // 6. Civ-level war resolution: expiry, surrender, and collapse
        //    Iterate all civs; EndWarBetween handles symmetry so process each pair once.
        var processed = new HashSet<(CivId, CivId)>();
        foreach (var civ in world.Civilizations.Values)
        {
            foreach (var (enemyCivId, yearDeclared) in civ.WarsAgainst.ToList())
            {
                var key = (Min(civ.Id, enemyCivId), Max(civ.Id, enemyCivId));
                if (!processed.Add(key)) continue; // already handled this pair

                string? reason = null;

                // Truce by expiry — but if the defender's capital is critically damaged, the
                // attacker can force a conquest rather than accepting a mere truce.
                if (world.CurrentYear - yearDeclared >= cfg.MaxWarDurationYears)
                {
                    bool conquestForced = false;
                    if (world.Civilizations.TryGetValue(enemyCivId, out var enemyCiv)
                        && world.Settlements.TryGetValue(enemyCiv.CapitalTile, out var capitalStub)
                        && capitalStub.Health <= cfg.WarConquestHealthThreshold
                        && civ.RulerId.Value != 0)
                    {
                        // Siege complete: attacker's raider forcibly annexes the capital
                        var attacker = world.GetEntity(civ.RulerId) as Tier1Character;
                        if (attacker != null)
                        {
                            var siegeCmd = new RaidSettlement(attacker.Id, enemyCiv.CapitalTile);
                            // Set settlement health to 0 to trigger conquest branch in ResolveRaid
                            world.Settlements[enemyCiv.CapitalTile] = capitalStub with { Health = 0 };
                            ResolveRaid(siegeCmd, world, pending);
                            conquestForced = true;
                        }
                    }
                    reason = conquestForced ? null : "truce"; // null = already handled via conquest
                }

                // Surrender: either side's total population collapsed below the threshold
                if (reason == null)
                {
                    int popA = CivTotalPop(civ.Id, world);
                    int popB = CivTotalPop(enemyCivId, world);
                    if (popA < cfg.WarSurrenderPopThreshold || popB < cfg.WarSurrenderPopThreshold)
                        reason = "surrender";
                }

                // Destruction: either civ collapsed entirely
                if (reason == null)
                {
                    bool aGone = civ.IsCollapsed;
                    bool bGone = world.Civilizations.TryGetValue(enemyCivId, out var ec) && ec.IsCollapsed;
                    if (aGone || bGone)
                        reason = "destruction";
                }

                if (reason != null)
                    EndWarBetween(civ.Id, enemyCivId, reason, world, pending);
            }
        }
    }

    // ─── Civilisation floor ───────────────────────────────────────────────────

    private const int SaltCivFloor = 760;

    /// <summary>
    /// If active civs drop below the configured floor, probabilistically spawn new free-agent
    /// founders on unclaimed fertile land. They arrive with an Expansion goal so they will
    /// attempt to settle and start a new civilisation.
    /// </summary>
    private static void RunCivFloorSpawns(WorldState world, List<PendingEvent> pending, SimConfig cfg)
    {
        int activeCivs = 0;
        foreach (var c in world.Civilizations.Values)
            if (!c.IsCollapsed) activeCivs++;

        int deficit = cfg.Character.CivFloorCount - activeCivs;
        if (deficit <= 0) return;

        var charCfg = cfg.Character;
        for (int slot = 0; slot < deficit; slot++)
        {
            float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear, slot, 0, SaltCivFloor);
            if (roll >= charCfg.CivFloorSpawnChance) continue;

            var tile = FindCivFloorSpawnTile(world, cfg);
            if (tile is null) continue;

            long seq     = (200_000L + world.CurrentYear * 997L + slot * 31L) & 0x7FFFFFFF;
            var  biome   = (BiomeType)world.TileGrid.GetTile(tile.Value).BiomeType;
            var  founder = CharacterFactory.Spawn(tile.Value, biome, world.WorldSeed, seq, cfg, world.CurrentYear);
            int  founderOrdinal = world.ClaimNameOrdinal(founder.Identity.Name);
            if (founderOrdinal > 0)
                founder.Identity = founder.Identity with { NameOrdinal = founderOrdinal };

            // Seed an Expansion goal so they actively seek to settle rather than waiting for
            // the normal goal-formation roll.
            founder.Goals.Add(new GoalData
            {
                Type       = GoalType.Expansion,
                Priority   = 1.0f,
                StaleSince = (int)world.CurrentTick,
                FormedTick = (int)world.CurrentTick
            });

            world.Entities.Add(founder);
            pending.Add(new PendingEvent(EventType.CharacterBorn, tile.Value, null,
                JsonSerializer.Serialize(new CharacterBornPayload(
                    founder.Id.Value, founder.Identity.Name, founder.Identity.Epithet,
                    founder.Personality.Ambition, founder.Personality.Aggression, Source: "civ_floor")),
                new[] { founder.Id.Value },
                ActorId: founder.Id.Value, ActorName: founder.Identity.Name));
        }
    }

    private static TileCoord? FindCivFloorSpawnTile(WorldState world, SimConfig cfg)
    {
        int minFertility = cfg.Character.MinFertilityToSettle;
        int minDist      = cfg.Character.CivFloorMinDist;
        int minDistSq    = minDist * minDist;
        int w = world.TileGrid.TileWidth;
        int h = world.TileGrid.TileHeight;

        var candidates = new List<TileCoord>();
        // Sample every 4th tile — this runs rarely (only when civs are few) so a full scan
        // is fine, but sampling keeps it snappy for large worlds.
        for (int y = 1; y < h - 1; y += 4)
        for (int x = 0; x < w; x += 4)
        {
            var coord = new TileCoord(x, y);
            if (!world.IsLand(coord)) continue;
            var tile = world.TileGrid.GetTile(coord);
            if ((BiomeType)tile.BiomeType == BiomeType.HighMountain) continue;
            if (tile.Fertility < minFertility) continue;

            bool tooClose = false;
            foreach (var s in world.Settlements.Keys)
            {
                int dx = coord.X - s.X, dy = coord.Y - s.Y;
                if (dx * dx + dy * dy < minDistSq) { tooClose = true; break; }
            }
            if (tooClose) continue;

            candidates.Add(coord);
        }

        if (candidates.Count == 0) return null;

        int idx = (int)(WorldRng.FloatAt(world.WorldSeed, world.CurrentYear, 0, 1, SaltCivFloor) * candidates.Count);
        return candidates[Math.Clamp(idx, 0, candidates.Count - 1)];
    }

    /// <summary>
    /// Ends a war between two civs, records a peace treaty on both sides, and fires the event.
    /// Safe to call regardless of which side initiated; handles asymmetric state gracefully.
    /// </summary>
    private static void EndWarBetween(
        CivId civA, CivId civB, string reason, WorldState world, List<PendingEvent> pending)
    {
        if (!world.Civilizations.TryGetValue(civA, out var ca)) return;
        if (!world.Civilizations.TryGetValue(civB, out var cb)) return;

        ca.WarsAgainst.Remove(civB);
        cb.WarsAgainst.Remove(civA);

        // Record peace so neither side can re-declare immediately
        ca.PeaceTreaties[civB] = world.CurrentYear;
        cb.PeaceTreaties[civA] = world.CurrentYear;

        // Peace resolves territorial tension — reset so the clock restarts after the cooldown
        ca.BorderTension.Remove(civB);
        cb.BorderTension.Remove(civA);

        int warCount = ca.WarHistory.GetValueOrDefault(civB, 0);
        var payload = JsonSerializer.Serialize(new WarEndedPayload(
            civA.Value, ca.Name, civB.Value, cb.Name, reason, warCount));
        long[]? warEndSecondary = cb.RulerId.Value != ca.RulerId.Value ? new[] { cb.RulerId.Value } : null;
        pending.Add(new PendingEvent(EventType.WarEnded, ca.CapitalTile, null, payload,
            new[] { ca.RulerId.Value }, warEndSecondary,
            CivId: civA.Value));
    }

    private static void FireAllianceBroken(
        Tier1Character a, Tier1Character b, string reason,
        WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new AllianceBrokenPayload(
            a.Id.Value, a.Identity.Name, b.Id.Value, b.Identity.Name, reason));
        pending.Add(new PendingEvent(EventType.AllianceBroken, a.Location, null, payload,
            new[] { a.Id.Value }, new[] { b.Id.Value },
            ActorId: a.Id.Value, ActorName: a.Identity.Name));
    }

    // ─── Border tension ───────────────────────────────────────────────────────

    /// <summary>
    /// Annual civ-level territorial pressure scan. For each pair of non-enemy civs whose
    /// settlements are within WarProximityRadius, tension accrues proportional to how many
    /// settlement pairs are close and how aggressive the declaring civ's ruler is.
    /// Tension decays when civs are no longer proximate. Crossing TensionWarThreshold
    /// triggers war if the ruler's Aggression meets the threshold — no physical contact needed.
    /// </summary>
    private static void RunBorderTension(WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Character;
        int r   = cfg.WarProximityRadius;

        // Index settlements by CivId once — reused for all pair checks
        var byCiv = new Dictionary<CivId, List<TileCoord>>();
        foreach (var (coord, stub) in world.Settlements)
        {
            if (!byCiv.TryGetValue(stub.CivId, out var list))
                byCiv[stub.CivId] = list = new();
            list.Add(coord);
        }

        var activeCivs = world.Civilizations.Values
            .Where(c => !c.IsCollapsed && byCiv.ContainsKey(c.Id))
            .ToList();

        for (int i = 0; i < activeCivs.Count; i++)
        for (int j = i + 1; j < activeCivs.Count; j++)
        {
            var a = activeCivs[i];
            var b = activeCivs[j];

            if (a.IsAtWarWith(b.Id)) continue;
            if (a.InPeaceCooldownWith(b.Id, world.CurrentYear, cfg.PeaceCooldownYears, cfg.WarExhaustionYearsPerWar)) continue;

            // Measure proximity: sum (1 - dist/r) over all close settlement pairs
            float proximity = 0f;
            foreach (var ca in byCiv[a.Id])
            foreach (var cb in byCiv[b.Id])
            {
                int dx = ca.X - cb.X, dy = ca.Y - cb.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= r) proximity += 1f - dist / r;
            }

            if (proximity <= 0f)
            {
                // No proximity this year — decay tension on both sides
                Decay(a.BorderTension, b.Id, cfg.TensionDecayRate);
                Decay(b.BorderTension, a.Id, cfg.TensionDecayRate);
                continue;
            }

            // Accumulate tension scaled by each ruler's Aggression (dead/missing ruler → neutral 0.5)
            float aggrA = (world.GetEntity(a.RulerId) as Tier1Character)?.Personality.Aggression ?? 0.5f;
            float aggrB = (world.GetEntity(b.RulerId) as Tier1Character)?.Personality.Aggression ?? 0.5f;

            a.BorderTension[b.Id] = a.BorderTension.GetValueOrDefault(b.Id, 0f) + proximity * aggrA * cfg.TensionAccrualPerPair;
            b.BorderTension[a.Id] = b.BorderTension.GetValueOrDefault(a.Id, 0f) + proximity * aggrB * cfg.TensionAccrualPerPair;
        }

        // Check threshold and declare war — one declaration per civ per annual tick
        foreach (var civ in activeCivs)
        {
            if (civ.WarsAgainst.Count >= cfg.MaxActiveWars) continue;
            float rulerAggr = (world.GetEntity(civ.RulerId) as Tier1Character)?.Personality.Aggression ?? 0f;
            if (rulerAggr < cfg.WarAggressionThreshold) continue;

            foreach (var (enemyCivId, tension) in civ.BorderTension.ToList())
            {
                if (tension < cfg.TensionWarThreshold) continue;
                if (civ.IsAtWarWith(enemyCivId)) continue;
                if (civ.InPeaceCooldownWith(enemyCivId, world.CurrentYear, cfg.PeaceCooldownYears, cfg.WarExhaustionYearsPerWar)) continue;
                if (!world.Civilizations.TryGetValue(enemyCivId, out var enemy) || enemy.IsCollapsed) continue;

                StartWarBetween(civ, enemy, "border_tension", world, pending);
                // StartWarBetween is guarded internally, but clear tension regardless so the
                // pair resets whether or not the declaration succeeded
                civ.BorderTension.Remove(enemyCivId);
                enemy.BorderTension.Remove(civ.Id);
                break; // one war per civ per annual tick; re-evaluate next year if still hostile
            }
        }
    }

    private static void Decay(Dictionary<CivId, float> tension, CivId key, float rate)
    {
        if (!tension.TryGetValue(key, out float t)) return;
        t *= (1f - rate);
        if (t < 0.01f) tension.Remove(key);
        else tension[key] = t;
    }

    // ─── War helpers ──────────────────────────────────────────────────────────

    private static int CivTotalPop(CivId civId, WorldState world)
    {
        int total = 0;
        foreach (var s in world.Settlements.Values)
            if (s.CivId == civId) total += s.Population;
        return total;
    }

    // Canonical ordering for de-duplicating civ pairs in the annual war loop
    private static CivId Min(CivId a, CivId b) => a.Value < b.Value ? a : b;
    private static CivId Max(CivId a, CivId b) => a.Value > b.Value ? a : b;

    // ─── Event helpers ────────────────────────────────────────────────────────

    private static void FireCivFounded(
        Civilization civ, Tier1Character founder, WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new CivFoundedPayload(
            civ.Id.Value, civ.Name, founder.Id.Value, founder.Identity.Name));
        pending.Add(new PendingEvent(EventType.CivilizationFounded, civ.CapitalTile, null, payload,
            new[] { founder.Id.Value },
            ActorId: founder.Id.Value, ActorName: founder.Identity.Name, CivId: civ.Id.Value));
    }

    private static void FireSettlementFounded(
        SettlementStub stub, Tier1Character founder, WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new SettlementFoundedPayload(
            founder.Id.Value, founder.Identity.Name,
            stub.CivId.Value, world.Civilizations.TryGetValue(stub.CivId, out var c) ? c.Name : "",
            50)); // SettlementStartPop
        pending.Add(new PendingEvent(EventType.SettlementFounded, stub.Tile, null, payload,
            new[] { founder.Id.Value },
            ActorId: founder.Id.Value, ActorName: founder.Identity.Name,
            CivId: stub.CivId.Value, SettlementName: stub.Name));
    }

    // ─── Name generation ─────────────────────────────────────────────────────

    private const int SaltSettlementPrefix   = 5001;
    private const int SaltSettlementSuffix   = 5002;
    private const int SaltFertilityVariance  = 5003;

    private static string GenerateSettlementName(
        TileCoord tile, WorldState world, SettlementNamesConfig cfg)
    {
        if (cfg.Prefixes.Length == 0 || cfg.Suffixes.Length == 0)
            return $"Settlement ({tile.X},{tile.Y})";

        // Deterministic from world seed + tile position — same tile always gets same name
        float pf = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltSettlementPrefix);
        float sf = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltSettlementSuffix);
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;

        // Bias prefix selection toward biome character
        int pi = BiasedIndex(pf, biome, cfg.Prefixes.Length);
        int si = (int)(sf * cfg.Suffixes.Length);
        return cfg.Prefixes[pi] + cfg.Suffixes[si];
    }

    // Deterministic founding-time fertility variance: maps [0,1] → [1-variance, 1+variance]
    // so each settlement has a permanent slight edge or disadvantage baked in at birth.
    private static float GenerateFertilityMultiplier(TileCoord tile, WorldState world)
    {
        float r = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltFertilityVariance);
        // r ∈ [0,1] → multiplier ∈ [0.85, 1.15] (variance of ±0.15 baked into SettlementConfig)
        // DECISION: variance range is hardcoded here; SettlementConfig.FertilityVariance is the
        // intended range, but injecting SimConfig into CivTracker adds coupling we avoid for now.
        const float variance = 0.15f;
        return 1f - variance + r * (variance * 2f);
    }

    // Slightly bias prefix selection so rocky biomes lean toward hard-sounding names,
    // warm biomes toward bright/green — purely cosmetic, not guaranteed.
    private static int BiasedIndex(float raw, BiomeType biome, int count)
    {
        // Map raw [0,1] through a small biome-dependent shift, then wrap
        float shift = biome switch
        {
            BiomeType.Mountain or BiomeType.Hills or BiomeType.Volcanic
                => 0.3f,   // push toward Iron/Stone/Crag/Flint end
            BiomeType.Grassland or BiomeType.Savanna or BiomeType.TemperateForest
                => -0.15f, // push toward Green/Gold/Fair end
            BiomeType.Tundra or BiomeType.BorealForest
                => 0.15f,  // push toward Cold/Frost/Dark end
            _ => 0f
        };
        return (int)(((raw + shift + 1f) % 1f) * count);
    }
}
