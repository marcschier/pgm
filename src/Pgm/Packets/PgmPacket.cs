// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace Pgm.Packets;

/// <summary>Identifies the body carried by a <see cref="PgmPacket"/>.</summary>
public enum PgmBodyKind : byte
{
    /// <summary>An unknown or unset body.</summary>
    None = 0,

    /// <summary>A Source Path Message body.</summary>
    SourcePathMessage,

    /// <summary>An ODATA or RDATA body.</summary>
    Data,

    /// <summary>A NAK, NNAK, or NCF body.</summary>
    NakLike,

    /// <summary>A Source Path Message request body.</summary>
    SourcePathMessageRequest,

    /// <summary>A POLL body.</summary>
    Poll,

    /// <summary>A POLR body.</summary>
    PollResponse,
}

/// <summary>Represents a parsed or encodable PGM packet as an allocation-free span view.</summary>
public readonly ref struct PgmPacket
{
    private readonly ReadOnlySpan<byte> _options;
    private readonly ReadOnlySpan<byte> _data;
    private readonly PgmSourcePathMessage _spm;
    private readonly PgmNakPacket _nak;
    private readonly PgmPollPacket _poll;
    private readonly PgmPollResponsePacket _polr;
    private readonly uint _dataSequence;
    private readonly uint _dataTrailingEdge;

    private PgmPacket(
        PgmHeader header,
        PgmBodyKind kind,
        ReadOnlySpan<byte> options,
        ReadOnlySpan<byte> data,
        uint dataSequence,
        uint dataTrailingEdge,
        PgmSourcePathMessage spm,
        PgmNakPacket nak,
        PgmPollPacket poll,
        PgmPollResponsePacket polr)
    {
        Header = header;
        Kind = kind;
        _options = options;
        _data = data;
        _dataSequence = dataSequence;
        _dataTrailingEdge = dataTrailingEdge;
        _spm = spm;
        _nak = nak;
        _poll = poll;
        _polr = polr;
    }

    /// <summary>Gets the fixed common PGM header.</summary>
    public PgmHeader Header { get; }

    /// <summary>Gets the kind of body carried by this packet.</summary>
    public PgmBodyKind Kind { get; }

    /// <summary>Gets the encoded option extension bytes.</summary>
    public ReadOnlySpan<byte> Options => _options;

    /// <summary>Gets the encoded length of this packet.</summary>
    public int EncodedLength => PgmHeader.EncodedLength + GetBodyPrefixLength() + _options.Length + _data.Length;

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
        return new PgmPacket(
            header, PgmBodyKind.SourcePathMessage, options, default, 0, 0, body, default, default, default);
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

        return new PgmPacket(
            header,
            PgmBodyKind.Data,
            options,
            body.Data,
            body.SequenceNumber,
            body.TrailingEdgeSequenceNumber,
            default,
            default,
            default,
            default);
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

        return new PgmPacket(header, PgmBodyKind.NakLike, options, default, 0, 0, default, body, default, default);
    }

    /// <summary>Creates an SPMR packet.</summary>
    /// <param name="header">The fixed header.</param>
    /// <param name="options">The encoded option extensions.</param>
    /// <returns>The packet.</returns>
    public static PgmPacket CreateSourcePathMessageRequest(PgmHeader header, ReadOnlySpan<byte> options)
    {
        ValidateType(header, PgmPacketType.SourcePathMessageRequest);
        return new PgmPacket(
            header,
            PgmBodyKind.SourcePathMessageRequest,
            options,
            default,
            0,
            0,
            default,
            default,
            default,
            default);
    }

    /// <summary>Creates a POLL packet.</summary>
    /// <param name="header">The fixed header.</param>
    /// <param name="body">The POLL body.</param>
    /// <param name="options">The encoded option extensions.</param>
    /// <returns>The packet.</returns>
    public static PgmPacket CreatePoll(PgmHeader header, PgmPollPacket body, ReadOnlySpan<byte> options)
    {
        ValidateType(header, PgmPacketType.Poll);
        return new PgmPacket(header, PgmBodyKind.Poll, options, default, 0, 0, default, default, body, default);
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
        return new PgmPacket(header, PgmBodyKind.PollResponse, options, default, 0, 0, default, default, default, body);
    }

    /// <summary>Copies the option extension bytes into a new array.</summary>
    /// <returns>The option extension bytes.</returns>
    public byte[] GetOptionBytes() => _options.ToArray();

    /// <summary>Gets the data body when this packet carries ODATA or RDATA.</summary>
    /// <param name="body">The parsed data body.</param>
    /// <returns><see langword="true"/> when the body is data.</returns>
    public bool TryGetData(out PgmDataPacket body)
    {
        if (Kind == PgmBodyKind.Data)
        {
            body = new PgmDataPacket(_dataSequence, _dataTrailingEdge, _data);
            return true;
        }

        body = default;
        return false;
    }

    /// <summary>Gets the SPM body when this packet carries an SPM.</summary>
    /// <param name="body">The parsed SPM body.</param>
    /// <returns><see langword="true"/> when the body is an SPM.</returns>
    public bool TryGetSourcePathMessage(out PgmSourcePathMessage body)
    {
        body = _spm;
        return Kind == PgmBodyKind.SourcePathMessage;
    }

    /// <summary>Gets the NAK-like body when this packet carries one.</summary>
    /// <param name="body">The parsed NAK-like body.</param>
    /// <returns><see langword="true"/> when the body is NAK, NNAK, or NCF.</returns>
    public bool TryGetNak(out PgmNakPacket body)
    {
        body = _nak;
        return Kind == PgmBodyKind.NakLike;
    }

    /// <summary>Gets the POLL body when this packet carries one.</summary>
    /// <param name="body">The parsed POLL body.</param>
    /// <returns><see langword="true"/> when the body is a POLL.</returns>
    public bool TryGetPoll(out PgmPollPacket body)
    {
        body = _poll;
        return Kind == PgmBodyKind.Poll;
    }

    /// <summary>Gets the POLR body when this packet carries one.</summary>
    /// <param name="body">The parsed POLR body.</param>
    /// <returns><see langword="true"/> when the body is a POLR.</returns>
    public bool TryGetPollResponse(out PgmPollResponsePacket body)
    {
        body = _polr;
        return Kind == PgmBodyKind.PollResponse;
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
        if (Kind == PgmBodyKind.Data)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset), _dataSequence);
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset + 4), _dataTrailingEdge);
            offset += 8;
            _options.CopyTo(destination.Slice(offset));
            offset += _options.Length;
            _data.CopyTo(destination.Slice(offset));
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
    public static bool TryParse(ReadOnlySpan<byte> source, out PgmPacket packet)
    {
        if (!PgmHeader.TryParse(source, out var header))
        {
            packet = default;
            return false;
        }

        return TryParseBodyAndOptions(header, source.Slice(PgmHeader.EncodedLength), out packet);
    }

    private static bool TryParseBodyAndOptions(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket packet)
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
                return TryParseSpmr(header, source, out packet);
            case PgmPacketType.Poll:
                return TryParsePoll(header, source, out packet);
            case PgmPacketType.PollResponse:
                return TryParsePollResponse(header, source, out packet);
            default:
                packet = default;
                return false;
        }
    }

    private static bool TryParseSpm(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket packet)
    {
        if (!PgmSourcePathMessage.TryParseBody(source, out var body, out var bodyLength)
            || !TryReadOptions(header, source.Slice(bodyLength), out var optionLength))
        {
            packet = default;
            return false;
        }

        packet = CreateSourcePathMessage(header, body, source.Slice(bodyLength, optionLength));
        return true;
    }

    private static bool TryParseData(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket packet)
    {
        if (source.Length < 8 || !TryReadOptions(header, source.Slice(8), out var optionLength))
        {
            packet = default;
            return false;
        }

        var dataStart = 8 + optionLength;
        if (source.Length < dataStart + header.TsduLength)
        {
            packet = default;
            return false;
        }

        var body = new PgmDataPacket(
            BinaryPrimitives.ReadUInt32BigEndian(source),
            BinaryPrimitives.ReadUInt32BigEndian(source.Slice(4)),
            source.Slice(dataStart, header.TsduLength));
        packet = CreateData(header, body, source.Slice(8, optionLength));
        return true;
    }

    private static bool TryParseNakLike(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket packet)
    {
        if (!PgmNakPacket.TryParseBody(source, out var body, out var bodyLength)
            || !TryReadOptions(header, source.Slice(bodyLength), out var optionLength))
        {
            packet = default;
            return false;
        }

        packet = CreateNakLike(header, body, source.Slice(bodyLength, optionLength));
        return true;
    }

    private static bool TryParseSpmr(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket packet)
    {
        if (!TryReadOptions(header, source, out var optionLength))
        {
            packet = default;
            return false;
        }

        packet = CreateSourcePathMessageRequest(header, source.Slice(0, optionLength));
        return true;
    }

    private static bool TryParsePoll(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket packet)
    {
        if (!PgmPollPacket.TryParseBody(source, out var body, out var bodyLength)
            || !TryReadOptions(header, source.Slice(bodyLength), out var optionLength))
        {
            packet = default;
            return false;
        }

        packet = CreatePoll(header, body, source.Slice(bodyLength, optionLength));
        return true;
    }

    private static bool TryParsePollResponse(PgmHeader header, ReadOnlySpan<byte> source, out PgmPacket packet)
    {
        if (!PgmPollResponsePacket.TryParseBody(source, out var body, out var bodyLength)
            || !TryReadOptions(header, source.Slice(bodyLength), out var optionLength))
        {
            packet = default;
            return false;
        }

        packet = CreatePollResponse(header, body, source.Slice(bodyLength, optionLength));
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
            if (!PgmOptionHeader.TryParse(source.Slice(offset, totalLength - offset), out var option) || option is null)
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
        return Kind switch
        {
            PgmBodyKind.SourcePathMessage => _spm.BodyLength,
            PgmBodyKind.Data => 8,
            PgmBodyKind.NakLike => _nak.BodyLength,
            PgmBodyKind.Poll => _poll.BodyLength,
            PgmBodyKind.PollResponse => _polr.BodyLength,
            _ => 0,
        };
    }

    private bool TryWriteNonDataBody(Span<byte> destination, out int bodyLength)
    {
        switch (Kind)
        {
            case PgmBodyKind.SourcePathMessage:
                bodyLength = _spm.BodyLength;
                return _spm.TryWriteBody(destination);
            case PgmBodyKind.NakLike:
                bodyLength = _nak.BodyLength;
                return _nak.TryWriteBody(destination);
            case PgmBodyKind.SourcePathMessageRequest:
                bodyLength = 0;
                return true;
            case PgmBodyKind.Poll:
                bodyLength = _poll.BodyLength;
                return _poll.TryWriteBody(destination);
            case PgmBodyKind.PollResponse:
                bodyLength = _polr.BodyLength;
                return _polr.TryWriteBody(destination);
            default:
                bodyLength = 0;
                return false;
        }
    }
}
