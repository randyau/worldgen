using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.UI.UI.Selection;

namespace WorldEngine.UI.UI.Present;

// MAP: The single formatting authority — sim data/enums to display strings, no Myra reference.
/// <summary>
/// Converts sim data to display strings for every panel and the event log (framework §8.1,
/// P7). No panel formats sim internals itself; unit thresholds and enum→prose mappings live
/// here so they can be retuned in one place. Instance-based (not static) as a localization seam.
/// </summary>
// MOD SEAM: localizable via Presenter — swap the instance for a localized one later.
public sealed class Presenter
{
    // ── Names ────────────────────────────────────────────────────────────────

    /// <summary>Roman numeral for a name ordinal (e.g. "Robert III"). Falls back to Arabic past XX.</summary>
    public string ToRoman(int n)
    {
        if (n <= 0 || n > 20) return n.ToString();
        string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        string[] tens = { "", "X", "XX" };
        return tens[n / 10] + ones[n % 10];
    }

    // ── Units (moved from TileInspectorPanel) ───────────────────────────────

    /// <summary>Raw 0–255 temperature byte to Celsius.</summary>
    public float TempC(byte raw) => raw * (100f / 255f) - 50f;

    /// <summary>Raw 0–255 temperature byte to Fahrenheit.</summary>
    public float TempF(byte raw) => TempC(raw) * 9f / 5f + 32f;

    /// <summary>Float-precision variant of <see cref="TempC(byte)"/> (same 0–255 scale, e.g. <c>EffectiveTemperature</c>).</summary>
    public float TempC(float raw) => raw * (100f / 255f) - 50f;

    /// <summary>Float-precision variant of <see cref="TempF(byte)"/>.</summary>
    public float TempF(float raw) => TempC(raw) * 9f / 5f + 32f;

    /// <summary>Raw signed temperature delta byte to Celsius delta.</summary>
    public float TempDeltaC(float rawDelta) => rawDelta * (100f / 255f);

    /// <summary>Raw 0–255 elevation byte to a relative label.</summary>
    public string Elevation(byte raw) => raw switch
    {
        < 40  => "Deep",
        < 90  => "Low",
        < 160 => "Mid",
        < 210 => "High",
        _     => "Peak"
    };

    /// <summary>Raw 0–255 moisture byte to a human label.</summary>
    public string Moisture(byte raw) => raw switch
    {
        < 60  => "Arid",
        < 130 => "Moderate",
        < 190 => "Humid",
        _     => "Saturated"
    };

    /// <summary>Raw 0–255 fertility byte to a human label.</summary>
    public string Fertility(byte raw) => raw switch
    {
        < 60  => "Poor",
        < 130 => "Fair",
        < 190 => "Good",
        _     => "Rich"
    };

    /// <summary>Raw 0–255 magic intensity byte to a human label.</summary>
    public string MagicIntensity(byte raw) => raw switch
    {
        < 20  => "None",
        < 80  => "Faint",
        < 160 => "Present",
        _     => "Strong"
    };

    // ── Qualitative labels (moved from TileInspectorPanel / CharacterWatchPanel) ─────────────

    /// <summary>Settlement health 0–100 to Good/Struggling/Critical.</summary>
    public string Health(int health) => health switch
    {
        >= 70 => "Good",
        >= 40 => "Struggling",
        _     => "Critical"
    };

    /// <summary>Character wellbeing -1..1 to Flourishing…Spiraling.</summary>
    public string Wellbeing(float wellbeing) => wellbeing switch
    {
        >= 0.7f  => "Flourishing",
        >= 0.3f  => "Content",
        >= -0.3f => "Neutral",
        >= -0.7f => "Distressed",
        _        => "Spiraling"
    };

    /// <summary>Settlement resource store amount to well-stocked/adequate/bare (food/water) or abundant/moderate/scarce (other).</summary>
    public string Store(string resource, float amount) => resource is "food" or "water"
        ? (amount >= 2f ? "well-stocked" : amount >= 0.5f ? "adequate" : "bare")
        : (amount >= 10f ? "abundant" : amount >= 2f ? "moderate" : "scarce");

