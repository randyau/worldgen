namespace WorldEngine.Sim.Config;

public class ReligionConfig
{
    /// <summary>Spiritual need level required to trigger a FoundReligion goal.</summary>
    public float SpiritualFoundingThreshold     { get; set; } = 0.75f;
    /// <summary>Piety skill floor; characters below this can't found religions.</summary>
    public float PietyFoundingThreshold         { get; set; } = 0.50f;
    /// <summary>Wonder personality trait floor for religion founding.</summary>
    public float WonderFoundingThreshold        { get; set; } = 0.60f;
    /// <summary>Progress added to FoundReligion goal per year while Spiritual stays high (~3 years to complete).</summary>
    public float ReligionFoundingProgressPerYear { get; set; } = 0.35f;
    /// <summary>Minimum years between religion foundings for the same character.</summary>
    public int   ReligionFoundingCooldownYears  { get; set; } = 50;

    // ─── M15 15.1 — conversion via exposure ────────────────────────────────
    // See docs/phases/m15_religion_deepened.md "Long-run balance constraints". Receptivity is a
    // hard [0,1] gate, not just a small-but-nonzero pull — a character whose weighted stats land
    // at/below 0 has zero conversion chance every year, so agnosticism is a stable end-state for
    // low-Piety/high-Rationality characters, not merely a slow-converging transient.

    /// <summary>Weight of Piety in the personal-receptivity gate.</summary>
    public float ConversionWeightPiety      { get; set; } = 0.5f;
    /// <summary>Weight of Wonder in the personal-receptivity gate.</summary>
    public float ConversionWeightWonder     { get; set; } = 0.3f;
    /// <summary>Weight of Curiosity in the personal-receptivity gate.</summary>
    public float ConversionWeightCuriosity  { get; set; } = 0.2f;
    /// <summary>Weight subtracted for Rationality — secular skepticism dampens receptivity.</summary>
    public float ConversionWeightRationality { get; set; } = 0.4f;
    /// <summary>Flat subtraction applied before clamping — raises the bar so receptivity 0 is common, not rare.</summary>
    public float ConversionBaselineSkepticism { get; set; } = 0.25f;

    /// <summary>Overall per-year conversion-roll rate scale — keeps religions spreading gradually (centuries, not years) even in a maximally receptive population.</summary>
    public float ConversionBasePullScale    { get; set; } = 0.05f;
    /// <summary>Multiplier applied to a candidate religion's pull per point of positive Zealotry — militant religions pressure conversion harder within their own civ.</summary>
    public float ZealotryConversionBonus    { get; set; } = 0.5f;
    /// <summary>How strongly an existing religion Membership's Loyalty resists conversion pressure toward a different religion.</summary>
    public float ExistingLoyaltyResistance  { get; set; } = 0.6f;
    /// <summary>Initial Membership.Loyalty granted on a fresh conversion.</summary>
    public float InitialConvertLoyalty      { get; set; } = 0.3f;

    // ─── M15 15.2 — schism ──────────────────────────────────────────────────
    // Doctrinal tension proxy: a large membership with low average Loyalty (many
    // exposure-converted members rather than devout founders) is treated as ripe for schism —
    // see docs/phases/m15_religion_deepened.md "Long-run balance constraints" point 3.

    /// <summary>Minimum living membership before a religion is even eligible for schism.</summary>
    public int   SchismMinMembers            { get; set; } = 6;
    /// <summary>Average member Loyalty (excluding the leader) must be below this for schism eligibility.</summary>
    public float SchismAvgLoyaltyThreshold   { get; set; } = 0.5f;
    /// <summary>Annual schism probability when eligible, scaled by (1 - avgLoyalty).</summary>
    public float SchismBaseChance            { get; set; } = 0.04f;
}
