// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Receiver;

internal sealed class ReceiveWindow
{
    private readonly SourceKey sourceKey;
    private readonly PgmReceiver receiver;
    private readonly PgmReceiverOptions options;
    private readonly TimeProvider timeProvider;
    private readonly SortedDictionary<uint, ReceivedPacket> packets = new();
    private readonly Dictionary<uint, NakState> naks = new();
    private readonly Dictionary<uint, FragmentAssembly> fragments = new();
    private readonly Dictionary<uint, FecGroup> fecGroups = new();
    private readonly Queue<byte[]> completedApdus = new();
    private PgmNetworkAddress? sourcePath;
    private uint expectedSequence;
    private bool hasExpectedSequence;

    public ReceiveWindow(
        SourceKey sourceKey,
        PgmReceiver receiver,
        PgmReceiverOptions options,
        TimeProvider timeProvider)
    {
        this.sourceKey = sourceKey;
        this.receiver = receiver;
        this.options = options;
        this.timeProvider = timeProvider;
    }

    public bool ProcessPacket(PgmPacket packet)
    {
        if (packet.Body is PgmSourcePathMessage spm)
        {
            ProcessSpm(spm);
            return true;
        }

        if (packet.Body is PgmNakPacket nak && packet.Header.Type == PgmPacketType.NakConfirmation)
        {
            ProcessNakConfirmation(nak);
            return false;
        }

        if (packet.Body is PgmDataPacket data
            && (packet.Header.Type == PgmPacketType.OriginalData || packet.Header.Type == PgmPacketType.RepairData))
        {
            return ProcessData(packet, data);
        }

        return false;
    }

    public void CollectDueNaks(long now, List<PgmPacket> packetsToSend)
    {
        var lost = new List<uint>();

        foreach (NakState nak in naks.Values)
        {
            if (packets.ContainsKey(nak.SequenceNumber))
            {
                lost.Add(nak.SequenceNumber);
                continue;
            }

            if (now < nak.DueTimestamp)
            {
                continue;
            }

            if (nak.Attempts >= options.MaximumNakAttempts)
            {
                lost.Add(nak.SequenceNumber);
                continue;
            }

            packetsToSend.Add(CreateNak(nak.SequenceNumber));
            nak.MarkSent(now, options.InitialNakBackoff, options.MaximumNakBackoff,
                TimestampFromTimeSpan(options.NakConfirmationTimeout));
        }

        for (int index = 0; index < lost.Count; index++)
        {
            naks.Remove(lost[index]);
        }

        if (lost.Count > 0)
        {
            AdvancePastLostData();
        }
    }

    public bool TryDequeueCompletedApdu(out byte[]? apdu)
    {
        if (completedApdus.Count == 0)
        {
            apdu = null;
            return false;
        }

        apdu = completedApdus.Dequeue();
        return true;
    }

    private void ProcessSpm(PgmSourcePathMessage spm)
    {
        sourcePath = spm.Path;

        if (!hasExpectedSequence)
        {
            expectedSequence = spm.TrailingEdgeSequenceNumber;
            hasExpectedSequence = true;
        }

        RemoveBefore(spm.TrailingEdgeSequenceNumber);

        while (hasExpectedSequence && SequenceLessThan(expectedSequence, spm.TrailingEdgeSequenceNumber))
        {
            naks.Remove(expectedSequence);
            expectedSequence++;
        }
    }

    private bool ProcessData(PgmPacket packet, PgmDataPacket data)
    {
        if (!hasExpectedSequence)
        {
            expectedSequence = data.TrailingEdgeSequenceNumber;
            hasExpectedSequence = true;
        }

        RemoveBefore(data.TrailingEdgeSequenceNumber);

        if (SequenceLessThan(data.SequenceNumber, expectedSequence))
        {
            return false;
        }

        if (!packets.ContainsKey(data.SequenceNumber))
        {
            packets.Add(data.SequenceNumber, ReceivedPacket.FromPacket(packet, data));
        }

        naks.Remove(data.SequenceNumber);
        TrackFec(packet, data);
        DetectGaps(data.SequenceNumber);
        DeliverAvailable();
        return true;
    }

    private void ProcessNakConfirmation(PgmNakPacket nak)
    {
        if (naks.TryGetValue(nak.SequenceNumber, out var state))
        {
            state.MarkConfirmed(timeProvider.GetTimestamp(), TimestampFromTimeSpan(state.CurrentBackoff));
        }
    }

