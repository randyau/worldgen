namespace WorldEngine.Sim.Config;

/// <summary>Root config container; all subsections loaded from sim_config.toml.</summary>
public class SimConfig
{
    public WorldGenConfig WorldGen { get; set; } = new();
    public DisasterConfig Disasters { get; set; } = new();
    public EventsConfig Events { get; set; } = new();
    public ClimateConfig Climate { get; set; } = new();
    public SimLoopConfig SimLoop { get; set; } = new();
    public BeastsSimConfig Beasts { get; set; } = new();
    public CharacterSimConfig Character { get; set; } = new();
    public CharacterNamesConfig CharacterNames { get; set; } = new();
    public SettlementConfig Settlement { get; set; } = new();
    public ResourcePressureConfig ResourcePressure { get; set; } = new();
    public SettlementNamesConfig SettlementNames { get; set; } = new();
    public TerritoryConfig       Territory       { get; set; } = new();
    public ImprovementsConfig    Improvements    { get; set; } = new();
    public CulturalTraitsConfig  CulturalTraits  { get; set; } = new();
    public EmissaryConfig        Emissary        { get; set; } = new();
    public WarConfig             War             { get; set; } = new();
    public ReligionConfig        Religion        { get; set; } = new();
    public UnrestConfig          Unrest          { get; set; } = new();
    public UtilityAffinityConfig UtilityAffinity { get; set; } = new();
    public WildlifeRiskConfig    WildlifeRisk    { get; set; } = new();
    public ArtifactConfig        Artifacts       { get; set; } = new();

    // Loaded separately by AncestryLoader — not from sim_config.toml
    public AncestryRegistry AncestryRegistry { get; set; } = AncestryRegistry.Empty;

    public static SimConfig Default() => new();
}
