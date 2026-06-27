using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

public static partial class CivTracker
{
    private const int SaltRaidDamage  = 700;
    private const int SaltWarCampaign = 4200;

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
    internal static void StartWarBetween(
        Civilization declCiv, Civilization targCiv, string cause,
        WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Character;
        if (declCiv.IsAtWarWith(targCiv.Id)) return;
        if (declCiv.InPeaceCooldownWith(targCiv.Id, world.CurrentYear, cfg.PeaceCooldownYears, cfg.WarExhaustionYearsPerWar)) return;

        declCiv.WarsAgainst[targCiv.Id] = world.CurrentYear;
        targCiv.WarsAgainst[declCiv.Id] = world.CurrentYear;

        // Track war count for exhaustion scaling and cultural trait counters
        declCiv.WarHistory[targCiv.Id] = declCiv.WarHistory.GetValueOrDefault(targCiv.Id, 0) + 1;
        targCiv.WarHistory[declCiv.Id] = targCiv.WarHistory.GetValueOrDefault(declCiv.Id, 0) + 1;
        declCiv.TotalWarsInitiated++;  // declarer's total war count (for Militaristic trait)

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
        string[]? declarerTraits = declCiv.CulturalTraits.Count > 0
            ? declCiv.CulturalTraits.ToArray()
            : null;
        var payload = JsonSerializer.Serialize(new WarDeclaredPayload(
            declCiv.RulerId.Value, declRuler?.Identity.Name ?? declCiv.Name,
            declCiv.Id.Value, declCiv.Name,
            targCiv.Id.Value, targCiv.Name,
            cause, causeDescription, warNumber,
            DeclarerTraits: declarerTraits));
        var warEntityIds = new[] { declCiv.RulerId.Value };
        var warSecondaryIds = targRuler != null ? new[] { targCiv.RulerId.Value } : null;
        pending.Add(new PendingEvent(EventType.WarDeclared, eventTile, null, payload,
            warEntityIds, warSecondaryIds,
            ActorId: declCiv.RulerId.Value, ActorName: declRuler?.Identity.Name ?? declCiv.Name,
            CivId: declCiv.Id.Value));
    }

    // ─── Annual war campaigns (M4.2) ─────────────────────────────────────────