    private void DetectGaps(uint seenSequence)
    {
        for (uint sequence = expectedSequence; SequenceLessThan(sequence, seenSequence); sequence++)
        {
            if (!packets.ContainsKey(sequence) && !naks.ContainsKey(sequence))
            {
                naks.Add(sequence, new NakState(sequence, timeProvider.GetTimestamp()));
            }
        }
    }

    private void DeliverAvailable()
    {
        while (packets.TryGetValue(expectedSequence, out var packet))
        {
            packets.Remove(expectedSequence);
            naks.Remove(expectedSequence);
            QueueApdu(packet);
            expectedSequence++;
        }
    }

    private void QueueApdu(ReceivedPacket packet)
    {
        if (!packet.HasFragment)
        {
            completedApdus.Enqueue(packet.Data);
            return;
        }

        if (!fragments.TryGetValue(packet.FragmentFirstSequence, out var assembly))
        {
            assembly = new FragmentAssembly(packet.FragmentLength);
            fragments.Add(packet.FragmentFirstSequence, assembly);
        }

        if (assembly.TryAdd(packet.FragmentOffset, packet.Data) && assembly.IsComplete)
        {
            completedApdus.Enqueue(assembly.ToArray());
            fragments.Remove(packet.FragmentFirstSequence);
        }
    }

    private void TrackFec(PgmPacket packet, PgmDataPacket data)
    {
        OptionDetails details = OptionDetails.Parse(packet);

        if (!details.HasParityGroup || details.TransmissionGroupSize <= 0 || details.TransmissionGroupSize > 255)
        {
            return;
        }

        if (!fecGroups.TryGetValue(details.ParityGroupNumber, out var group))
        {
            group = new FecGroup(details.ParityGroupNumber, (int)details.TransmissionGroupSize);
            fecGroups.Add(details.ParityGroupNumber, group);
        }

        int index = (int)(data.SequenceNumber - details.ParityGroupNumber);
        if ((packet.Header.Options & PgmHeaderOptions.Parity) != 0)
        {
            index = group.SourceCount + Math.Max(0, index - group.SourceCount);
        }

        if (group.TryAdd(index, ReceivedPacket.FromPacket(packet, data)))
        {
            RepairFromFec(group);
        }
    }

    private void RepairFromFec(FecGroup group)
    {
        if (!group.CanDecode)
        {
            return;
        }

        byte[][] decoded;
        try
        {
            decoded = group.Decode();
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (DivideByZeroException)
        {
            return;
        }

        for (int index = 0; index < decoded.Length; index++)
        {
            uint sequence = group.FirstSequenceNumber + (uint)index;
            if (!packets.ContainsKey(sequence))
            {
                packets.Add(sequence, ReceivedPacket.FromRepairedSource(sequence, decoded[index]));
                naks.Remove(sequence);
            }
        }

        DeliverAvailable();
    }

    private void AdvancePastLostData()
    {
        while (hasExpectedSequence && !packets.ContainsKey(expectedSequence) && !naks.ContainsKey(expectedSequence))
        {
            expectedSequence++;

            if (packets.Count == 0)
            {
                break;
            }
        }

        DeliverAvailable();
    }

    private void RemoveBefore(uint sequence)
    {
        while (packets.Count > options.ReceiveWindowSize)
        {
            packets.Remove(packets.Keys.First());
        }

        var removeNaks = new List<uint>();
        foreach (uint nak in naks.Keys)
        {
            if (SequenceLessThan(nak, sequence))
            {
                removeNaks.Add(nak);
            }
        }

        for (int index = 0; index < removeNaks.Count; index++)
        {
            naks.Remove(removeNaks[index]);
        }
    }

    private PgmPacket CreateNak(uint sequenceNumber)
    {
        var body = new PgmNakPacket(sequenceNumber, sourcePath ?? options.DefaultSourceAddress, options.GroupAddress);
        return PgmPacket.CreateNakLike(
            new PgmHeader(
                options.SourcePort,
                options.DestinationPort,
                PgmPacketType.NegativeAcknowledgment,
                PgmHeaderOptions.None,
                0,
                options.ReceiverGlobalSourceId,
                0),
            body,
            Array.Empty<byte>());
    }

    private long TimestampFromTimeSpan(TimeSpan value)
    {
        double seconds = value.TotalSeconds;
        if (seconds <= 0)
        {
            return 0;
        }

        return (long)(seconds * timeProvider.TimestampFrequency);
    }

    private static bool SequenceLessThan(uint left, uint right)
    {
        return left != right && (int)(left - right) < 0;
    }
}