    // ── Enums → prose ────────────────────────────────────────────────────────

    /// <summary>Short verb phrase for an event type, e.g. "declared war" (moved from EventLogPanel.ShortEventDesc).</summary>
    public string EventVerbPhrase(EventType type) => type switch
    {
        EventType.CharacterBorn            => "born",
        EventType.CharacterDied            => "died",
        EventType.CharacterMarried         => "married",
        EventType.CharacterExiled          => "exiled",
        EventType.CharacterGrieved         => "grieving",
        EventType.CharacterFlourishing     => "flourishing",
        EventType.CharacterSpiraling       => "spiraling",
        EventType.WarDeclared              => "declared war",
        EventType.WarEnded                 => "war ended",
        EventType.AllianceFormed           => "alliance formed",
        EventType.AllianceBroken           => "alliance broken",
        EventType.RivalryFormed            => "rivalry formed",
        EventType.BattleOccurred           => "battle",
        EventType.SettlementFounded        => "settlement founded",
        EventType.SettlementConquered      => "settlement conquered",
        EventType.SettlementDestroyed      => "settlement destroyed",
        EventType.SettlementAbandoned      => "settlement abandoned",
        EventType.SettlementStraining      => "settlement straining",
        EventType.SettlementGrew           => "settlement grew",
        EventType.SettlementShrank         => "settlement shrank",
        EventType.CivilizationFounded      => "civilization founded",
        EventType.CivilizationCollapsed    => "civilization collapsed",
        EventType.CivSplintered            => "civilization splintered",
        EventType.CivTraitAcquired         => "cultural trait acquired",
        EventType.TerritoryExpanded        => "territory expanded",
        EventType.TerritoryLost            => "territory lost",
        EventType.ImprovementBuilt         => "improvement built",
        EventType.GoalFormed               => "goal set",
        EventType.GoalResolved             => "goal achieved",
        EventType.ArtworkCreated           => "artwork created",
        EventType.ArtisanCrafted           => "crafted",
        EventType.ScholarDiscovery         => "discovery made",
        EventType.MerchantTradeCompleted   => "trade completed",
        EventType.PhysicianHealed          => "healed",
        EventType.CharacterCrystallized    => "crystallized",
        EventType.DiseaseOutbreak          => "disease outbreak",
        EventType.DiseaseRecovered         => "disease cleared",
        EventType.DroughtBegan             => "drought began",
        EventType.DroughtEnded             => "drought ended",
        EventType.WildlifeRaid             => "wildlife raid",
        EventType.SuccessionOccurred       => "succession",
        EventType.SuccessionCrisis         => "succession crisis",
        EventType.AppointedToRole          => "appointed",
        EventType.DismissedFromRole        => "dismissed",
        EventType.ArtifactCreated          => "artifact created",
        EventType.ArtifactDestroyed        => "artifact destroyed",
        EventType.ArtifactTransferred      => "artifact transferred",
        EventType.ReligionFounded          => "religion founded",
        EventType.ReligionExtinct          => "religion went extinct",
        EventType.EmissaryDispatched       => "emissary dispatched",
        EventType.EmissaryLost             => "emissary lost",
        EventType.ReligiousEmissaryArrived => "religious emissary arrived",
        EventType.CivIntelGathered         => "intelligence gathered",
        EventType.VolcanicEruption         => "volcanic eruption",
        EventType.EarthquakeOccurred       => "earthquake",
        EventType.WildfireOccurred         => "wildfire",
        EventType.FloodOccurred            => "flood",
        EventType.SeaLevelChanged          => "sea level changed",
        EventType.BiomeChanged             => "biome changed",
        EventType.ClimateShifted           => "climate shifted",
        EventType.ResourceRecovered        => "resource recovered",
        EventType.BeastSpawned             => "beast spawned",
        EventType.BeastAwakened            => "beast awakened",
        EventType.BeastDied                => "beast died",
        EventType.BeastSlain               => "beast slain",
        EventType.BeastReproduced          => "beast reproduced",
        EventType.BeastEncountered         => "beast encountered",
        EventType.BeastAttackedChar        => "attacked by beast",
        EventType.GodModeArtifactPlaced    => "✦ artifact placed",
        EventType.GodModeDisasterTriggered => "✦ disaster triggered",
        EventType.GodModeEntitySpawned     => "✦ entity spawned",
        EventType.GodModeCharacterCreated  => "✦ character created",
        EventType.GodModeCharacterNudged   => "✦ character nudged",
        EventType.GodModeCivilizationForced => "✦ civilization forced",
        EventType.SeaVoyageEmbarked        => "set sail",
        EventType.SeaVoyageCompleted       => "made landfall",
        _                                   => type.ToString()
    };

