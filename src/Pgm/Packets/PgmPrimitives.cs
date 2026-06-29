// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace Pgm.Packets;

/// <summary>Identifies an RFC 3208 PGM packet type.</summary>
public enum PgmPacketType : byte
{
    /// <summary>Source Path Message.</summary>
    SourcePathMessage = 0x00,

    /// <summary>Poll request.</summary>
    Poll = 0x01,

    /// <summary>Poll response.</summary>
    PollResponse = 0x02,

    /// <summary>Original data packet.</summary>
    OriginalData = 0x04,

    /// <summary>Repair data packet.</summary>
    RepairData = 0x05,

    /// <summary>Negative acknowledgment.</summary>
    NegativeAcknowledgment = 0x08,

    /// <summary>Null negative acknowledgment.</summary>
    NullNegativeAcknowledgment = 0x09,

    /// <summary>NAK confirmation.</summary>
    NakConfirmation = 0x0A,

    /// <summary>Source Path Message request.</summary>
    SourcePathMessageRequest = 0x0C,
}

/// <summary>Defines bits in the fixed PGM options octet.</summary>
[Flags]
public enum PgmHeaderOptions : byte
{
    /// <summary>No fixed header options are set.</summary>
    None = 0x00,

    /// <summary>One or more option extensions are present.</summary>
    OptionsPresent = 0x80,

    /// <summary>One or more option extensions are network significant.</summary>
    NetworkSignificant = 0x40,

    /// <summary>The parity packet belongs to a variable-size transmission group.</summary>
    VariablePacketLength = 0x02,

    /// <summary>The packet is a parity packet.</summary>
    Parity = 0x01,
}

/// <summary>Identifies an address family indicator used by PGM NLAs.</summary>
public enum PgmAddressFamily : ushort
{
    /// <summary>IPv4 address family indicator.</summary>
    IPv4 = 1,

    /// <summary>IPv6 address family indicator.</summary>
    IPv6 = 2,
}

