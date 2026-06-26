namespace WorldEngine.Sim.Events;

// ─── Character ────────────────────────────────────────────────────────────────

internal sealed record CharacterBornPayload(
    long CharacterId, string CharacterName, string? Epithet,
    float Ambition, float Aggression, string? Role = null, string? Source = null);

internal sealed record CharacterDeathPayload(
    long CharacterId, string CharacterName, string Cause, int AgeSeason);

internal sealed record CharacterWellbeingPayload(
    long CharacterId, string CharacterName, float Wellbeing);

internal sealed record CharacterGriefPayload(
    long CharacterId, string CharacterName,
    long DeceasedId, string DeceasedName,
    float Intensity, float Wellbeing, bool HasAvenge);

internal sealed record ArtworkCreatedPayload(
    long CharacterId, string CharacterName, string ArtType, float Wellbeing);

internal sealed record GoalEventPayload(
    long CharacterId, string CharacterName,
    string GoalType, string GoalObject, long? TargetId, float Intensity,
    string Outcome = "formed");  // "formed", "completed", or "abandoned"

internal sealed record CharacterCrystallizedPayload(
    long OldCharacterId, string OldName, long NewCharacterId, string NewName);

// ─── Alliance / rivalry / war ─────────────────────────────────────────────────

internal sealed record AllianceFormedPayload(
    long DeclarerId, string DeclarerName, long TargetId, string TargetName,
    long DeclarerCivId, long TargetCivId);

internal sealed record AllianceBrokenPayload(
    long CharacterAId, string CharacterAName,
    long CharacterBId, string CharacterBName, string Reason);

internal sealed record RivalryFormedPayload(
    long CharacterId, string CharacterName, long TargetId, string TargetName);

internal sealed record WarDeclaredPayload(
    long DeclarerId, string DeclarerName,
    long DeclarerCivId, string DeclarerCivName,
    long TargetCivId, string TargetCivName,
    string Cause, string CauseDescription, int WarNumber);

internal sealed record WarEndedPayload(
    long CivAId, string CivAName, long CivBId, string CivBName,
    string Outcome, int WarNumber);

internal sealed record NegotiatedPayload(
    long CharacterId, string CharacterName, long TargetId, float TrustGain);

// ─── Battle / raid ────────────────────────────────────────────────────────────

internal sealed record BattlePayload(
    long RaiderId, string RaiderName, int Damage, int SettlementHealth,
    string RaidOutcome, bool RaiderWounded, int RaiderHealthPct);

// ─── Settlement / civilization ────────────────────────────────────────────────

internal sealed record CivFoundedPayload(
    long CivId, string CivName, long FounderId, string FounderName);

internal sealed record CivCollapsedPayload(
    long CivId, string? Reason = null);

internal sealed record SettlementFoundedPayload(
    long FounderId, string FounderName, long CivId, string CivName, int StartingPopulation);

internal sealed record SettlementDestroyedPayload(
    long FounderId, long DestroyerId, string DestroyerName, int TimesSettled);

internal sealed record SettlementConqueredPayload(
    long ConquererId, string ConquererName, long ConquerorCivId, long PreviousCivId, int SurvivingPop);

internal sealed record SettlementAbandonedPayload(
    long FounderId, int FoundedYear, int TimesSettled, int Population);

internal sealed record DiseaseOutbreakPayload(int Population);

internal sealed record DiseaseRecoveredPayload(int Population, int DurationYears);

internal sealed record WildlifeRaidPayload(
    int PopulationBefore, int PopulationLost,
    long DefenderId = 0, string? DefenderName = null);

internal sealed record SettlementStrainPayload(
    string Resource, float Ratio, string Impact);

internal sealed record SuccessionPayload(
    long PredecessorId, string PredecessorName, int PredecessorOrdinal,
    long SuccessorId, string SuccessorName, int SuccessorOrdinal);

internal sealed record SuccessionCrisisPayload(long CivId, string CivName, int CrisisEndYear);

// ─── Tier 2 ───────────────────────────────────────────────────────────────────

internal sealed record SpecialistAppointedPayload(
    long CharacterId, string CharacterName, string Role, int Population, int Threshold);

internal sealed record MerchantTradePayload(
    long CharacterId, string CharacterName, string TradedResource,
    int DestX, int DestY);

internal sealed record ScholarDiscoveryPayload(
    long CharacterId, string CharacterName, string DiscoveryType,
    string BonusKey, float BonusAmount);

internal sealed record PhysicianHealedPayload(
    long CharacterId, string CharacterName,
    long PatientId, string PatientName, int Healed, bool Critical);

internal sealed record ArtisanCraftedPayload(
    long CharacterId, string CharacterName, string GoodType);

// ─── Beast ────────────────────────────────────────────────────────────────────

internal sealed record BeastSpawnedPayload(
    long BeastId, string BeastName, string SpeciesId, bool IsLegendary);

internal sealed record BeastDeathPayload(
    long BeastId, string BeastName, string SpeciesId,
    bool IsLegendary, int AgeSeason, string Cause,
    long KillerId = 0, string? KillerName = null);

internal sealed record BeastReproducedPayload(
    long ParentId, string ParentName, long OffspringId, string OffspringName, string SpeciesId);

internal sealed record BeastEncounterPayload(
    long AttackerId, string AttackerName, long TargetId, string TargetName);

internal sealed record BeastCharEncounterPayload(
    long CharacterId, string CharacterName, long BeastId, string BeastName,
    int Damage, int CounterDamage, int CharHealthAfter, int BeastHealthAfter);

// ─── Environmental ────────────────────────────────────────────────────────────

internal sealed record DisasterPayload(float Intensity);

internal sealed record BiomeChangedPayload(string From, string To, float GlobalTemperatureAnomaly);

internal sealed record SeaLevelChangedPayload(float PreviousLevel, float NewLevel, float Delta);

internal sealed record EmptyPayload();
