namespace WorldEngine.Sim.Config;

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

    // Loaded separately by AncestryLoader — not from sim_config.toml
    public AncestryRegistry AncestryRegistry { get; set; } = AncestryRegistry.Empty;

    public static SimConfig Default() => new();
}
