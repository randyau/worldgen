namespace WorldEngine.Sim.Events;

// ─── Character ────────────────────────────────────────────────────────────────

internal sealed record CharacterBornPayload(
    long CharacterId, string CharacterName, string? Epithet,
    float Ambition, float Aggression,
    string? Role = null, string? Source = null, string? AncestryId = null);

internal sealed record CharacterDeathPayload(
    long CharacterId, string CharacterName, string Cause, int AgeSeason,
    string? AncestryId = null, int MaxAgeSeason = 0);

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

internal sealed record MarriagePayload(
    long CharacterAId, string CharacterAName, long CharacterBId, string CharacterBName,
    long FamilyOrgId);

// M13 13.2 — Debt as an obligation mechanic.
internal sealed record DebtIncurredPayload(
    long GranterId, string GranterName, long RecipientId, string RecipientName, float DebtMagnitude);

internal sealed record DebtForgivenPayload(
    long CreditorId, string CreditorName, long DebtorId, string DebtorName, float ForgivenMagnitude);

// M13 13.1 — Fear as a submission/appeasement axis.
internal sealed record RivalryPlacatedPayload(
    long CharacterId, string CharacterName, long TargetId, string TargetName);

// M13 13.4 — non-ruler bonds reaching the wider world: asylum/defection.
internal sealed record CharacterDefectedPayload(
    long CharacterId, string CharacterName,
    long OldCivId, string OldCivName, long NewCivId, string NewCivName,
    long ConfidantId, string ConfidantName);

// M13 13.5 — new relationship-transition events.
internal sealed record RivalsReconciledPayload(
    long CharacterId, string CharacterName, long TargetId, string TargetName);

internal sealed record RivalryEscalatedToFeudPayload(
    long CharacterId, string CharacterName, long TargetId, string TargetName);

internal sealed record CharacterEstrangedPayload(
    long CharacterAId, string CharacterAName, long CharacterBId, string CharacterBName);

internal sealed record OathBrokenPayload(
    long DebtorId, string DebtorName, long CreditorId, string CreditorName,
    long DebtorCivId, long CreditorCivId, float DebtBroken);

internal sealed record WarDeclaredPayload(
    long DeclarerId, string DeclarerName,
    long DeclarerCivId, string DeclarerCivName,
    long TargetCivId, string TargetCivName,
    string Cause, string CauseDescription, int WarNumber,
    string[]? DeclarerTraits = null);

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
    long CivId, string CivName, long FounderId, string FounderName,
    string FoundingOrigin = "NomadsSettled",
    long? ParentCivId = null, string? ParentCivName = null);

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

/// <summary>
/// Snapshot of the three structural factors that contributed to this outbreak (D4).
/// Allows CausalEdgeBuilder and narrative systems to attribute siege→plague, famine→plague, etc.
/// </summary>
internal sealed record DiseaseOutbreakPayload(
    int   Population,
    float DensityFactor,   // 1 + (Pop/Cap) × DensityMult — always ≥ 1
    float ContactFactor,   // DiseaseContactMult if civ had active contact, else 1.0
    float FamineFactor,    // DiseaseFamineMult if in food crisis, else 1.0
    bool  InWar,           // civ was at war when outbreak started (causal marker)
    bool  InFamine         // settlement was below famine threshold (causal marker)
);

internal sealed record DiseaseRecoveredPayload(int Population, int DurationYears);

internal sealed record WildlifeRaidPayload(
    int PopulationBefore, int PopulationLost,
    long DefenderId = 0, string? DefenderName = null);

internal sealed record SettlementStrainPayload(
    string Resource, float Ratio, string Impact);

internal sealed record SuccessionPayload(
    long PredecessorId, string PredecessorName, int PredecessorOrdinal,
    long SuccessorId, string SuccessorName, int SuccessorOrdinal,
    string[]? CivTraits = null);

internal sealed record SuccessionCrisisPayload(long CivId, string CivName, int CrisisEndYear);

// ─── Tier 2 ───────────────────────────────────────────────────────────────────

