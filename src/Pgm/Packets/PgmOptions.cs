// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace Pgm.Packets;

/// <summary>Identifies an RFC 3208 PGM option extension type.</summary>
public enum PgmOptionType : byte
{
    /// <summary>Option extension length.</summary>
    Length = 0x00,

    /// <summary>Fragmentation option.</summary>
    Fragment = 0x01,

    /// <summary>NAK list option.</summary>
    NakList = 0x02,

    /// <summary>Forward error correction option.</summary>
    Fec = 0x08,

    /// <summary>Forward error correction parity parameter option.</summary>
    ParityParameters = Fec,

    /// <summary>Forward error correction parity group option.</summary>
    ParityGroup = 0x09,

    /// <summary>Invalid option marker.</summary>
    Invalid = 0x7F,
}

/// <summary>Represents the common PGM option extension header.</summary>
public sealed class PgmOptionHeader
{
    /// <summary>The encoded length of a PGM option extension common header.</summary>
    public const int EncodedLength = 4;

    /// <summary>Initializes a new instance of the <see cref="PgmOptionHeader"/> class.</summary>
    /// <param name="type">The option type.</param>
    /// <param name="length">The total option length.</param>
    /// <param name="flags">The option flags byte.</param>
    /// <param name="specific">The option-specific byte.</param>
    /// <param name="isLast">A value indicating whether the OPT_END bit is set.</param>
    public PgmOptionHeader(PgmOptionType type, byte length, byte flags, byte specific, bool isLast)
    {
        Type = type;
        Length = length;
        Flags = flags;
        Specific = specific;
        IsLast = isLast;
    }

    /// <summary>Gets the option type without the OPT_END bit.</summary>
    public PgmOptionType Type { get; }

    /// <summary>Gets the total option length.</summary>
    public byte Length { get; }

    /// <summary>Gets the reserved, FEC encoding, OPX, and encoded-null flags byte.</summary>
    public byte Flags { get; }

    /// <summary>Gets the option-specific byte.</summary>
    public byte Specific { get; }

    /// <summary>Gets a value indicating whether this option terminates the option list.</summary>
    public bool IsLast { get; }

    /// <summary>Writes this option header to a destination span.</summary>
    /// <param name="destination">The destination span.</param>
    /// <returns><see langword="true"/> when the header was written.</returns>
    public bool TryWrite(Span<byte> destination)
    {
        if (destination.Length < EncodedLength)
        {
            return false;
        }

        destination[0] = (byte)((byte)Type | (IsLast ? 0x80 : 0));
        destination[1] = Length;
        destination[2] = Flags;
        destination[3] = Specific;
        return true;
    }

    /// <summary>Parses an option header from a source span.</summary>
    /// <param name="source">The source span.</param>
    /// <param name="header">The parsed option header.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParse(ReadOnlySpan<byte> source, out PgmOptionHeader? header)
    {
        if (source.Length < EncodedLength)
        {
            header = null;
            return false;
        }

        var length = source[1];
        if (length < EncodedLength || source.Length < length)
        {
            header = null;
            return false;
        }

        header = new PgmOptionHeader(
            (PgmOptionType)(source[0] & 0x7F),
            length,
            source[2],
            source[3],
            (source[0] & 0x80) != 0);
        return true;
    }
}

