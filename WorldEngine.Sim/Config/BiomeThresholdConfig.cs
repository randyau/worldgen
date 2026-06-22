namespace WorldEngine.Sim.Config;

public class BiomeThresholdConfig
{
    // Elevation thresholds (byte, 0-255)
    public byte HighMountainElevation { get; set; } = 220;
    public byte MountainElevation     { get; set; } = 180;
    public byte HillsElevation        { get; set; } = 140;

    // Temperature thresholds (byte, 0-255)
    public byte HotTemperature        { get; set; } = 180;
    public byte ColdTemperature       { get; set; } = 80;
    public byte PolarTemperature      { get; set; } = 40;

    // Moisture thresholds (byte, 0-255)
    public byte WetMoisture           { get; set; } = 160;
    public byte DryMoisture           { get; set; } = 60;
    public byte AridMoisture          { get; set; } = 30;
}
