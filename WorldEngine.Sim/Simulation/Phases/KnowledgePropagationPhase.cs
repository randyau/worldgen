using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Annual phase (Spring) that fills Civilization.KnownCivs via three mechanisms:
///   1. Proximity rumor — civs with settlements within knowledge_spread_radius gain contact
///   2. Decay — existing contacts lose confidence each year without a refresh mechanism
///   3. Rumor chaining — one-hop indirect propagation of non-Rumor contacts
///
/// Character-encounter seeding (mechanism 4) is wired in CharacterBehaviorPhase
/// via CivTracker.SeedCivContact at the cross-civ encounter point.
/// </summary>
public static class KnowledgePropagationPhase
{
    private const int SaltProximity  = 4100;
    private const int SaltChain      = 4101;

    /// <summary>
    /// Execute all annual knowledge propagation passes in order:
    /// 1. Proximity rumor spread (mutual, both civs gain contact)
    /// 2. Confidence decay for contacts not refreshed this tick
    /// 3. Rumor chaining (one hop from non-Rumor sources only)
    /// </summary>
    public static void Execute(WorldState world)
    {
        var cfg = world.SimConfig.Emissary;

        // Build settlement-by-civ index (reuses same pattern as RunBorderTension)
        var byCiv = new Dictionary<CivId, List<TileCoord>>();
        foreach (var (coord, stub) in world.Settlements)
        {
            if (!byCiv.TryGetValue(stub.CivId, out var list))
                byCiv[stub.CivId] = list = new();
            list.Add(coord);
        }

        var activeCivs = new List<Civilization>();
        foreach (var civ in world.Civilizations.Values)
            if (!civ.IsCollapsed) activeCivs.Add(civ);

        // Track which contacts were refreshed this year so we can decay the rest
        var refreshed = new HashSet<(CivId From, CivId To)>();

        RunProximityRumors(world, activeCivs, byCiv, cfg, refreshed);
        RunConfidenceDecay(activeCivs, cfg, refreshed, world.CurrentYear);
        RunRumorChaining(world, activeCivs, cfg);
    }

    // ─── 1. Proximity rumor spread ────────────────────────────────────────────

    private static void RunProximityRumors(
        WorldState world,
        List<Civilization> activeCivs,
        Dictionary<CivId, List<TileCoord>> byCiv,
        EmissaryConfig cfg,
        HashSet<(CivId, CivId)> refreshed)
    {
        int r = cfg.KnowledgeSpreadRadius;
        int rSq = r * r;

        for (int i = 0; i < activeCivs.Count; i++)
        for (int j = i + 1; j < activeCivs.Count; j++)
        {
            var civA = activeCivs[i];
            var civB = activeCivs[j];

            if (!byCiv.ContainsKey(civA.Id) || !byCiv.ContainsKey(civB.Id)) continue;

            bool inRange = false;
            foreach (var ca in byCiv[civA.Id])
            {
                foreach (var cb in byCiv[civB.Id])
                {
                    int dx = ca.X - cb.X, dy = ca.Y - cb.Y;
                    if (dx * dx + dy * dy <= rSq) { inRange = true; break; }
                }
                if (inRange) break;
            }

            if (!inRange) continue;

            // Both civs gain or refresh a Rumor contact
            CivTracker.SeedCivContact(civA.Id, civB.Id, CivContactSource.Rumor,
                civB.CapitalTile, cfg.RumorConfidenceGain, world);
            CivTracker.SeedCivContact(civB.Id, civA.Id, CivContactSource.Rumor,
                civA.CapitalTile, cfg.RumorConfidenceGain, world);

            refreshed.Add((civA.Id, civB.Id));
            refreshed.Add((civB.Id, civA.Id));
        }
    }

    // ─── 2. Confidence decay ──────────────────────────────────────────────────

    private static void RunConfidenceDecay(
        List<Civilization> activeCivs,
        EmissaryConfig cfg,
        HashSet<(CivId, CivId)> refreshed,
        int currentYear)
    {
        foreach (var civ in activeCivs)
        {
            var toRemove = new List<CivId>();
            foreach (var (targetId, contact) in civ.KnownCivs)
            {
                // Skip if refreshed by proximity or emissary this year
                if (refreshed.Contains((civ.Id, targetId))) continue;

                float newConfidence = contact.Confidence - cfg.ConfidenceDecayPerYear;
                if (newConfidence <= 0f)
                {
                    toRemove.Add(targetId);
                }
                else
                {
                    civ.KnownCivs[targetId] = contact with { Confidence = newConfidence };
                }
            }
            foreach (var id in toRemove) civ.KnownCivs.Remove(id);
        }
    }

    // ─── 3. Rumor chaining ────────────────────────────────────────────────────

    /// <summary>
    /// One-hop rumor propagation: if Civ A knows Civ B and knows Civ C (via non-Rumor source),
    /// then Civ B may learn of Civ C at reduced confidence.
    ///
    /// The "one hop" rule filters on A's knowledge of C (contactAC): only non-Rumor knowledge
    /// of C can be chained to B. This prevents chains-of-chains from propagating unboundedly.
    /// </summary>
    public static void RunRumorChaining(WorldState world, List<Civilization> activeCivs, EmissaryConfig cfg)
    {
        // Collect new contacts to add after iteration (avoid modify-during-iteration)
        var pending = new List<(CivId toCiv, CivId ofCiv, TileCoord capitalTile, float confidence)>();

        foreach (var civA in activeCivs)
        {
            // Snapshot KnownCivs to iterate safely
            var known = civA.KnownCivs.ToList();

            foreach (var (civBId, contactAB) in known)
            {
                if (!world.Civilizations.TryGetValue(civBId, out var civB)) continue;
                if (civB.IsCollapsed) continue;

                foreach (var (civCId, contactAC) in known)
                {
                    if (civCId == civBId) continue;

                    // DECISION: A's knowledge of C must be non-Rumor to be chainable.
                    // This enforces the one-hop rule from the spec risk notes:
                    // "check contact.BestSource != Rumor before treating A's knowledge of C as eligible to chain."
                    if (contactAC.BestSource == CivContactSource.Rumor) continue;

                    if (!world.Civilizations.TryGetValue(civCId, out var civC)) continue;
                    if (civC.IsCollapsed) continue;
                    if (civB.KnownCivs.ContainsKey(civCId)) continue;

                    // Roll chain probability
                    float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear,
                        civBId.Value * 997 + civCId.Value, civA.Id.Value, SaltChain);
                    if (roll >= cfg.RumorChainProbability) continue;

                    float chainConfidence = contactAC.Confidence * cfg.RumorChainConfidenceFactor;
                    pending.Add((civBId, civCId, civC.CapitalTile, chainConfidence));
                }
            }
        }

        foreach (var (toCiv, ofCiv, capitalTile, confidence) in pending)
        {
            CivTracker.SeedCivContact(toCiv, ofCiv, CivContactSource.Rumor, capitalTile, confidence, world);
        }
    }
}
