// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace Pgm.Packets;

/// <summary>Represents a parsed or encodable PGM packet.</summary>
public sealed class PgmPacket
{
    private readonly byte[] _options;

    private PgmPacket(PgmHeader header, object body, ReadOnlySpan<byte> options)
    {
        Header = header;
        Body = body;
        _options = options.ToArray();
    }

    /// <summary>Gets the fixed common PGM header.</summary>
    public PgmHeader Header { get; }

    /// <summary>Gets the packet-specific body.</summary>
    public object Body { get; }

    /// <summary>Gets the encoded length of this packet.</summary>
    public int EncodedLength => PgmHeader.EncodedLength + GetBodyPrefixLength() + _options.Length + GetDataLength();

    /// <summary>Creates an SPM packet.</summary>
    /// <param name="header">The fixed header.</param>
    /// <param name="body">The SPM body.</param>
    /// <param name="options">The encoded option extensions.</param>
    /// <returns>The packet.</returns>
    public static PgmPacket CreateSourcePathMessage(
        PgmHeader header,
        PgmSourcePathMessage body,
        ReadOnlySpan<byte> options)
    {
        ValidateType(header, PgmPacketType.SourcePathMessage);
        return new PgmPacket(header, body, options);
    }

    /// <summary>Creates an ODATA or RDATA packet.</summary>
    /// <param name="header">The fixed header.</param>
    /// <param name="body">The data body.</param>
    /// <param name="options">The encoded option extensions.</param>
    /// <returns>The packet.</returns>
    public static PgmPacket CreateData(PgmHeader header, PgmDataPacket body, ReadOnlySpan<byte> options)
    {
        if (header.Type != PgmPacketType.OriginalData && header.Type != PgmPacketType.RepairData)
        {
            throw new ArgumentException("The header type must be ODATA or RDATA.", nameof(header));
        }

        if (header.TsduLength != body.TsduLength)
        {
            throw new ArgumentException("The header TSDU length must match the data length.", nameof(header));
        }

        return new PgmPacket(header, body, options);
    }

    /// <summary>Creates a NAK, NNAK, or NCF packet.</summary>
    /// <param name="header">The fixed header.</param>
    /// <param name="body">The NAK-like body.</param>
    /// <param name="options">The encoded option extensions.</param>
    /// <returns>The packet.</returns>
    public static PgmPacket CreateNakLike(PgmHeader header, PgmNakPacket body, ReadOnlySpan<byte> options)
    {
        if (header.Type != PgmPacketType.NegativeAcknowledgment
            && header.Type != PgmPacketType.NullNegativeAcknowledgment
            && header.Type != PgmPacketType.NakConfirmation)
        {
            throw new ArgumentException("The header type must be NAK, NNAK, or NCF.", nameof(header));
        }

        return new PgmPacket(header, body, options);
    }

    /// <summary>Creates an SPMR packet.</summary>
    /// <param name="header">The fixed header.</param>
    /// <param name="options">The encoded option extensions.</param>
    /// <returns>The packet.</returns>
    public static PgmPacket CreateSourcePathMessageRequest(PgmHeader header, ReadOnlySpan<byte> options)
    {
        ValidateType(header, PgmPacketType.SourcePathMessageRequest);
        return new PgmPacket(header, new PgmSourcePathMessageRequest(), options);
    }

    /// <summary>Creates a POLL packet.</summary>
    /// <param name="header">The fixed header.</param>
    /// <param name="body">The POLL body.</param>
    /// <param name="options">The encoded option extensions.</param>
    /// <returns>The packet.</returns>
    public static PgmPacket CreatePoll(PgmHeader header, PgmPollPacket body, ReadOnlySpan<byte> options)
    {
        ValidateType(header, PgmPacketType.Poll);
        return new PgmPacket(header, body, options);
    }

