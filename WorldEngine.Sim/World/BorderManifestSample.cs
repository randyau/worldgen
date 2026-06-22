namespace WorldEngine.Sim.World;

public struct BorderManifestSample  // 5 bytes × 64 × 4 edges = 1,280 bytes per tile
{
    public byte Elevation;
    public byte Moisture;
    public byte HasRiverCrossing;  // 1 if a river crosses here
    public byte HasRoadCrossing;   // 1 if a road crosses here (M2+)
    public byte FlowVolume;        // river flow volume if HasRiverCrossing
}