    /// <summary>Intent phrase for a character goal type, e.g. "wants to found a city".</summary>
    public string GoalIntentPhrase(GoalType type) => type switch
    {
        GoalType.Survive          => "struggling to survive",
        GoalType.Security         => "seeking safety",
        GoalType.Acquire          => "seeking resources",
        GoalType.Flee             => "fleeing danger",
        GoalType.Endure           => "enduring hardship",
        GoalType.Dominance        => "seeking dominance over a rival",
        GoalType.Alliance         => "seeking an alliance",
        GoalType.Unify            => "seeking to unify with a rival",
        GoalType.Bond             => "seeking companionship",
        GoalType.Protect          => "protecting someone trusted",
        GoalType.Avenge           => "seeking vengeance",
        GoalType.Grieve           => "grieving a loss",
        GoalType.Create           => "pursuing a creative work",
        GoalType.FoundCity        => "wants to found a city",
        GoalType.BuildImprovement => "planning an improvement",
        GoalType.FoundReligion    => "wants to found a religion",
        GoalType.SlayBeast        => "hunting a legendary beast",
        GoalType.CovetArtifact    => "coveting an artifact",
        GoalType.SeaVoyage        => "voyaging across the sea",
        _                         => type.ToString()
    };

    /// <summary>Name for a disaster type, e.g. "volcanic ashfall".</summary>
    public string DisasterName(DisasterType type) => type switch
    {
        DisasterType.Wildfire      => "wildfire",
        DisasterType.Flood         => "flood",
        DisasterType.VolcanicAsh   => "volcanic ashfall",
        DisasterType.SeismicDamage => "seismic damage",
        _                          => type.ToString()
    };

    /// <summary>Display name for an artifact category.</summary>
    public string ArtifactCategoryName(ArtifactCategory category) => category switch
    {
        ArtifactCategory.Weapon  => "Weapon",
        ArtifactCategory.Armor   => "Armor",
        ArtifactCategory.Regalia => "Regalia",
        ArtifactCategory.Tome    => "Tome",
        ArtifactCategory.Relic   => "Relic",
        ArtifactCategory.Jewelry => "Jewelry",
        ArtifactCategory.Artwork => "Artwork",
        _                        => category.ToString()
    };

    /// <summary>Display label for a selection kind, e.g. "Settlement".</summary>
    public string SelectionKindLabel(SelectionKind kind) => kind switch
    {
        SelectionKind.Tile       => "Tile",
        SelectionKind.Settlement => "Settlement",
        SelectionKind.Character  => "Character",
        SelectionKind.Civ        => "Civilization",
        _                        => "None"
    };

    // ── Names ────────────────────────────────────────────────────────────────

    /// <summary>Formats a character's name with an optional epithet, e.g. "Aria the Bold".</summary>
    public string CharacterName(string name, string? epithet) =>
        string.IsNullOrEmpty(epithet) ? name : $"{name} the {epithet}";

    /// <summary>Formats a civilization label, annotating collapsed civs.</summary>
    public string CivLabel(string name, bool isCollapsed) =>
        isCollapsed ? $"{name} (collapsed)" : name;

    /// <summary>Formats an in-world year/season, e.g. "Year 412, Autumn".</summary>
    public string YearSeason(int year, Season season) => $"Year {year}, {season}";
}
