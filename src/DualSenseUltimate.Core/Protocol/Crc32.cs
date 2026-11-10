namespace DualSenseUltimate.Core.Protocol;

/// <summary>CRC-32 (IEEE, reflected) as used by Sony Bluetooth HID reports. A one byte seed
/// (0xA1 input, 0xA2 output, 0xA3 feature) is hashed before the report data.</summary>
public static class Crc32
{
    public const byte SeedInput = 0xA1, SeedOutput = 0xA2, SeedFeature = 0xA3;
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(byte seed, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        crc = Table[(crc ^ seed) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    /// <summary>Writes the CRC of buffer[0..^4] into the last four bytes (little endian).</summary>
    public static void Append(byte seed, Span<byte> buffer)
    {
        uint crc = Compute(seed, buffer[..^4]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer[^4..], crc);
    }

    public static bool Verify(byte seed, ReadOnlySpan<byte> buffer) =>
        buffer.Length > 4 &&
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer[^4..]) == Compute(seed, buffer[..^4]);
}
