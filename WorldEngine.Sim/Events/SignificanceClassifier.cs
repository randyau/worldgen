using System.Text.Json;
using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Events;

/// <summary>
/// Maps a (type, payload, isFirstOfKind) tuple to an <see cref="EventTier"/> and
/// <see cref="PopulationImpact"/>. The final tier is the max of the verb-class floor
/// and the impact-derived tier, bumped one level when the event is first of its kind.
/// </summary>
public static class SignificanceClassifier
{
    // threshold for SeaLevelChanged delta to be Catastrophic (in normalized sea level units)
    // The spec says "delta > 5m" but we use normalized units; 5m ≈ 0.02 in fraction-of-255
    private const float SeaLevelCatastrophicDelta = 0.02f;

    public static (EventTier tier, PopulationImpact impact) Classify(
        EventType type,
        string payloadJson,
        bool isFirstOfKind)
    {
        var impact = ComputeImpact(type, payloadJson);
        var verbTier = VerbTierFloor(VerbClassification.Classify(type));
        var impactTier = ImpactTier(impact);
        var baseTier = (EventTier)Math.Max((int)verbTier, (int)impactTier);
        var finalTier = isFirstOfKind ? BumpTier(baseTier) : baseTier;
        return (finalTier, impact);
    }

    private static PopulationImpact ComputeImpact(EventType type, string payloadJson)
    {
        float intensity = TryGetFloat(payloadJson, "Intensity");
        return type switch
        {
            EventType.VolcanicEruption  => intensity > 0.8f ? PopulationImpact.Catastrophic
                                         : intensity > 0.5f ? PopulationImpact.Major
                                         : PopulationImpact.Moderate,
            EventType.EarthquakeOccurred => intensity > 0.9f ? PopulationImpact.Catastrophic
                                          : intensity > 0.6f ? PopulationImpact.Major
                                          : PopulationImpact.Moderate,
            EventType.FloodOccurred     => intensity > 0.7f ? PopulationImpact.Major : PopulationImpact.Minor,
            EventType.WildfireOccurred  => intensity > 0.6f ? PopulationImpact.Moderate : PopulationImpact.Minor,
            EventType.DroughtBegan      => PopulationImpact.Moderate,
            EventType.SeaLevelChanged   => TryGetFloat(payloadJson, "Delta") > SeaLevelCatastrophicDelta
                                           ? PopulationImpact.Catastrophic : PopulationImpact.Major,
            // Beast events
            EventType.BeastAwakened     => PopulationImpact.Major,   // → Regional tier
            EventType.BeastSlain        => PopulationImpact.None,     // VerbClass.Destruction → Regional
            EventType.BeastDied         => PopulationImpact.None,
            // Tier 1 character events — all Headline (Tier 1 involved, per §23)
            EventType.CharacterBorn     => PopulationImpact.Minor,    // → Headline via Tier1 rule
            EventType.CharacterDied     => PopulationImpact.Moderate, // → Headline
            EventType.CharacterMarried  => PopulationImpact.None,
            EventType.CharacterExiled   => PopulationImpact.None,
            EventType.CharacterGrieved     => PopulationImpact.Minor,
            EventType.CharacterFlourishing => PopulationImpact.Minor,
            EventType.CharacterSpiraling   => PopulationImpact.Minor,
            EventType.ArtworkCreated       => PopulationImpact.None,
            EventType.GoalFormed           => PopulationImpact.None,
            EventType.GoalResolved         => PopulationImpact.None,
            // Political events — Headline
            EventType.WarDeclared       => PopulationImpact.Major,
            EventType.WarEnded          => PopulationImpact.Major,
            EventType.BattleOccurred    => PopulationImpact.Moderate,
            EventType.CivilizationFounded   => PopulationImpact.Major,
            EventType.CivilizationCollapsed => PopulationImpact.Catastrophic,
            // Settlement events — Regional (Creation/Destruction verb floors apply)
            EventType.SettlementFounded     => PopulationImpact.Minor,
            EventType.SettlementDestroyed   => PopulationImpact.Moderate,
            EventType.SettlementConquered   => PopulationImpact.Major,    // → Headline (Transfer verb + Major)
            // Relationship events — Character tier
            EventType.AllianceFormed    => PopulationImpact.None,
            EventType.AllianceBroken    => PopulationImpact.None,
            EventType.RivalryFormed     => PopulationImpact.None,
            EventType.Negotiated        => PopulationImpact.None,
            EventType.SuccessionOccurred      => PopulationImpact.Minor,
            // Population events (3400-range)
            EventType.SettlementGrew      => PopulationImpact.None,   // suppressed: too noisy
            EventType.SettlementShrank    => PopulationImpact.None,
            EventType.SettlementAbandoned  => PopulationImpact.Major,  // → Regional tier
            EventType.SettlementStraining  => PopulationImpact.Moderate, // → Character tier; Major when crisis
            // Tier 2 character events
            EventType.AppointedToRole         => PopulationImpact.None,
            EventType.DismissedFromRole       => PopulationImpact.None,
            EventType.MerchantTradeCompleted  => PopulationImpact.None,
            EventType.ScholarDiscovery        => PopulationImpact.Minor,
            EventType.PhysicianHealed         => PopulationImpact.None,
            EventType.CharacterCrystallized   => PopulationImpact.Minor,
            EventType.ArtisanCrafted          => PopulationImpact.None,
            _                                 => PopulationImpact.None,
        };
    }

    private static EventTier VerbTierFloor(VerbClass verb) => verb switch
    {
        VerbClass.Destruction => EventTier.Regional,
        VerbClass.Transformation => EventTier.Character,
        _ => EventTier.Background
    };

    private static EventTier ImpactTier(PopulationImpact impact) => impact switch
    {
        PopulationImpact.Catastrophic => EventTier.Headline,
        PopulationImpact.Major        => EventTier.Regional,
        PopulationImpact.Moderate     => EventTier.Character,
        _                             => EventTier.Background
    };

    private static EventTier BumpTier(EventTier tier) =>
        tier < EventTier.Headline ? (EventTier)((int)tier + 1) : EventTier.Headline;

    private static float TryGetFloat(string json, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var el))
                return el.GetSingle();
        }
        catch { /* malformed JSON — treat as 0 */ }
        return 0f;
    }
}