internal sealed record SpecialistAppointedPayload(
    long CharacterId, string CharacterName, string Role, int Population, int Threshold);

internal sealed record SpecialistDismissedPayload(
    long CharacterId, string CharacterName, string Role, string Reason);

internal sealed record MerchantTradePayload(
    long CharacterId, string CharacterName, string TradedResource,
    int DestX, int DestY);

// M14 14.1 — a real Wealth transfer resulting from a merchant's trade: the destination
// settlement paid PaidValue (in precious-commodity value) for Quantity units of Resource;
// MerchantShare of that went to the merchant's personal Wealth, the remainder recirculated back
// into the merchant's home settlement's own ResourceStores (EconomyConfig.MerchantHomeCutFraction).
internal sealed record TradePaidPayload(
    long CharacterId, string CharacterName, string Resource, float Quantity,
    float PaidValue, float MerchantShare, int DestX, int DestY);

// M14 14.2 — persistent trade routes / caravan transit.
// Reopened distinguishes the two cases TradeRouteFormed covers: a fresh graduation from repeated
// one-shot trades (false) vs. a previously Severed route automatically reopening (true).
internal sealed record TradeRouteFormedPayload(
    int TileAX, int TileAY, int TileBX, int TileBY, bool Reopened);

// Cause is one of "war", "settlement-lost", or "losses" (sustained caravan interception/disaster/
// piracy losses reaching EconomyConfig.TradeRouteSeverThreshold).
internal sealed record TradeRouteSeveredPayload(
    int TileAX, int TileAY, int TileBX, int TileBY, string Cause);

// Cause is one of "war" (interception), "disaster", or "piracy" — see EconomyConfig
// .CaravanInterceptionChance/CaravanDisasterChance/CaravanPiracyChance.
internal sealed record CaravanRaidedPayload(
    long MerchantId, string Resource, float Quantity, string Cause,
    int HomeX, int HomeY, int DestX, int DestY);

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

// ─── Cultural Traits (Phase 3.2) ─────────────────────────────────────────────

internal sealed record CivTraitAcquiredPayload(
    int    CivId,
    string CivName,
    string Trait,
    string Reason);

// ─── Territory / Improvements (Phase 3.0) ────────────────────────────────────

internal sealed record TerritoryExpandedPayload(
    long CivId, string CivName, int CityTileX, int CityTileY,
    int TileCount, int TotalOwned);

internal sealed record TerritoryLostPayload(
    long CivId, string CivName, int CityTileX, int CityTileY,
    int TilesReleased, int TotalOwned, string Reason);

internal sealed record ImprovementBuiltPayload(
    long BuilderId, string BuilderName, long CivId,
    int TileX, int TileY, string ImprovementType);

// ─── M4 Phase 1 — Emissary events ────────────────────────────────────────────

internal sealed record EmissaryDispatchedPayload(
    long FromCivId, string FromCivName,
    long ToCivId,   string ToCivName,
    string Purpose,
    int    ArrivalYear,
    float  SurvivalChance);

internal sealed record EmissaryLostPayload(
    long FromCivId, string FromCivName,
    long ToCivId,   string ToCivName,
    string Purpose);

internal sealed record ReligiousEmissaryArrivedPayload(
    long FromCivId, string FromCivName,
    long ToCivId,   string ToCivName,
    int  CharactersAffected);

internal sealed record CivIntelGatheredPayload(
    long FromCivId, string FromCivName,
    long ToCivId,   string ToCivName,
    float NewConfidence);

// ─── M4 Phase 3 — Religion ────────────────────────────────────────────────────

internal sealed record ReligionFoundedPayload(
    long   FounderId, string FounderName,
    int    Year,
    int    TileX, int TileY);

// ─── M11 — sea voyages ─────────────────────────────────────────────────────

internal sealed record SeaVoyagePayload(
    long CharacterId, string CharacterName, long CivId,
    int TileX, int TileY);

// ─── S2 Splinter / Secession ─────────────────────────────────────────────────

