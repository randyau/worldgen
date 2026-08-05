using WorldEngine.Sim.Config;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// M14 14.0 — annual economy sweep (decisions 8 and 10). Runs on the same isAnnualTick cadence
/// CharacterBehaviorPhase.CheckMarriageEstrangement already uses — no new tick-cadence concept.
/// Computes TotalMoneySupply/MoneySupplyPerCapita and EMA-updates WorldState.GlobalPriceIndex
/// toward the clamped target, then applies EconomyConfig.PersonalWealthSpoilageRate as the "cost of
/// living" sink on every living character's Wealth and on standing WealthDrop pools (the sink that
/// makes the price-index clamp meaningful — see decision 10). No new source is introduced: every
/// term summed here is money that already exists in some other form (mining production is the only
/// source, spoilage/raid destruction the only sinks).
/// See docs/phases/m14_economy_independent_wealth.md.
/// </summary>
public static class EconomyPhase
{
    public static void RunAnnual(WorldState world, SimConfig simConfig)
    {
        var cfg = simConfig.Economy;

        // ─── 1. Personal Wealth spoilage (decision 10) — apply before summing so the index
        // reflects the same post-spoilage state that persists into next year. ──────────────────
        foreach (var c in world.Entities.Characters)
        {
            if (!c.IsAlive || c.Wealth <= 0f) continue;
            c.AddWealth(-c.Wealth * cfg.PersonalWealthSpoilageRate);
        }
        foreach (var t2 in world.Entities.Tier2Chars)
        {
            if (!t2.IsAlive || t2.Wealth <= 0f) continue;
            t2.AddWealth(-t2.Wealth * cfg.PersonalWealthSpoilageRate);
        }

        // WealthDrop pools spoil at the same rate (decision 5's revision) and are pruned once
        // negligible so the list doesn't grow forever with dust-sized remnants.
        for (int i = world.WealthDrops.Count - 1; i >= 0; i--)
        {
            var drop = world.WealthDrops[i];
            float remaining = drop.Amount * (1f - cfg.PersonalWealthSpoilageRate);
            if (remaining < 0.01f) { world.WealthDrops.RemoveAt(i); continue; }
            world.WealthDrops[i] = drop with { Amount = remaining };
        }

        // ─── 2. TotalMoneySupply (decision 8's full formula, including decision 10's fixes) ────
        float totalMoneySupply = 0f;
        int totalPopulation = 0;

        foreach (var c in world.Entities.Characters)
        {
            if (!c.IsAlive) continue;
            totalMoneySupply += c.Wealth;
            totalPopulation++;
        }
        foreach (var t2 in world.Entities.Tier2Chars)
        {
            if (!t2.IsAlive) continue;
            totalMoneySupply += t2.Wealth;
        }
        foreach (var org in world.Organizations.Values)
            totalMoneySupply += org.Treasury;
        foreach (var drop in world.WealthDrops)
            totalMoneySupply += drop.Amount;
        foreach (var stub in world.Settlements.Values)
        {
            totalMoneySupply += stub.GetStore("gold")   * cfg.GetBaseValue("gold");
            totalMoneySupply += stub.GetStore("silver") * cfg.GetBaseValue("silver");
            totalMoneySupply += stub.GetStore("gems")   * cfg.GetBaseValue("gems");
            totalPopulation  += stub.Population;
        }

        float moneySupplyPerCapita = totalMoneySupply / Math.Max(1, totalPopulation);

        // ─── 3. EMA-update GlobalPriceIndex toward the clamped target ──────────────────────────
        float target = Math.Clamp(moneySupplyPerCapita / Math.Max(0.0001f, cfg.ReferenceMoneySupplyPerCapita),
            cfg.PriceIndexMin, cfg.PriceIndexMax);
        world.GlobalPriceIndex = Math.Clamp(
            world.GlobalPriceIndex + cfg.PriceIndexEmaAlpha * (target - world.GlobalPriceIndex),
            cfg.PriceIndexMin, cfg.PriceIndexMax);
    }
}
