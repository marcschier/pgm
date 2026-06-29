// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Receiver;

internal readonly struct OptionDetails
{
    public bool HasFragment { get; private init; }

    public uint FragmentFirstSequence { get; private init; }

    public uint FragmentOffset { get; private init; }

    public uint FragmentLength { get; private init; }

    public bool HasFec { get; private init; }

    public uint TransmissionGroupSize { get; private init; }

    public bool HasParityGroup { get; private init; }

    public uint ParityGroupNumber { get; private init; }

    public static OptionDetails Parse(PgmPacket packet)
    {
        OptionDetails details = default;
        byte[] options = packet.GetOptionBytes();
        int offset = 0;

        for (int count = 0; offset < options.Length && count < PgmPacketConstants.MaximumOptionCount; count++)
        {
            if (!PgmOptionHeader.TryParse(options.AsSpan(offset), out var header) || header is null)
            {
                break;
            }

            ReadOnlySpan<byte> option = options.AsSpan(offset, header.Length);
            details = details.ReadOption(header, option);
            offset += header.Length;

            if (header.IsLast)
            {
                break;
            }
        }

        return details;
    }

    private OptionDetails ReadOption(PgmOptionHeader header, ReadOnlySpan<byte> option)
    {
        OptionDetails details = this;

        if (header.Type == PgmOptionType.Fragment
            && PgmOptionCodec.TryReadFragment(option, out var first, out var offset, out var length))
        {
            details = new OptionDetails
            {
                HasFragment = true,
                FragmentFirstSequence = first,
                FragmentOffset = offset,
                FragmentLength = length,
                HasFec = HasFec,
                TransmissionGroupSize = TransmissionGroupSize,
                HasParityGroup = HasParityGroup,
                ParityGroupNumber = ParityGroupNumber,
            };
        }
        else if (header.Type == PgmOptionType.ParityParameters
            && PgmOptionCodec.TryReadParityParameters(option, out var groupSize, out _, out _))
        {
            details = new OptionDetails
            {
                HasFragment = HasFragment,
                FragmentFirstSequence = FragmentFirstSequence,
                FragmentOffset = FragmentOffset,
                FragmentLength = FragmentLength,
                HasFec = true,
                TransmissionGroupSize = groupSize,
                HasParityGroup = HasParityGroup,
                ParityGroupNumber = ParityGroupNumber,
            };
        }
        else if (header.Type == PgmOptionType.ParityGroup
            && PgmOptionCodec.TryReadParityGroup(option, out var groupNumber))
        {
            details = new OptionDetails
            {
                HasFragment = HasFragment,
                FragmentFirstSequence = FragmentFirstSequence,
                FragmentOffset = FragmentOffset,
                FragmentLength = FragmentLength,
                HasFec = HasFec,
                TransmissionGroupSize = TransmissionGroupSize,
                HasParityGroup = true,
                ParityGroupNumber = groupNumber,
            };
        }

        return details;
    }
}
