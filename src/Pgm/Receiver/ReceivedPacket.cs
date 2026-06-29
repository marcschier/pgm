// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Receiver;

internal sealed class ReceivedPacket
{
    private ReceivedPacket(
        uint sequenceNumber,
        byte[] data,
        bool hasFragment,
        uint fragmentFirstSequence,
        uint fragmentOffset,
        uint fragmentLength)
    {
        SequenceNumber = sequenceNumber;
        Data = data;
        HasFragment = hasFragment;
        FragmentFirstSequence = fragmentFirstSequence;
        FragmentOffset = fragmentOffset;
        FragmentLength = fragmentLength;
    }

    public uint SequenceNumber { get; }

    public byte[] Data { get; }

    public bool HasFragment { get; }

    public uint FragmentFirstSequence { get; }

    public uint FragmentOffset { get; }

    public uint FragmentLength { get; }

    public static ReceivedPacket FromPacket(PgmPacket packet, PgmDataPacket data)
    {
        OptionDetails details = OptionDetails.Parse(packet);
        return new ReceivedPacket(
            data.SequenceNumber,
            data.GetDataBytes(),
            details.HasFragment,
            details.FragmentFirstSequence,
            details.FragmentOffset,
            details.FragmentLength);
    }

    public static ReceivedPacket FromRepairedSource(uint sequenceNumber, byte[] data)
    {
        return new ReceivedPacket(sequenceNumber, data, false, 0, 0, 0);
    }
}
