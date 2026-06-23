using System.Text.Json;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// Resolves character commands that affect civilizations, settlements, and relationships.
/// </summary>
public static class CivTracker
{
    private const int SettlementStartPop    = 50;
    private const int SettlementStartHealth = 100;
    private const int RaidDamageMin         = 10;
    private const int RaidDamageMax         = 30;
    private const int SaltRaidDamage        = 700;

    public static void Resolve(
        ICommand cmd,
        WorldState world,
        List<PendingEvent> pending)
    {
        switch (cmd)
        {
            case EstablishSettlement es:
                ResolveEstablish(es, world, pending); break;
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
        EstablishSettlement cmd, WorldState world, List<PendingEvent> pending)
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
            founder.Identity = founder.Identity with { CivId = civId };

            FireCivFounded(civ, founder, world, pending);
        }
        else
        {
            world.Civilizations[civId].Members.Add(founder.Id);
        }

        var stub = new SettlementStub(
            FounderId:   founder.Id,
            CivId:       civId,
            Tile:        cmd.Tile,
            FoundedYear: world.CurrentYear,
            Population:  SettlementStartPop,
            Health:      SettlementStartHealth);
        world.Settlements[cmd.Tile] = stub;

        // Mark goal as progressed
        foreach (var g in founder.Goals)
            if (g.Type == GoalType.Expansion) g.Progress = Math.Min(1f, g.Progress + 0.5f);

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
        if (rel.IsAlly) return; // already allied

        var updated = rel with
        {
            Trust = Math.Min(1f, rel.Trust + 0.3f),
            Flags = rel.Flags | RelationshipFlags.IsAlly
        };
        world.Relationships.Upsert(updated);

        // Belonging need satisfaction
        c.Needs      = c.Needs with { Belonging = Math.Min(1f, c.Needs.Belonging + 0.15f) };
        target.Needs = target.Needs with { Belonging = Math.Min(1f, target.Needs.Belonging + 0.1f) };
        c.Skills = c.Skills with { Diplomacy = Math.Min(1f, c.Skills.Diplomacy + 0.02f) };

        foreach (var g in c.Goals)
            if (g.Type == GoalType.Alliance && g.TargetEntityId == target.Id)
                g.Progress = 1f;

        var payload = JsonSerializer.Serialize(new
        {
            characterId = c.Id.Value,
            targetId    = target.Id.Value,
            charName    = c.Identity.Name,
            targetName  = target.Identity.Name,
            location    = new[] { c.Location.X, c.Location.Y }
        });
        pending.Add(new PendingEvent(EventType.AllianceFormed, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));
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

        var payload = JsonSerializer.Serialize(new
        {
            characterId = c.Id.Value,
            targetId    = target.Id.Value,
            charName    = c.Identity.Name,
            targetName  = target.Identity.Name
        });
        pending.Add(new PendingEvent(EventType.RivalryFormed, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));
    }

    // ─── War ──────────────────────────────────────────────────────────────────

    private static void ResolveWar(DeclareWar cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        if (rel.IsAtWar) return;

        world.Relationships.Upsert(rel with
        {
            Trust = Math.Min(rel.Trust - 0.3f, -0.3f),
            Flags = rel.Flags | RelationshipFlags.IsAtWar | RelationshipFlags.IsRival
        });

        var payload = JsonSerializer.Serialize(new
        {
            declarerId   = c.Id.Value,
            declarerName = c.Identity.Name,
            targetId     = target.Id.Value,
            targetName   = target.Identity.Name,
            declarerCiv  = c.Identity.CivId.Value,
            targetCiv    = target.Identity.CivId.Value
        });
        pending.Add(new PendingEvent(EventType.WarDeclared, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));
    }

    // ─── Raid ─────────────────────────────────────────────────────────────────

    private static void ResolveRaid(
        RaidSettlement cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character raider) return;
        if (!world.Settlements.TryGetValue(cmd.SettlementTile, out var settlement)) return;

        int damage = RaidDamageMin
            + (int)(world.GetRandomFloat(raider.Id, SaltRaidDamage)
                    * (RaidDamageMax - RaidDamageMin));
        int newHealth = settlement.Health - damage;

        raider.Skills = raider.Skills with
            { Combat = Math.Min(1f, raider.Skills.Combat + 0.02f) };
        raider.Needs = raider.Needs with
            { Status = Math.Min(1f, raider.Needs.Status + 0.1f) };

        var payload = JsonSerializer.Serialize(new
        {
            raiderId   = raider.Id.Value,
            raiderName = raider.Identity.Name,
            tile       = new[] { cmd.SettlementTile.X, cmd.SettlementTile.Y },
            damage,
            settlementHealth = newHealth
        });
        pending.Add(new PendingEvent(EventType.BattleOccurred, cmd.SettlementTile, null, payload,
            new[] { raider.Id.Value }));

        if (newHealth <= 0)
        {
            world.Settlements.Remove(cmd.SettlementTile);
            pending.Add(new PendingEvent(EventType.SettlementDestroyed, cmd.SettlementTile, null,
                JsonSerializer.Serialize(new
                {
                    tile        = new[] { cmd.SettlementTile.X, cmd.SettlementTile.Y },
                    founderId   = settlement.FounderId.Value,
                    destroyerId = raider.Id.Value
                })));
        }
        else
        {
            world.Settlements[cmd.SettlementTile] = settlement with { Health = newHealth };
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

        var payload = JsonSerializer.Serialize(new
        {
            characterId = c.Id.Value,
            targetId    = target.Id.Value,
            trustGain
        });
        pending.Add(new PendingEvent(EventType.Negotiated, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));
    }

    // ─── Event helpers ────────────────────────────────────────────────────────

    private static void FireCivFounded(
        Civilization civ, Tier1Character founder, WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            civId      = civ.Id.Value,
            civName    = civ.Name,
            founderId  = founder.Id.Value,
            founderName = founder.Identity.Name,
            capital    = new[] { civ.CapitalTile.X, civ.CapitalTile.Y },
            year       = world.CurrentYear
        });
        pending.Add(new PendingEvent(EventType.CivilizationFounded, civ.CapitalTile, null, payload));
    }

    private static void FireSettlementFounded(
        SettlementStub stub, Tier1Character founder, WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            founderId  = founder.Id.Value,
            founderName = founder.Identity.Name,
            civId      = stub.CivId.Value,
            tile       = new[] { stub.Tile.X, stub.Tile.Y },
            year       = world.CurrentYear
        });
        pending.Add(new PendingEvent(EventType.SettlementFounded, stub.Tile, null, payload));
    }
}
