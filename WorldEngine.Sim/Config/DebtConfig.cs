namespace WorldEngine.Sim.Config;

/// <summary>M13 13.2 — Debt as an obligation mechanic: gifted aid in a time of need creates an
/// inheritable, forgivable obligation that measurably biases the debtor's behavior.</summary>
public class DebtConfig
{
    public float AidTrustThreshold     { get; set; } = 0.4f;  // granter must already trust the recipient this much
    public float AidNeedThreshold      { get; set; } = 0.3f;  // recipient's Food or Safety must be below this to qualify
    public float AidDebtIncrement      { get; set; } = 0.3f;  // |Debt| added toward the granter per GrantAid
    public float AidTrustGain          { get; set; } = 0.15f; // Trust gained by both parties from the exchange
    public float AidNeedRestore        { get; set; } = 0.25f; // recipient's triggering need restored by this much
    public float DebtWarDampenMin      { get; set; } = 0.3f;  // War/Raid score multiplier floor when maximally indebted to someone in the target civ
    public float ForgiveTrustThreshold { get; set; } = 0.6f;  // creditor must trust the debtor this much before forgiving
    public float ForgiveMinDebt        { get; set; } = 0.2f;  // minimum |Debt| owed before forgiveness is considered
    public float ForgiveTrustGain      { get; set; } = 0.2f;  // Trust gained by both parties when debt is forgiven
}
