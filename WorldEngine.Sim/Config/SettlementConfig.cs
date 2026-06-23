namespace WorldEngine.Sim.Config;

public sealed class SettlementConfig
{
    public float PopGrowthRate       { get; set; } = 2.0f;
    public float PopDecayRate        { get; set; } = 0.05f;
    public int   PopMinViable        { get; set; } = 5;
    public int   PopMax              { get; set; } = 50_000;
    public int   CrystalPopArtisan   { get; set; } = 200;
    public int   CrystalPopScholar   { get; set; } = 300;
    public int   CrystalPopPhysician { get; set; } = 500;
    public int   CrystalPopMerchant  { get; set; } = 1_000;
}
