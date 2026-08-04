namespace WorldEngine.Sim.Config;

/// <summary>M13 13.2 — Debt as an obligation mechanic: gifted aid in a time of need creates an
/// inheritable, forgivable obligation that measurably biases the debtor's behavior.</summary>
public class DebtConfig
{
    // 2026-08-03: kept at its original value — the real Tier1-Tier1 GrantAid blocker turned out to
    // be that ordinary (non-married) Tier1-Tier1 Trust essentially never reaches any meaningful
    // level at all (calibration observed a ~0.0-0.36 ceiling across 3 seeds), not that this specific
    // threshold was slightly too high. Fixed via a same-civ shortcut in UtilityScorer/CivTracker
    // (mirrors the existing Tier2 "shared homeland" shortcut) instead of chasing the threshold
    // further down — this value still gates the already-trusted-stranger case.
    public float AidTrustThreshold     { get; set; } = 0.4f;  // granter must already trust the recipient this much
    public float AidNeedThreshold      { get; set; } = 0.3f;  // recipient's Food or Safety must be below this to qualify
    public float AidDebtIncrement      { get; set; } = 0.3f;  // |Debt| added toward the granter per GrantAid
    public float AidTrustGain          { get; set; } = 0.15f; // Trust gained by both parties from the exchange
    public float AidNeedRestore        { get; set; } = 0.25f; // recipient's triggering need restored by this much
    public float DebtWarDampenMin      { get; set; } = 0.3f;  // War/Raid score multiplier floor when maximally indebted to someone in the target civ
    public float ForgiveTrustThreshold { get; set; } = 0.6f;  // creditor must trust the debtor this much before forgiving
    public float ForgiveMinDebt        { get; set; } = 0.2f;  // minimum |Debt| owed before forgiveness is considered
    public float ForgiveTrustGain      { get; set; } = 0.2f;  // Trust gained by both parties when debt is forgiven

    // 2026-08-03: a Tier1-Tier1 GrantAid/ForgiveDebt candidate scores identically to the far more
    // common Tier2 "shared homeland" shortcut (same formula, ignores the target), so whichever
    // tier a granter's radius scan reaches first permanently wins that tick's single best-candidate
    // slot — Tier1-Tier1 Debt never won that tie in calibration. This multiplier breaks the tie in
    // the Tier1 pair's favor (a personal bond outweighs routine community charity) without loosening
    // either action's own trust/need gates.
    public float Tier1AidPriorityBonus { get; set; } = 1.2f;

    // 2026-08-03: a separate (more permissive) need threshold for the same-civ Tier1-Tier1 aid
    // shortcut, distinct from AidNeedThreshold above so the already-calibrated Tier2 Debt volume
    // isn't touched. Two same-civ Tier1s (rulers/heroes) being co-located AND one of them hitting a
    // full crisis (< AidNeedThreshold, 0.3) at the same moment proved too rare to ever fire in
    // calibration — a milder "below-average" dip is enough for a fellow citizen's Tier1 to notice.
    public float Tier1AidNeedThreshold { get; set; } = 0.9f;

    // M13 13.5 — Oath-breaking: a debtor who wars/raids their own creditor's civ anyway.
    public float OathBreakTrustPenalty { get; set; } = 0.4f;  // Trust lost on the violated edge when the debt is broken instead of honored
}
