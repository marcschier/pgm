// Copyright (c) marcschier. Licensed under the MIT License.

namespace Pgm.Packets;

/// <summary>Provides PGM-over-UDP encapsulation helpers.</summary>
public static class PgmUdpEncapsulation
{
    /// <summary>Gets the default UDP port for PGM encapsulation.</summary>
    public const int DefaultPort = PgmPacketConstants.UdpEncapsulationPort;

    /// <summary>Writes a PGM packet as the payload of a UDP datagram buffer.</summary>
    /// <param name="packet">The PGM packet.</param>
    /// <param name="destination">The UDP payload destination span.</param>
    /// <returns><see langword="true"/> when the packet was written.</returns>
    public static bool TryWritePayload(PgmPacket packet, Span<byte> destination)
    {
        return packet.TryWrite(destination);
    }

    /// <summary>Parses a PGM packet from a UDP datagram payload.</summary>
    /// <param name="payload">The UDP payload.</param>
    /// <param name="packet">The parsed packet.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParsePayload(ReadOnlySpan<byte> payload, out PgmPacket packet)
    {
        return PgmPacket.TryParse(payload, out packet);
    }
}