    /// <summary>
    /// Fires one abstract campaign battle per active war per year.
    /// Removes the character-proximity bottleneck: declared wars now always generate battles
    /// regardless of where named characters happen to be.
    /// </summary>
    internal static void RunWarCampaigns(WorldState world, List<PendingEvent> pending)
    {
        var wCfg = world.SimConfig.War;
        var processed = new HashSet<(CivId, CivId)>();

        foreach (var civA in world.Civilizations.Values)
        {
            if (civA.IsCollapsed) continue;

            foreach (var (enemyCivId, _) in civA.WarsAgainst.ToList())
            {
                // Only process each pair once
                var key = (Min(civA.Id, enemyCivId), Max(civA.Id, enemyCivId));
                if (!processed.Add(key)) continue;

                if (!world.Civilizations.TryGetValue(enemyCivId, out var civB) || civB.IsCollapsed)
                    continue;

                // Find the nearest enemy settlement to raid via campaign
                SettlementStub? target = null;
                TileCoord targetTile = default;
                float nearestDist = float.MaxValue;
                foreach (var (tile, stub) in world.Settlements)
                {
                    if (stub.CivId != enemyCivId) continue;
                    float dist = 0f;
                    foreach (var (aTile, aStub) in world.Settlements)
                    {
                        if (aStub.CivId != civA.Id) continue;
                        int dx = aTile.X - tile.X, dy = aTile.Y - tile.Y;
                        float d = MathF.Sqrt(dx * dx + dy * dy);
                        if (d < dist || dist == 0f) dist = d;
                    }
                    if (dist < nearestDist) { nearestDist = dist; target = stub; targetTile = tile; }
                }

                if (target == null) continue; // no settlements for civ B — handled by collapse logic

                // Find best attacker from civ A
                Tier1Character? bestAttacker = null;
                float bestCombat = -1f;
                foreach (var memberId in civA.Members)
                {
                    if (world.GetEntity(memberId) is not Tier1Character ch || !ch.IsAlive) continue;
                    if (ch.Skills.Combat > bestCombat)
                    {
                        bestCombat = ch.Skills.Combat;
                        bestAttacker = ch;
                    }
                }

                float attackerStr = bestAttacker?.Skills.Combat ?? wCfg.CampaignBattleBaseStrength;
                float defenderStr = 0.3f + (target.Health / (float)SettlementStartHealth) * 0.5f;

                // Deterministic roll seeded by world seed, year, and the pair
                float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear,
                    civA.Id.Value, enemyCivId.Value, SaltWarCampaign);
                bool attackerWins = roll < attackerStr / (attackerStr + defenderStr);

                long attackerEntityId = bestAttacker?.Id.Value ?? civA.RulerId.Value;
                string attackerName   = bestAttacker?.Identity.Name ?? civA.Name;

                if (attackerWins)
                {
                    int newHealth = Math.Max(0, target.Health - wCfg.CampaignBattleDamage);
                    world.Settlements[targetTile] = target with { Health = newHealth };
                    civA.WarBattleWins[enemyCivId] = civA.WarBattleWins.GetValueOrDefault(enemyCivId, 0) + 1;

                    var bPayload = JsonSerializer.Serialize(new BattlePayload(
                        attackerEntityId, attackerName,
                        wCfg.CampaignBattleDamage, newHealth, "campaign_victory",
                        false, 100));
                    pending.Add(new PendingEvent(EventType.BattleOccurred, targetTile, null, bPayload,
                        new[] { attackerEntityId },
                        CivId: civA.Id.Value, SettlementName: target.Name));

                    // Conquest if health depleted
                    if (newHealth <= 0)
                    {
                        CivId previousCivId = target.CivId;
                        int   conqueredPop  = Math.Max(1, target.Population / 2);
                        world.Settlements[targetTile] = target with
                        {
                            CivId              = civA.Id,
                            Health             = SettlementStartHealth / 2,
                            Population         = conqueredPop,
                            PopulationF        = 0f,
                            ConqueredYear      = world.CurrentYear,
                            ConqueredFromCivId = previousCivId.Value,
                        };

                        if (world.Civilizations.TryGetValue(previousCivId, out var losingCiv))
                        {
                            if (target.IsColony) losingCiv.ColonyCount    = Math.Max(0, losingCiv.ColonyCount    - 1);
                            else                 losingCiv.SettlementCount = Math.Max(0, losingCiv.SettlementCount - 1);
                        }
                        civA.SettlementCount++;

                        TransferTerritory(targetTile, previousCivId, civA.Id, world);

                        pending.Add(new PendingEvent(EventType.SettlementConquered, targetTile, null,
                            JsonSerializer.Serialize(new SettlementConqueredPayload(
                                attackerEntityId, attackerName,
                                civA.Id.Value, previousCivId.Value, conqueredPop)),
                            new[] { attackerEntityId },
                            CivId: civA.Id.Value, SettlementName: target.Name));

                        bool anyLeft = world.Settlements.Values.Any(s => s.CivId == previousCivId);
                        if (!anyLeft)
                            pending.Add(new PendingEvent(EventType.CivilizationCollapsed, targetTile, null,
                                JsonSerializer.Serialize(new CivCollapsedPayload(previousCivId.Value, "conquered")),
                                CivId: previousCivId.Value));
                    }
                }
                else
                {
                    // Defender wins — attacker repulsed
                    civB.WarBattleWins[civA.Id] = civB.WarBattleWins.GetValueOrDefault(civA.Id, 0) + 1;

                    var bPayload = JsonSerializer.Serialize(new BattlePayload(
                        attackerEntityId, attackerName,
                        0, target.Health, "repulsed",
                        false, 100));
                    pending.Add(new PendingEvent(EventType.BattleOccurred, targetTile, null, bPayload,
                        new[] { attackerEntityId },
                        CivId: civA.Id.Value, SettlementName: target.Name));
                }
            }
        }
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
                    ResourceStores     = newStores,
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

                // Transfer territory: reassign all tiles of the conquered city to the winning civ's nearest city
                TransferTerritory(cmd.SettlementTile, previousCivId, raider.Identity.CivId, world);

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
                int timesSettled = RegisterRuin(cmd.SettlementTile, settlement, "destroyed", world, pending);
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
}
