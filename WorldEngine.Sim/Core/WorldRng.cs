using System.Buffers.Binary;
using System.IO.Hashing;

namespace WorldEngine.Sim.Core;

public static class WorldRng
{
    public static float FloatAt(int worldSeed, long tick, int x, int y, int salt)
    {
        uint hash = HashInputs(worldSeed, tick, x, y, salt);
        // Map to [0, 1) using top 24 bits for float precision
        return (hash >> 8) / (float)(1 << 24);
    }

    public static int IntAt(int worldSeed, long tick, int x, int y, int min, int max, int salt)
    {
        uint hash = HashInputs(worldSeed, tick, x, y, salt);
        return min + (int)(hash % (uint)(max - min));
    }

    private static uint HashInputs(int worldSeed, long tick, int x, int y, int salt)
    {
        Span<byte> buffer = stackalloc byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(buffer[0..4], worldSeed);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[4..12], tick);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[12..16], x);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[16..20], y);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[20..24], salt);
        return XxHash32.HashToUInt32(buffer);
    }
}