    /// <summary>Creates a POLR packet.</summary>
    /// <param name="header">The fixed header.</param>
    /// <param name="body">The POLR body.</param>
    /// <param name="options">The encoded option extensions.</param>
    /// <returns>The packet.</returns>
    public static PgmPacket CreatePollResponse(
        PgmHeader header,
        PgmPollResponsePacket body,
        ReadOnlySpan<byte> options)
    {
        ValidateType(header, PgmPacketType.PollResponse);
        return new PgmPacket(header, body, options);
    }

    /// <summary>Copies the option extension bytes into a new array.</summary>
    /// <returns>The option extension bytes.</returns>
    public byte[] GetOptionBytes()
    {
        var copy = new byte[_options.Length];
        _options.CopyTo(copy, 0);
        return copy;
    }

    /// <summary>Writes this packet to a destination span.</summary>
    /// <param name="destination">The destination span.</param>
    /// <returns><see langword="true"/> when the packet was written.</returns>
    public bool TryWrite(Span<byte> destination)
    {
        if (destination.Length < EncodedLength || !Header.TryWrite(destination))
        {
            return false;
        }

        var offset = PgmHeader.EncodedLength;
        if (Body is PgmDataPacket data)
        {
            if (!data.TryWriteBodyPrefix(destination.Slice(offset)))
            {
                return false;
            }

            offset += 8;
            _options.CopyTo(destination.Slice(offset));
            offset += _options.Length;
            data.CopyDataTo(destination.Slice(offset));
            return true;
        }

        if (!TryWriteNonDataBody(destination.Slice(offset), out var bodyLength))
        {
            return false;
        }

        offset += bodyLength;
        _options.CopyTo(destination.Slice(offset));
        return true;
    }

    /// <summary>Parses a PGM packet from a source span.</summary>
    /// <param name="source">The source span.</param>
    /// <param name="packet">The parsed packet.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParse(ReadOnlySpan<byte> source, out PgmPacket? packet)
    {
        if (!PgmHeader.TryParse(source, out var header) || header is null)
        {
            packet = null;
            return false;
        }

        return TryParseBodyAndOptions(header, source.Slice(PgmHeader.EncodedLength), out packet);
    }

    private static bool TryParseBodyAndOptions(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket? packet)
    {
        switch (header.Type)
        {
            case PgmPacketType.SourcePathMessage:
                return TryParseSpm(header, source, out packet);
            case PgmPacketType.OriginalData:
            case PgmPacketType.RepairData:
                return TryParseData(header, source, out packet);
            case PgmPacketType.NegativeAcknowledgment:
            case PgmPacketType.NullNegativeAcknowledgment:
            case PgmPacketType.NakConfirmation:
                return TryParseNakLike(header, source, out packet);
            case PgmPacketType.SourcePathMessageRequest:
                return TryCreateWithOptions(header, new PgmSourcePathMessageRequest(), source, out packet);
            case PgmPacketType.Poll:
                return TryParsePoll(header, source, out packet);
            case PgmPacketType.PollResponse:
                return TryParsePollResponse(header, source, out packet);
            default:
                packet = null;
                return false;
        }
    }

    private static bool TryParseSpm(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket? packet)
    {
        if (!PgmSourcePathMessage.TryParseBody(source, out var body, out var bodyLength) || body is null)
        {
            packet = null;
            return false;
        }

        return TryCreateWithOptions(header, body, source.Slice(bodyLength), out packet);
    }

    private static bool TryParseData(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket? packet)
    {
        if (source.Length < 8 || !TryReadOptions(header, source.Slice(8), out var optionLength))
        {
            packet = null;
            return false;
        }

        var dataStart = 8 + optionLength;
        if (source.Length < dataStart + header.TsduLength)
        {
            packet = null;
            return false;
        }

        var body = new PgmDataPacket(
            BinaryPrimitives.ReadUInt32BigEndian(source),
            BinaryPrimitives.ReadUInt32BigEndian(source.Slice(4)),
            source.Slice(dataStart, header.TsduLength));
        packet = new PgmPacket(header, body, source.Slice(8, optionLength));
        return true;
    }