/// <summary>Represents the six-octet global source identifier in a PGM TSI.</summary>
public readonly struct PgmGlobalSourceId : IEquatable<PgmGlobalSourceId>
{
    /// <summary>The encoded length of a PGM global source identifier.</summary>
    public const int EncodedLength = 6;

    /// <summary>Initializes a new instance of the <see cref="PgmGlobalSourceId"/> struct.</summary>
    /// <param name="value">The low 48 bits to encode as the global source identifier.</param>
    public PgmGlobalSourceId(ulong value)
    {
        if ((value & 0xFFFF000000000000UL) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the numeric value of the global source identifier.</summary>
    public ulong Value { get; }

    /// <summary>Determines whether two identifiers are equal.</summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns><see langword="true"/> when the identifiers are equal.</returns>
    public static bool operator ==(PgmGlobalSourceId left, PgmGlobalSourceId right) => left.Value == right.Value;

    /// <summary>Determines whether two identifiers differ.</summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns><see langword="true"/> when the identifiers differ.</returns>
    public static bool operator !=(PgmGlobalSourceId left, PgmGlobalSourceId right) => left.Value != right.Value;

    /// <summary>Writes the global source identifier to a destination span.</summary>
    /// <param name="destination">The destination span.</param>
    /// <returns><see langword="true"/> when the identifier was written.</returns>
    public bool TryWrite(Span<byte> destination)
    {
        if (destination.Length < EncodedLength)
        {
            return false;
        }

        destination[0] = (byte)(Value >> 40);
        destination[1] = (byte)(Value >> 32);
        destination[2] = (byte)(Value >> 24);
        destination[3] = (byte)(Value >> 16);
        destination[4] = (byte)(Value >> 8);
        destination[5] = (byte)Value;
        return true;
    }

    /// <summary>Parses a global source identifier from a source span.</summary>
    /// <param name="source">The source span.</param>
    /// <param name="globalSourceId">The parsed global source identifier.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParse(ReadOnlySpan<byte> source, out PgmGlobalSourceId globalSourceId)
    {
        if (source.Length < EncodedLength)
        {
            globalSourceId = default;
            return false;
        }

        ulong value = ((ulong)source[0] << 40)
            | ((ulong)source[1] << 32)
            | ((ulong)source[2] << 24)
            | ((ulong)source[3] << 16)
            | ((ulong)source[4] << 8)
            | source[5];
        globalSourceId = new PgmGlobalSourceId(value);
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(PgmGlobalSourceId other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PgmGlobalSourceId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>Represents a PGM network-layer address.</summary>
public readonly struct PgmNetworkAddress : IEquatable<PgmNetworkAddress>
{
    /// <summary>The size of the AFI and reserved fields that prefix an NLA.</summary>
    public const int HeaderLength = 4;

    private readonly ulong _high;
    private readonly ulong _low;
    private readonly byte _length;

    /// <summary>Initializes a new instance of the <see cref="PgmNetworkAddress"/> struct.</summary>
    /// <param name="addressFamily">The address family indicator.</param>
    /// <param name="address">The address bytes.</param>
    public PgmNetworkAddress(PgmAddressFamily addressFamily, ReadOnlySpan<byte> address)
    {
        var expectedLength = GetAddressLength(addressFamily);
        if (expectedLength == 0 || address.Length != expectedLength)
        {
            throw new ArgumentException("The address length does not match the address family.", nameof(address));
        }

        AddressFamily = addressFamily;
        _length = (byte)expectedLength;
        Span<byte> bytes = stackalloc byte[16];
        bytes.Clear();
        address.CopyTo(bytes);
        _high = BinaryPrimitives.ReadUInt64BigEndian(bytes);
        _low = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(8));
    }

    /// <summary>Gets the address family indicator.</summary>
    public PgmAddressFamily AddressFamily { get; }

    /// <summary>Gets the encoded length of this network-layer address.</summary>
    public int EncodedLength => HeaderLength + _length;

    /// <summary>Determines whether two addresses are equal.</summary>
    /// <param name="left">The left address.</param>
    /// <param name="right">The right address.</param>
    /// <returns><see langword="true"/> when the addresses are equal.</returns>
    public static bool operator ==(PgmNetworkAddress left, PgmNetworkAddress right) => left.Equals(right);

    /// <summary>Determines whether two addresses differ.</summary>
    /// <param name="left">The left address.</param>
    /// <param name="right">The right address.</param>
    /// <returns><see langword="true"/> when the addresses differ.</returns>
    public static bool operator !=(PgmNetworkAddress left, PgmNetworkAddress right) => !left.Equals(right);

    /// <summary>Copies the address bytes into a new array.</summary>
    /// <returns>The address bytes.</returns>
    public byte[] GetAddressBytes()
    {
        var copy = new byte[_length];
        _ = TryCopyAddress(copy);
        return copy;
    }

    /// <summary>Copies the address octets (without the AFI prefix) to a destination span.</summary>
    /// <param name="destination">The destination span.</param>
    /// <returns><see langword="true"/> when the address was copied.</returns>
    public bool TryCopyAddress(Span<byte> destination)
    {
        if (destination.Length < _length)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, _high);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(8), _low);
        bytes.Slice(0, _length).CopyTo(destination);
        return true;
    }

    /// <summary>Writes this network-layer address to a destination span.</summary>
    /// <param name="destination">The destination span.</param>
    /// <returns><see langword="true"/> when the address was written.</returns>
    public bool TryWrite(Span<byte> destination)
    {
        if (destination.Length < EncodedLength)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)AddressFamily);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2), 0);
        return TryCopyAddress(destination.Slice(HeaderLength));
    }

    /// <summary>Parses a network-layer address from a source span.</summary>
    /// <param name="source">The source span.</param>
    /// <param name="address">The parsed network-layer address.</param>
    /// <param name="bytesRead">The number of bytes consumed.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParse(ReadOnlySpan<byte> source, out PgmNetworkAddress address, out int bytesRead)
    {
        if (source.Length < HeaderLength)
        {
            address = default;
            bytesRead = 0;
            return false;
        }

        var addressFamily = (PgmAddressFamily)BinaryPrimitives.ReadUInt16BigEndian(source);
        var addressLength = GetAddressLength(addressFamily);
        var encodedLength = HeaderLength + addressLength;
        if (addressLength == 0 || source.Length < encodedLength)
        {
            address = default;
            bytesRead = 0;
            return false;
        }

        address = new PgmNetworkAddress(addressFamily, source.Slice(HeaderLength, addressLength));
        bytesRead = encodedLength;
        return true;
    }

    /// <summary>Gets the number of address octets for a known PGM address family.</summary>
    /// <param name="addressFamily">The address family indicator.</param>
    /// <returns>The address octet count, or zero when the family is unknown.</returns>
    public static int GetAddressLength(PgmAddressFamily addressFamily)
    {
        if (addressFamily == PgmAddressFamily.IPv4)
        {
            return 4;
        }

        if (addressFamily == PgmAddressFamily.IPv6)
        {
            return 16;
        }

        return 0;
    }

    /// <inheritdoc/>
    public bool Equals(PgmNetworkAddress other) =>
        AddressFamily == other.AddressFamily && _length == other._length && _high == other._high && _low == other._low;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PgmNetworkAddress other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine((ushort)AddressFamily, _length, _high, _low);
}

