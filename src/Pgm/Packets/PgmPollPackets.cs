// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace Pgm.Packets;

/// <summary>Represents a POLL body.</summary>
public sealed class PgmPollPacket
{
    /// <summary>Initializes a new instance of the <see cref="PgmPollPacket"/> class.</summary>
    /// <param name="sequenceNumber">The poll sequence number.</param>
    /// <param name="round">The poll round.</param>
    /// <param name="subType">The poll subtype.</param>
    /// <param name="path">The poll path network-layer address.</param>
    /// <param name="backoffInterval">The poll back-off interval, in microseconds.</param>
    /// <param name="randomString">The poll random string.</param>
    /// <param name="mask">The poll matching bit-mask.</param>
    public PgmPollPacket(
        uint sequenceNumber,
        ushort round,
        ushort subType,
        PgmNetworkAddress path,
        uint backoffInterval,
        uint randomString,
        uint mask)
    {
        SequenceNumber = sequenceNumber;
        Round = round;
        SubType = subType;
        Path = path ?? throw new ArgumentNullException(nameof(path));
        BackoffInterval = backoffInterval;
        RandomString = randomString;
        Mask = mask;
    }

    /// <summary>Gets the poll sequence number.</summary>
    public uint SequenceNumber { get; }

    /// <summary>Gets the poll round.</summary>
    public ushort Round { get; }

    /// <summary>Gets the poll subtype.</summary>
    public ushort SubType { get; }

    /// <summary>Gets the poll path network-layer address.</summary>
    public PgmNetworkAddress Path { get; }

    /// <summary>Gets the poll back-off interval, in microseconds.</summary>
    public uint BackoffInterval { get; }

    /// <summary>Gets the poll random string.</summary>
    public uint RandomString { get; }

    /// <summary>Gets the poll matching bit-mask.</summary>
    public uint Mask { get; }

    /// <summary>Gets the encoded length of this body.</summary>
    public int BodyLength => 20 + Path.EncodedLength;

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
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(4), Round);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(6), SubType);
        if (!Path.TryWrite(destination.Slice(8)))
        {
            return false;
        }

        var offset = 8 + Path.EncodedLength;
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset), BackoffInterval);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset + 4), RandomString);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset + 8), Mask);
        return true;
    }

    /// <summary>Parses a POLL body from a source span.</summary>
    /// <param name="source">The source span.</param>
    /// <param name="body">The parsed body.</param>
    /// <param name="bytesRead">The number of bytes consumed.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParseBody(ReadOnlySpan<byte> source, out PgmPollPacket? body, out int bytesRead)
    {
        if (source.Length < 24 || !PgmNetworkAddress.TryParse(source.Slice(8), out var path, out var pathLength)
            || path is null)
        {
            body = null;
            bytesRead = 0;
            return false;
        }

        var offset = 8 + pathLength;
        if (source.Length < offset + 12)
        {
            body = null;
            bytesRead = 0;
            return false;
        }

        body = new PgmPollPacket(
            BinaryPrimitives.ReadUInt32BigEndian(source),
            BinaryPrimitives.ReadUInt16BigEndian(source.Slice(4)),
            BinaryPrimitives.ReadUInt16BigEndian(source.Slice(6)),
            path,
            BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset)),
            BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset + 4)),
            BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset + 8)));
        bytesRead = offset + 12;
        return true;
    }
}

/// <summary>Represents a POLR body.</summary>
public sealed class PgmPollResponsePacket
{
    /// <summary>Initializes a new instance of the <see cref="PgmPollResponsePacket"/> class.</summary>
    /// <param name="sequenceNumber">The poll sequence number.</param>
    /// <param name="round">The poll round.</param>
    public PgmPollResponsePacket(uint sequenceNumber, ushort round)
    {
        SequenceNumber = sequenceNumber;
        Round = round;
    }

    /// <summary>Gets the poll sequence number.</summary>
    public uint SequenceNumber { get; }

    /// <summary>Gets the poll round.</summary>
    public ushort Round { get; }

    /// <summary>Gets the encoded length of this body.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The packet body model exposes instance lengths consistently.")]
    public int BodyLength => 8;

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
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(4), Round);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(6), 0);
        return true;
    }

    /// <summary>Parses a POLR body from a source span.</summary>
    /// <param name="source">The source span.</param>
    /// <param name="body">The parsed body.</param>
    /// <param name="bytesRead">The number of bytes consumed.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParseBody(ReadOnlySpan<byte> source, out PgmPollResponsePacket? body, out int bytesRead)
    {
        if (source.Length < 8)
        {
            body = null;
            bytesRead = 0;
            return false;
        }

        body = new PgmPollResponsePacket(
            BinaryPrimitives.ReadUInt32BigEndian(source),
            BinaryPrimitives.ReadUInt16BigEndian(source.Slice(4)));
        bytesRead = 8;
        return true;
    }
}