/// <summary>Provides codecs for RFC 3208 PGM option extensions.</summary>
public static class PgmOptionCodec
{
    /// <summary>Writes an OPT_LENGTH option.</summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="totalOptionsLength">The total length of all options including OPT_LENGTH.</param>
    /// <param name="isLast">A value indicating whether this option ends the option list.</param>
    /// <returns><see langword="true"/> when the option was written.</returns>
    public static bool TryWriteLength(Span<byte> destination, ushort totalOptionsLength, bool isLast)
    {
        if (destination.Length < 4)
        {
            return false;
        }

        destination[0] = (byte)(isLast ? 0x80 : 0x00);
        destination[1] = 4;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2), totalOptionsLength);
        return true;
    }

    /// <summary>Writes an OPT_FRAGMENT option.</summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="firstSequenceNumber">The first sequence number in the APDU.</param>
    /// <param name="offset">The fragment offset.</param>
    /// <param name="length">The original APDU length.</param>
    /// <param name="isLast">A value indicating whether this option ends the option list.</param>
    /// <returns><see langword="true"/> when the option was written.</returns>
    public static bool TryWriteFragment(
        Span<byte> destination,
        uint firstSequenceNumber,
        uint offset,
        uint length,
        bool isLast)
    {
        if (!new PgmOptionHeader(PgmOptionType.Fragment, 16, 0, 0, isLast).TryWrite(destination))
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4), firstSequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8), offset);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(12), length);
        return true;
    }

    /// <summary>Writes an OPT_NAK_LIST option.</summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="sequenceNumbers">The additional requested sequence numbers.</param>
    /// <param name="isLast">A value indicating whether this option ends the option list.</param>
    /// <returns><see langword="true"/> when the option was written.</returns>
    public static bool TryWriteNakList(Span<byte> destination, ReadOnlySpan<uint> sequenceNumbers, bool isLast)
    {
        if (sequenceNumbers.Length > 62)
        {
            return false;
        }

        var length = 4 + (sequenceNumbers.Length * 4);
        if (length > byte.MaxValue || destination.Length < length)
        {
            return false;
        }

        _ = new PgmOptionHeader(PgmOptionType.NakList, (byte)length, 0, 0, isLast).TryWrite(destination);
        for (var i = 0; i < sequenceNumbers.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4 + (i * 4)), sequenceNumbers[i]);
        }

        return true;
    }

    /// <summary>Writes an OPT_PARITY_PRM option.</summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="transmissionGroupSize">The transmission group size.</param>
    /// <param name="proactiveParity">A value indicating whether proactive parity is available.</param>
    /// <param name="onDemandParity">A value indicating whether on-demand parity is available.</param>
    /// <param name="isLast">A value indicating whether this option ends the option list.</param>
    /// <returns><see langword="true"/> when the option was written.</returns>
    public static bool TryWriteParityParameters(
        Span<byte> destination,
        uint transmissionGroupSize,
        bool proactiveParity,
        bool onDemandParity,
        bool isLast)
    {
        if (!new PgmOptionHeader(PgmOptionType.ParityParameters, 8, 0, GetParitySpecificByte(
            proactiveParity,
            onDemandParity), isLast).TryWrite(destination))
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4), transmissionGroupSize);
        return true;
    }

    /// <summary>Writes an OPT_FEC option using the RFC 3208 OPT_PARITY_PRM format.</summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="transmissionGroupSize">The transmission group size.</param>
    /// <param name="proactiveParity">A value indicating whether proactive parity is available.</param>
    /// <param name="onDemandParity">A value indicating whether on-demand parity is available.</param>
    /// <param name="isLast">A value indicating whether this option ends the option list.</param>
    /// <returns><see langword="true"/> when the option was written.</returns>
    public static bool TryWriteFec(
        Span<byte> destination,
        uint transmissionGroupSize,
        bool proactiveParity,
        bool onDemandParity,
        bool isLast)
    {
        return TryWriteParityParameters(destination, transmissionGroupSize, proactiveParity, onDemandParity, isLast);
    }

    /// <summary>Writes an OPT_PARITY_GRP option.</summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="parityGroupNumber">The parity group number.</param>
    /// <param name="isLast">A value indicating whether this option ends the option list.</param>
    /// <returns><see langword="true"/> when the option was written.</returns>
    public static bool TryWriteParityGroup(Span<byte> destination, uint parityGroupNumber, bool isLast)
    {
        if (!new PgmOptionHeader(PgmOptionType.ParityGroup, 8, 0, 0, isLast).TryWrite(destination))
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4), parityGroupNumber);
        return true;
    }

    /// <summary>Reads an OPT_LENGTH value.</summary>
    /// <param name="option">The option bytes.</param>
    /// <param name="totalOptionsLength">The total option length.</param>
    /// <returns><see langword="true"/> when the option value was read.</returns>
    public static bool TryReadLength(ReadOnlySpan<byte> option, out ushort totalOptionsLength)
    {
        if (!PgmOptionHeader.TryParse(option, out var header) || header is null || header.Type != PgmOptionType.Length)
        {
            totalOptionsLength = 0;
            return false;
        }

        totalOptionsLength = BinaryPrimitives.ReadUInt16BigEndian(option.Slice(2));
        return header.Length == 4;
    }

    /// <summary>Reads an OPT_FRAGMENT value.</summary>
    /// <param name="option">The option bytes.</param>
    /// <param name="firstSequenceNumber">The first sequence number in the APDU.</param>
    /// <param name="offset">The fragment offset.</param>
    /// <param name="length">The original APDU length.</param>
    /// <returns><see langword="true"/> when the option value was read.</returns>
    public static bool TryReadFragment(
        ReadOnlySpan<byte> option,
        out uint firstSequenceNumber,
        out uint offset,
        out uint length)
    {
        if (!PgmOptionHeader.TryParse(option, out var header) || header is null
            || header.Type != PgmOptionType.Fragment || header.Length != 16)
        {
            firstSequenceNumber = 0;
            offset = 0;
            length = 0;
            return false;
        }

        firstSequenceNumber = BinaryPrimitives.ReadUInt32BigEndian(option.Slice(4));
        offset = BinaryPrimitives.ReadUInt32BigEndian(option.Slice(8));
        length = BinaryPrimitives.ReadUInt32BigEndian(option.Slice(12));
        return true;
    }

    /// <summary>Reads an OPT_PARITY_PRM value.</summary>
    /// <param name="option">The option bytes.</param>
    /// <param name="transmissionGroupSize">The transmission group size.</param>
    /// <param name="proactiveParity">A value indicating whether proactive parity is available.</param>
    /// <param name="onDemandParity">A value indicating whether on-demand parity is available.</param>
    /// <returns><see langword="true"/> when the option value was read.</returns>
    public static bool TryReadParityParameters(
        ReadOnlySpan<byte> option,
        out uint transmissionGroupSize,
        out bool proactiveParity,
        out bool onDemandParity)
    {
        if (!PgmOptionHeader.TryParse(option, out var header) || header is null
            || header.Type != PgmOptionType.ParityParameters || header.Length != 8)
        {
            transmissionGroupSize = 0;
            proactiveParity = false;
            onDemandParity = false;
            return false;
        }

        transmissionGroupSize = BinaryPrimitives.ReadUInt32BigEndian(option.Slice(4));
        proactiveParity = (header.Specific & 0x01) != 0;
        onDemandParity = (header.Specific & 0x02) != 0;
        return true;
    }

    /// <summary>Reads an OPT_FEC value using the RFC 3208 OPT_PARITY_PRM format.</summary>
    /// <param name="option">The option bytes.</param>
    /// <param name="transmissionGroupSize">The transmission group size.</param>
    /// <param name="proactiveParity">A value indicating whether proactive parity is available.</param>
    /// <param name="onDemandParity">A value indicating whether on-demand parity is available.</param>
    /// <returns><see langword="true"/> when the option value was read.</returns>
    public static bool TryReadFec(
        ReadOnlySpan<byte> option,
        out uint transmissionGroupSize,
        out bool proactiveParity,
        out bool onDemandParity)
    {
        return TryReadParityParameters(option, out transmissionGroupSize, out proactiveParity, out onDemandParity);
    }

    /// <summary>Reads an OPT_PARITY_GRP value.</summary>
    /// <param name="option">The option bytes.</param>
    /// <param name="parityGroupNumber">The parity group number.</param>
    /// <returns><see langword="true"/> when the option value was read.</returns>
    public static bool TryReadParityGroup(ReadOnlySpan<byte> option, out uint parityGroupNumber)
    {
        if (!PgmOptionHeader.TryParse(option, out var header) || header is null
            || header.Type != PgmOptionType.ParityGroup || header.Length != 8)
        {
            parityGroupNumber = 0;
            return false;
        }

        parityGroupNumber = BinaryPrimitives.ReadUInt32BigEndian(option.Slice(4));
        return true;
    }

    /// <summary>Calculates the fixed header option bits for an option extension block.</summary>
    /// <param name="options">The option extension block.</param>
    /// <returns>The fixed header option bits.</returns>
    public static PgmHeaderOptions GetHeaderOptions(ReadOnlySpan<byte> options)
    {
        if (options.Length == 0)
        {
            return PgmHeaderOptions.None;
        }

        var result = PgmHeaderOptions.OptionsPresent;
        var offset = 0;
        for (var count = 0; offset < options.Length && count < PgmPacketConstants.MaximumOptionCount; count++)
        {
            if (!PgmOptionHeader.TryParse(options.Slice(offset), out var header) || header is null)
            {
                break;
            }

            if (header.Type == PgmOptionType.NakList)
            {
                result |= PgmHeaderOptions.NetworkSignificant;
            }

            offset += header.Length;
            if (header.IsLast)
            {
                break;
            }
        }

        return result;
    }

    private static byte GetParitySpecificByte(bool proactiveParity, bool onDemandParity)
    {
        var value = 0;
        if (proactiveParity)
        {
            value |= 0x01;
        }

        if (onDemandParity)
        {
            value |= 0x02;
        }

        return (byte)value;
    }
}
