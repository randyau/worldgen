namespace WorldEngine.Sim.Simulation;

/// <summary>
/// Central registry of RNG salt constants used by WorldRng.Next() calls throughout the sim.
/// Salts are partitioned by phase so they can never collide across phases.
/// When adding a new salt: pick the next unused integer in the block assigned to your phase.
///
/// Block layout:
///   100–199  EntityBehaviorPhase
///   300–399  EntityBehaviorPhase (beast reproduction)
///   800–809  CharacterBehaviorPhase (combat)
///   810–819  PopulationDynamicsPhase (disease)
///   820–829  PopulationDynamicsPhase (wildlife)
///   830–839  PopulationDynamicsPhase (crystallisation)
///   900–909  Tier2BehaviorPhase (general/artisan)
///   910–919  Tier2BehaviorPhase (scholar)
///   920–929  Tier2BehaviorPhase (merchant)
///   930–939  Tier2BehaviorPhase (physician / notable/exceptional rolls)
///  1000–1099 CharacterBehaviorPhase (civ-birth, grief)
///  1100–1199 CharacterBehaviorPhase (disease)
///  3000–3099 CharacterBehaviorPhase (artwork)
/// </summary>
internal static class SimRngSalts
{
    // EntityBehaviorPhase — reproduction & emergence
    public const int BeastReproduction = 300;
    public const int BeastEmergeTile   = 301;

    // CharacterBehaviorPhase — combat / beast encounter
    public const int CharBeastEncounter  = 800;

    // PopulationDynamicsPhase — disease
    public const int PopDiseaseOutbreak  = 810;
    public const int PopDiseaseSpread    = 811;
    public const int PopDiseaseRecovery  = 812;

    // PopulationDynamicsPhase — wildlife
    public const int PopWildlife         = 820;

    // PopulationDynamicsPhase — Tier2 crystallisation
    public const int PopCrystallise      = 830;

    // Tier2BehaviorPhase
    public const int T2General      = 900;  // crystallization + artisan notable roll
    public const int T2ArtisanExcep = 901;  // artisan exceptional (masterwork) roll
    public const int T2Scholar      = 910;
    public const int T2ScholarExcep = 911;  // scholar exceptional roll
    public const int T2Merchant     = 920;
    public const int T2MerchantExcep = 921; // merchant exceptional roll
    public const int T2Physician    = 930;  // physician notable roll
    public const int T2PhysicianExcep = 931; // physician exceptional roll

    // CharacterBehaviorPhase — civ-born, grief
    public const int CharCivBirth        = 1000;

    // CharacterBehaviorPhase — disease exposure / recovery
    public const int CharDiseaseExposure = 1100;
    public const int CharDiseaseRecovery = 1101;

    // CharacterBehaviorPhase — artwork
    public const int CharArtType         = 3001;
}
