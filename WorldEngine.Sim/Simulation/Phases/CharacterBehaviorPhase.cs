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
    private readonly SettlementConfig   _settleCfg;
    private readonly SimConfig _simCfg;

    public CharacterBehaviorPhase(SimConfig cfg)
    {
        _simCfg    = cfg;
        _cfg       = cfg.Character;
        _settleCfg = cfg.Settlement;
    }

    public List<PendingEvent> Execute(WorldState world, long tick, bool isAnnualTick = false)
    {
        var pending = new List<PendingEvent>();

        // Snapshot to avoid modify-during-iteration
        var characters = world.Entities.Characters.ToList();
        var deathsThisTick = new List<(EntityId Id, string Name)>();

        foreach (var c in characters)
        {
            if (!c.IsAlive) continue;
            UpdateLifecycle(c, world, tick, pending);
            if (!c.IsAlive)
            {
                deathsThisTick.Add((c.Id, c.Identity.Name));
                continue;
            }
            c.TicksInCurrentTile++;
            NeedsUpdater.Update(c, world, _cfg);
            GoalManager.UpdateGoals(c, world, tick, _cfg);
            bool wasSpiraling = c.Wellbeing <= _cfg.SpiralThreshold;
            bool wasFlourishingBefore = c.Wellbeing >= _cfg.FlourishingThreshold;
            bool isSpiraling = GoalManager.UpdateWellbeing(c, world, _cfg, out bool crossedFlourishing);
            if (crossedFlourishing)
                EmitFlourishingEvent(c, pending);
            if (isSpiraling && !wasSpiraling) // only emit on the crossing, not every tick
                EmitSpiralEvent(c, pending);
            ApplyTerritorialPressure(c, world, tick);
            ApplyPassiveDrains(c, world);
            CheckBeastEncounters(c, world, pending, tick);
            var cmd = UtilityScorer.SelectAction(c, world, _cfg);
            if (cmd != null)
                ResolveCommand(cmd, c, world, pending, tick);
        }

        // Grief: after all deaths are resolved, notify mourners
        foreach (var (deadId, deadName) in deathsThisTick)
        {
            var mourners = new List<(EntityId, float)>();
            GoalManager.ApplyGriefToMourners(deadId, deadName, world, _cfg, mourners);
            foreach (var (mournerId, intensity) in mourners)
            {
                if (world.GetEntity(mournerId) is Tier1Character mourner && mourner.IsAlive)
                    EmitGriefEvent(mourner, deadId, deadName, intensity, pending);
            }
        }

        // Remove dead characters from registry
        foreach (var c in characters.Where(ch => !ch.IsAlive))
            world.Entities.Remove(c.Id);

        // Spawn next-generation heroes once per year (Spring tick only) — runs per-tick otherwise
        // iterates all settlements for no additional output and burns O(settlements) per tick.
        if (isAnnualTick)
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

            // Emigration pressure: over-capacity settlements get an extra spawn boost and the
            // new character is seeded with a Colonize goal so they actively seek distant land.
            bool overCapacity = stub.CarryingCapacity > 0
                && stub.Population > _settleCfg.EmigrationThreshold * stub.CarryingCapacity;
            if (overCapacity)
            {
                float pressureFactor = Math.Clamp(
                    ((float)stub.Population / stub.CarryingCapacity - _settleCfg.EmigrationThreshold)
                    / (1f - _settleCfg.EmigrationThreshold), 0f, 1f);
                chance += _settleCfg.EmigrationBonusChance * pressureFactor;
            }

            float r = WorldRng.FloatAt(world.WorldSeed, (int)tick, (int)(stub.FounderId.Value & 0x7FFFFFFF), 0, SaltCivBirth);
            if (r > chance) continue;

            // Unique entitySeq derived from tick + tile to stay deterministic
            long seq  = (50_000L + tick * 997L + kvp.Key.X * 31 + kvp.Key.Y) & 0x7FFFFFFF;
            var tileData = world.TileGrid.GetTile(kvp.Key);
            var born  = CharacterFactory.Spawn(kvp.Key, (BiomeType)tileData.BiomeType, world.WorldSeed, seq, _simCfg, world.CurrentYear);
            born.Identity = born.Identity with { CivId = stub.CivId };
            civ.Members.Add(born.Id);
            world.Entities.Add(born);

            // Emigrant: seed Colonize goal immediately and deduct population from parent
            if (overCapacity)
            {
                born.Goals.Add(new GoalData
                {
                    Type       = GoalType.Colonize,
                    Priority   = 0.9f,
                    StaleSince = (int)tick,
                    FormedTick = (int)tick
                });
                // Re-read stub in case it was updated earlier this tick
                if (world.Settlements.TryGetValue(kvp.Key, out var freshStub))
                    world.Settlements[kvp.Key] = freshStub with
                        { Population = Math.Max(0, freshStub.Population - _settleCfg.EmigrantPopCost) };
            }

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

        // Rivalries end when a participant dies — you can't have a personal feud with a corpse.
        // Wars are civ-level and continue regardless of whether this individual is alive.
        // Alliances and bond edges are left in place: they feed grief/mourning logic
        // (ApplyGriefToMourners runs after this) and are pruned by the annual cleanup.
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
            case CreateArtwork:
                ResolveCreateArtwork(c, world, pending, tick);
                break;
            case FleeRegion flee:
                ResolveMove(c, flee.Destination, world);
                break;
            case EstablishSettlement:
            case AllyWith:
            case DeclareRivalry:
            case DeclareWar:
            case RaidSettlement:
            case Negotiate:
                CivTracker.Resolve(cmd, world, pending, _simCfg.SettlementNames);
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
        // Resting restores physical needs plus identity/spiritual needs —
        // stillness enables reflection, contemplation, and sense of self.
        c.Needs = c.Needs with
        {
            Safety    = Math.Min(1f, c.Needs.Safety    + 0.05f),
            Food      = Math.Min(1f, c.Needs.Food      + 0.05f),
            Shelter   = Math.Min(1f, c.Needs.Shelter   + 0.03f),
            Status    = Math.Min(1f, c.Needs.Status    + 0.01f),
            Purpose   = Math.Min(1f, c.Needs.Purpose   + 0.02f),
            Spiritual = Math.Min(1f, c.Needs.Spiritual + 0.03f),
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
            if (rel.IsAlly || rel.IsRival) continue; // relationship already decided

            // Drain trust by a small amount each tick — enough to reach -0.1 within ~5 years of contact
            world.Relationships.Upsert(rel with
            {
                Trust = Math.Max(-0.5f, rel.Trust - _cfg.TerritorialTrustDrain)
            });
        }
    }

    // ─── Passive trust drains (ancestry cultural distance + personality mismatch) ──

    /// <summary>
    /// Applied each tick for every pair of co-located characters from different civs.
    /// First meeting applies a one-time ancestry modifier; subsequent ticks drain by
    /// cultural distance and personality mismatch.
    /// </summary>
    private void ApplyPassiveDrains(Tier1Character c, WorldState world)
    {
        var registry = _simCfg.AncestryRegistry;

        foreach (var e in world.GetEntitiesAt(c.Location))
        {
            if (e is not Tier1Character other || other.Id == c.Id || !other.IsAlive) continue;
            // Only drain between chars of different civs; same-civ relations handled by territorial pressure
            if (c.Identity.CivId == other.Identity.CivId) continue;

            bool isFirstMeeting = world.Relationships.Get(c.Id, other.Id) == null;
            var rel = world.Relationships.GetOrCreate(c.Id, other.Id);
            if (rel.IsAlly) continue;

            float trust = rel.Trust;

            if (isFirstMeeting)
            {
                // First-meeting modifier: average of both ancestries' view of the other
                float modifierAB = registry.GetFirstMeetingTrust(c.Identity.AncestryId, other.Identity.AncestryId);
                float modifierBA = registry.GetFirstMeetingTrust(other.Identity.AncestryId, c.Identity.AncestryId);
                trust += (modifierAB + modifierBA) * 0.5f;
            }

            // Cultural distance drain — proportional to how different the ancestries are
            float culturalDist = registry.GetCulturalDistance(c.Identity.AncestryId, other.Identity.AncestryId);
            trust -= culturalDist * _cfg.CulturalDistanceDrainRate;

            // Personality mismatch drain — characters with very different Stability punish each other
            float stabilityDiff = Math.Abs(c.Personality.Stability - other.Personality.Stability);
            trust -= stabilityDiff * _cfg.PersonalityMismatchDrainRate;

            world.Relationships.Upsert(rel with { Trust = Math.Clamp(trust, -1f, 1f) });
        }
    }

    // ─── Artwork creation ────────────────────────────────────────────────────

    private static void ResolveCreateArtwork(
        Tier1Character c, WorldState world, List<PendingEvent> pending, long tick)
    {
        // Progress the Create goal and boost Wellbeing
        var createGoal = c.Goals.FirstOrDefault(g => g.Type == GoalType.Create);
        if (createGoal != null)
        {
            createGoal.Progress = Math.Min(1f, createGoal.Progress + 0.2f);
            createGoal.StaleSince = (int)tick;
        }
        c.Wellbeing = Math.Min(1f, c.Wellbeing + 0.05f);

        var payload = JsonSerializer.Serialize(new
        {
            characterId   = c.Id.Value,
            characterName = c.Identity.Name,
            location      = new[] { c.Location.X, c.Location.Y },
            wellbeing     = c.Wellbeing
        });
        pending.Add(new PendingEvent(EventType.ArtworkCreated, c.Location, null, payload,
            new[] { c.Id.Value }));
    }

    // ─── Emotional state events ──────────────────────────────────────────────

    private static void EmitFlourishingEvent(Tier1Character c, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            characterId   = c.Id.Value,
            characterName = c.Identity.Name,
            wellbeing     = c.Wellbeing,
            location      = new[] { c.Location.X, c.Location.Y }
        });
        pending.Add(new PendingEvent(EventType.CharacterFlourishing, c.Location, null, payload,
            new[] { c.Id.Value }));
    }

    private static void EmitSpiralEvent(Tier1Character c, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            characterId   = c.Id.Value,
            characterName = c.Identity.Name,
            wellbeing     = c.Wellbeing,
            location      = new[] { c.Location.X, c.Location.Y }
        });
        pending.Add(new PendingEvent(EventType.CharacterSpiraling, c.Location, null, payload,
            new[] { c.Id.Value }));
    }

    private static void EmitGriefEvent(
        Tier1Character mourner, EntityId deadId, string deadName, float intensity,
        List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            characterId   = mourner.Id.Value,
            characterName = mourner.Identity.Name,
            deceasedId    = deadId.Value,
            deceasedName  = deadName,
            intensity,
            wellbeing     = mourner.Wellbeing,
            hasAvenge     = mourner.Goals.Any(g => g.Type == GoalType.Avenge)
        });
        pending.Add(new PendingEvent(EventType.CharacterGrieved, mourner.Location, null, payload,
            new[] { mourner.Id.Value }));
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
