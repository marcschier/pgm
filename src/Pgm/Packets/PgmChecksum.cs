// Copyright (c) marcschier. Licensed under the MIT License.

namespace Pgm.Packets;

/// <summary>Computes the RFC 3208 one's-complement checksum over a PGM packet.</summary>
public static class PgmChecksum
{
    /// <summary>Computes the checksum value for a PGM packet.</summary>
    /// <param name="packet">The complete encoded PGM packet with the checksum field cleared.</param>
    /// <returns>The checksum to write to the fixed PGM header.</returns>
    public static ushort Compute(ReadOnlySpan<byte> packet)
    {
        uint sum = 0;
        var offset = 0;
        while (offset + 1 < packet.Length)
        {
            sum += (uint)((packet[offset] << 8) | packet[offset + 1]);
            offset += 2;
        }

        if (offset < packet.Length)
        {
            sum += (uint)(packet[offset] << 8);
        }

        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        var checksum = (ushort)~sum;
        return checksum == 0 ? ushort.MaxValue : checksum;
    }
}