/// <summary>
/// Fired when one or more settlements secede from a parent civ and form a new one.
/// Tier ≥ Regional; Headline if secession is large (≥3 settlements).
/// </summary>
internal sealed record CivSplinteredPayload(
    long   ParentCivId,   string ParentCivName,
    long   NewCivId,      string NewCivName,
    long   NewRulerId,    string NewRulerName,
    int    SettlementsSeceded,
    int    PopulationTransferred,
    float  LeaderUnrest,
    int    TileX, int TileY);

// ─── M5 Artifacts (W0 foundation) ────────────────────────────────────────────

internal sealed record ArtifactCreatedPayload(
    long ArtifactId, string ArtifactName, string Category,
    long CreatorId, string CreatorName, string Origin, float Quality);

internal sealed record ArtifactTransferredPayload(
    long ArtifactId, string ArtifactName, string FromOwner, string ToOwner, string Reason);

// M14 14.3 — a CovetArtifact goal-holder bought the coveted artifact from its living owner.
// Price is the full Wealth transferred (PricingService.ArtifactEffectivePrice at resolution time).
internal sealed record ArtifactPurchasedPayload(
    long ArtifactId, string ArtifactName, long BuyerId, string BuyerName,
    string FromOwner, string ToOwnerName, float Price);

internal sealed record ArtifactDestroyedPayload(
    long ArtifactId, string ArtifactName, string Cause);

// ─── M14 14.4 — Guild organizations, treasuries, civ-level economic ruin ────

/// <summary>A new Guild-kind Organization forms (a merchant joining an already-existing Guild
/// fires no separate event — see Tier2BehaviorPhase.FormOrJoinGuild).</summary>
internal sealed record GuildFormedPayload(
    long OrganizationId, string GuildName,
    long FounderId, string FounderName, int TileX, int TileY);

/// <summary>Decision 9: any member deposits personal Wealth into their Organization's Treasury,
/// no authority check.</summary>
internal sealed record TreasuryContributionPayload(
    long CharacterId, string CharacterName,
    long OrganizationId, string OrganizationName, float Amount);

/// <summary>Decision 9: the Leader-only converse of TreasuryContribution — Organization.Treasury
/// to a leader-designated member's personal Wealth.</summary>
internal sealed record TreasuryWithdrawalPayload(
    long LeaderId, string LeaderName,
    long OrganizationId, string OrganizationName,
    long RecipientId, string RecipientName, float Amount);

/// <summary>Decision 10: a Civilization-kind Organization's Treasury first crosses negative
/// (edge-triggered — see Organization.TreasuryInsolvencyFlagged).</summary>
internal sealed record TreasuryInsolventPayload(
    long CivId, string CivName, float Treasury);

/// <summary>War reparations (deep-review finding folded into 14.4): a one-time Wealth transfer
/// from the losing civ's Treasury to the winner's on war resolution — allowed to drive the
/// loser's Treasury negative (the TreasuryInsolvent trigger above).</summary>
internal sealed record WarReparationsPaidPayload(
    long WinnerCivId, string WinnerCivName,
    long LoserCivId, string LoserCivName, float Amount);

/// <summary>A Guild's Leader seat changes hands via SuccessionResolver.SelectSuccessor
/// (unmodified — no new succession mechanism, per the M12 audit note).</summary>
internal sealed record GuildSuccessionPayload(
    long OrganizationId, string GuildName,
    long PredecessorId, string PredecessorName,
    long SuccessorId, string SuccessorName);

// ─── GodMode authored events (9000-range) ────────────────────────────────────

internal sealed record GodModeArtifactPayload(
    long ArtifactId, string ArtifactName, string Category, float Quality);

internal sealed record GodModeDisasterPayload(
    string DisasterType, float Intensity);

internal sealed record GodModeCharacterPayload(
    long CharacterId, string CharacterName, string? AncestryId);

internal sealed record GodModeNudgePayload(
    long CharacterId, string CharacterName, string Nudge);