    private static bool TryParseNakLike(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket? packet)
    {
        if (!PgmNakPacket.TryParseBody(source, out var body, out var bodyLength) || body is null)
        {
            packet = null;
            return false;
        }

        return TryCreateWithOptions(header, body, source.Slice(bodyLength), out packet);
    }

    private static bool TryParsePoll(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket? packet)
    {
        if (!PgmPollPacket.TryParseBody(source, out var body, out var bodyLength) || body is null)
        {
            packet = null;
            return false;
        }

        return TryCreateWithOptions(header, body, source.Slice(bodyLength), out packet);
    }

    private static bool TryParsePollResponse(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket? packet)
    {
        if (!PgmPollResponsePacket.TryParseBody(source, out var body, out var bodyLength) || body is null)
        {
            packet = null;
            return false;
        }

        return TryCreateWithOptions(header, body, source.Slice(bodyLength), out packet);
    }

    private static bool TryCreateWithOptions(
        PgmHeader header,
        object body,
        ReadOnlySpan<byte> source,
        out PgmPacket? packet)
    {
        if (!TryReadOptions(header, source, out var optionLength))
        {
            packet = null;
            return false;
        }

        packet = new PgmPacket(header, body, source.Slice(0, optionLength));
        return true;
    }

    private static bool TryReadOptions(PgmHeader header, ReadOnlySpan<byte> source, out int optionLength)
    {
        if ((header.Options & PgmHeaderOptions.OptionsPresent) == 0)
        {
            optionLength = 0;
            return true;
        }

        if (source.Length < 4 || !PgmOptionCodec.TryReadLength(source, out var totalLength) || totalLength < 4
            || source.Length < totalLength)
        {
            optionLength = 0;
            return false;
        }

        var offset = 0;
        var foundEnd = false;
        for (var count = 0; offset < totalLength && count < PgmPacketConstants.MaximumOptionCount; count++)
        {
            if (!PgmOptionHeader.TryParse(source.Slice(offset, totalLength - offset), out var option)
                || option is null)
            {
                optionLength = 0;
                return false;
            }

            offset += option.Length;
            if (option.IsLast)
            {
                foundEnd = true;
                break;
            }
        }

        optionLength = totalLength;
        return foundEnd && offset == totalLength;
    }

    private static void ValidateType(PgmHeader header, PgmPacketType expectedType)
    {
        if (header.Type != expectedType)
        {
            throw new ArgumentException("The header type does not match the packet body.", nameof(header));
        }
    }

    private int GetBodyPrefixLength()
    {
        if (Body is PgmSourcePathMessage spm)
        {
            return spm.BodyLength;
        }

        if (Body is PgmDataPacket)
        {
            return 8;
        }

        if (Body is PgmNakPacket nak)
        {
            return nak.BodyLength;
        }

        if (Body is PgmPollPacket poll)
        {
            return poll.BodyLength;
        }

        if (Body is PgmPollResponsePacket polr)
        {
            return polr.BodyLength;
        }

        return 0;
    }

    private int GetDataLength()
    {
        return Body is PgmDataPacket data ? data.TsduLength : 0;
    }

    private bool TryWriteNonDataBody(Span<byte> destination, out int bodyLength)
    {
        if (Body is PgmSourcePathMessage spm)
        {
            bodyLength = spm.BodyLength;
            return spm.TryWriteBody(destination);
        }

        if (Body is PgmNakPacket nak)
        {
            bodyLength = nak.BodyLength;
            return nak.TryWriteBody(destination);
        }

        if (Body is PgmSourcePathMessageRequest spmr)
        {
            bodyLength = spmr.BodyLength;
            return spmr.TryWriteBody(destination);
        }

        if (Body is PgmPollPacket poll)
        {
            bodyLength = poll.BodyLength;
            return poll.TryWriteBody(destination);
        }

        if (Body is PgmPollResponsePacket polr)
        {
            bodyLength = polr.BodyLength;
            return polr.TryWriteBody(destination);
        }

        bodyLength = 0;
        return false;
    }
}
