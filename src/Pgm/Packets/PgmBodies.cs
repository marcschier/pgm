// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace Pgm.Packets;

/// <summary>Represents an RFC 3208 Source Path Message body.</summary>
public readonly struct PgmSourcePathMessage
{
    /// <summary>Initializes a new instance of the <see cref="PgmSourcePathMessage"/> struct.</summary>
    /// <param name="sequenceNumber">The SPM sequence number.</param>
    /// <param name="trailingEdgeSequenceNumber">The trailing edge sequence number.</param>
    /// <param name="leadingEdgeSequenceNumber">The leading edge sequence number.</param>
    /// <param name="path">The path network-layer address.</param>
    public PgmSourcePathMessage(
        uint sequenceNumber,
        uint trailingEdgeSequenceNumber,
        uint leadingEdgeSequenceNumber,
        PgmNetworkAddress path)
    {
        SequenceNumber = sequenceNumber;
        TrailingEdgeSequenceNumber = trailingEdgeSequenceNumber;
        LeadingEdgeSequenceNumber = leadingEdgeSequenceNumber;
        Path = path;
    }

    /// <summary>Gets the SPM sequence number.</summary>
    public uint SequenceNumber { get; }

    /// <summary>Gets the trailing edge sequence number.</summary>
    public uint TrailingEdgeSequenceNumber { get; }

    /// <summary>Gets the leading edge sequence number.</summary>
    public uint LeadingEdgeSequenceNumber { get; }

    /// <summary>Gets the path network-layer address.</summary>
    public PgmNetworkAddress Path { get; }

    /// <summary>Gets the encoded length of this body.</summary>
    public int BodyLength => 12 + Path.EncodedLength;

    /// <summary>Writes this body to a destination span.</summary>
    /// <param name="destination">The destination span.</param>
    /// <returns><see langword="true"/> when the body was written.</returns>
    public bool TryWriteBody(Span<byte> destination)
    {
        if (destination.Length < BodyLength)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, SequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4), TrailingEdgeSequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8), LeadingEdgeSequenceNumber);
        return Path.TryWrite(destination.Slice(12));
    }

    /// <summary>Parses a Source Path Message body from a source span.</summary>
    /// <param name="source">The source span.</param>
    /// <param name="body">The parsed body.</param>
    /// <param name="bytesRead">The number of bytes consumed.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParseBody(
        ReadOnlySpan<byte> source,
        out PgmSourcePathMessage body,
        out int bytesRead)
    {
        if (source.Length < 16 || !PgmNetworkAddress.TryParse(source.Slice(12), out var path, out var pathLength))
        {
            body = default;
            bytesRead = 0;
            return false;
        }

        body = new PgmSourcePathMessage(
            BinaryPrimitives.ReadUInt32BigEndian(source),
            BinaryPrimitives.ReadUInt32BigEndian(source.Slice(4)),
            BinaryPrimitives.ReadUInt32BigEndian(source.Slice(8)),
            path);
        bytesRead = 12 + pathLength;
        return true;
    }
}

/// <summary>Represents an ODATA or RDATA body.</summary>
public readonly ref struct PgmDataPacket
{
    /// <summary>Initializes a new instance of the <see cref="PgmDataPacket"/> struct.</summary>
    /// <param name="sequenceNumber">The data sequence number.</param>
    /// <param name="trailingEdgeSequenceNumber">The trailing edge sequence number.</param>
    /// <param name="data">The transport service data unit bytes.</param>
    public PgmDataPacket(uint sequenceNumber, uint trailingEdgeSequenceNumber, ReadOnlySpan<byte> data)
    {
        if (data.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        SequenceNumber = sequenceNumber;
        TrailingEdgeSequenceNumber = trailingEdgeSequenceNumber;
        Data = data;
    }

    /// <summary>Gets the data sequence number.</summary>
    public uint SequenceNumber { get; }

    /// <summary>Gets the trailing edge sequence number.</summary>
    public uint TrailingEdgeSequenceNumber { get; }

    /// <summary>Gets the transport service data unit bytes.</summary>
    public ReadOnlySpan<byte> Data { get; }

    /// <summary>Gets the transport service data unit length.</summary>
    public ushort TsduLength => (ushort)Data.Length;

    /// <summary>Gets the encoded length of this body and its data.</summary>
    public int BodyLength => 8 + Data.Length;

    /// <summary>Copies the transport service data unit into a new array.</summary>
    /// <returns>The transport service data unit bytes.</returns>
    public byte[] GetDataBytes() => Data.ToArray();

    /// <summary>Copies the transport service data unit to a destination span.</summary>
    /// <param name="destination">The destination span.</param>
    public void CopyDataTo(Span<byte> destination) => Data.CopyTo(destination);

    /// <summary>Writes this body prefix without TSDU data.</summary>
    /// <param name="destination">The destination span.</param>
    /// <returns><see langword="true"/> when the body prefix was written.</returns>
    public bool TryWriteBodyPrefix(Span<byte> destination)
    {
        if (destination.Length < 8)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, SequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4), TrailingEdgeSequenceNumber);
        return true;
    }
}