/// <summary>Represents the fixed common PGM header defined by RFC 3208 section 8.</summary>
public readonly struct PgmHeader
{
    /// <summary>The encoded length of the fixed common PGM header.</summary>
    public const int EncodedLength = 16;

    /// <summary>Initializes a new instance of the <see cref="PgmHeader"/> struct.</summary>
    /// <param name="sourcePort">The source port field.</param>
    /// <param name="destinationPort">The destination port field.</param>
    /// <param name="type">The packet type.</param>
    /// <param name="options">The fixed header options.</param>
    /// <param name="checksum">The packet checksum.</param>
    /// <param name="globalSourceId">The global source identifier.</param>
    /// <param name="tsduLength">The transport service data unit length.</param>
    public PgmHeader(
        ushort sourcePort,
        ushort destinationPort,
        PgmPacketType type,
        PgmHeaderOptions options,
        ushort checksum,
        PgmGlobalSourceId globalSourceId,
        ushort tsduLength)
    {
        SourcePort = sourcePort;
        DestinationPort = destinationPort;
        Type = type;
        Options = options;
        Checksum = checksum;
        GlobalSourceId = globalSourceId;
        TsduLength = tsduLength;
    }

    /// <summary>Gets the source port field.</summary>
    public ushort SourcePort { get; }

    /// <summary>Gets the destination port field.</summary>
    public ushort DestinationPort { get; }

    /// <summary>Gets the PGM packet type.</summary>
    public PgmPacketType Type { get; }

    /// <summary>Gets the fixed header options.</summary>
    public PgmHeaderOptions Options { get; }

    /// <summary>Gets the packet checksum.</summary>
    public ushort Checksum { get; }

    /// <summary>Gets the global source identifier.</summary>
    public PgmGlobalSourceId GlobalSourceId { get; }

    /// <summary>Gets the transport service data unit length.</summary>
    public ushort TsduLength { get; }

    /// <summary>Writes this header to a destination span.</summary>
    /// <param name="destination">The destination span.</param>
    /// <returns><see langword="true"/> when the header was written.</returns>
    public bool TryWrite(Span<byte> destination)
    {
        if (destination.Length < EncodedLength)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, SourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2), DestinationPort);
        destination[4] = (byte)Type;
        destination[5] = (byte)Options;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(6), Checksum);
        _ = GlobalSourceId.TryWrite(destination.Slice(8, PgmGlobalSourceId.EncodedLength));
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(14), TsduLength);
        return true;
    }

    /// <summary>Parses a fixed common PGM header from a source span.</summary>
    /// <param name="source">The source span.</param>
    /// <param name="header">The parsed header.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParse(ReadOnlySpan<byte> source, out PgmHeader header)
    {
        if (source.Length < EncodedLength)
        {
            header = default;
            return false;
        }

        var type = source[4];
        if ((type & 0xF0) != 0 || !PgmPacketConstants.IsKnownPacketType((PgmPacketType)type))
        {
            header = default;
            return false;
        }

        _ = PgmGlobalSourceId.TryParse(source.Slice(8), out var globalSourceId);
        header = new PgmHeader(
            BinaryPrimitives.ReadUInt16BigEndian(source),
            BinaryPrimitives.ReadUInt16BigEndian(source.Slice(2)),
            (PgmPacketType)type,
            (PgmHeaderOptions)source[5],
            BinaryPrimitives.ReadUInt16BigEndian(source.Slice(6)),
            globalSourceId,
            BinaryPrimitives.ReadUInt16BigEndian(source.Slice(14)));
        return true;
    }
}

/// <summary>Provides RFC constants used by PGM packet codecs.</summary>
public static class PgmPacketConstants
{
    /// <summary>The IP protocol number assigned to native PGM over IP.</summary>
    public const int IpProtocolNumber = 113;

    /// <summary>The well-known UDP port commonly used for PGM-over-UDP encapsulation.</summary>
    public const int UdpEncapsulationPort = 3055;

    /// <summary>The maximum number of option extensions allowed by RFC 3208.</summary>
    public const int MaximumOptionCount = 16;

    /// <summary>Determines whether a packet type is defined by RFC 3208.</summary>
    /// <param name="type">The packet type.</param>
    /// <returns><see langword="true"/> when the packet type is known.</returns>
    public static bool IsKnownPacketType(PgmPacketType type)
    {
        return type == PgmPacketType.SourcePathMessage
            || type == PgmPacketType.Poll
            || type == PgmPacketType.PollResponse
            || type == PgmPacketType.OriginalData
            || type == PgmPacketType.RepairData
            || type == PgmPacketType.NegativeAcknowledgment
            || type == PgmPacketType.NullNegativeAcknowledgment
            || type == PgmPacketType.NakConfirmation
            || type == PgmPacketType.SourcePathMessageRequest;
    }
}