/// <summary>Represents a NAK, NNAK, or NCF body.</summary>
public readonly struct PgmNakPacket
{
    /// <summary>Initializes a new instance of the <see cref="PgmNakPacket"/> struct.</summary>
    /// <param name="sequenceNumber">The requested sequence number.</param>
    /// <param name="source">The source network-layer address.</param>
    /// <param name="group">The multicast group network-layer address.</param>
    public PgmNakPacket(uint sequenceNumber, PgmNetworkAddress source, PgmNetworkAddress group)
    {
        SequenceNumber = sequenceNumber;
        Source = source;
        Group = group;
    }

    /// <summary>Gets the requested sequence number.</summary>
    public uint SequenceNumber { get; }

    /// <summary>Gets the source network-layer address.</summary>
    public PgmNetworkAddress Source { get; }

    /// <summary>Gets the multicast group network-layer address.</summary>
    public PgmNetworkAddress Group { get; }

    /// <summary>Gets the encoded length of this body.</summary>
    public int BodyLength => 4 + Source.EncodedLength + Group.EncodedLength;

    /// <summary>Writes this body to a destination span.</summary>
    /// <param name="destination">The destination span.</param>
    /// <returns><see langword="true"/> when the body was written.</returns>
    public bool TryWriteBody(Span<byte> destination)
    {
        if (destination.Length < BodyLength)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, SequenceNumber);
        return Source.TryWrite(destination.Slice(4)) && Group.TryWrite(destination.Slice(4 + Source.EncodedLength));
    }

    /// <summary>Parses a NAK-like body from a source span.</summary>
    /// <param name="source">The source span.</param>
    /// <param name="body">The parsed body.</param>
    /// <param name="bytesRead">The number of bytes consumed.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParseBody(ReadOnlySpan<byte> source, out PgmNakPacket body, out int bytesRead)
    {
        if (source.Length < 12
            || !PgmNetworkAddress.TryParse(source.Slice(4), out var sourceAddress, out var sourceLength))
        {
            body = default;
            bytesRead = 0;
            return false;
        }

        var groupStart = 4 + sourceLength;
        if (!PgmNetworkAddress.TryParse(source.Slice(groupStart), out var groupAddress, out var groupLength))
        {
            body = default;
            bytesRead = 0;
            return false;
        }

        body = new PgmNakPacket(BinaryPrimitives.ReadUInt32BigEndian(source), sourceAddress, groupAddress);
        bytesRead = groupStart + groupLength;
        return true;
    }
}

/// <summary>Represents an SPMR body.</summary>
public readonly struct PgmSourcePathMessageRequest
{
    /// <summary>Gets the encoded length of this body.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The packet body model exposes instance lengths consistently.")]
    public int BodyLength => 0;

    /// <summary>Writes this body to a destination span.</summary>
    /// <param name="destination">The destination span.</param>
    /// <returns><see langword="true"/> when the body was written.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The packet body model exposes instance codecs consistently.")]
    public bool TryWriteBody(Span<byte> destination)
    {
        _ = destination;
        return true;
    }
}
