// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Receiver;

internal sealed class ReceiveWindow
{
    private readonly SourceKey _sourceKey;
    private readonly PgmReceiver _receiver;
    private readonly PgmReceiverOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SortedDictionary<uint, ReceivedPacket> _packets = new();
    private readonly Dictionary<uint, NakState> _naks = new();
    private readonly Dictionary<uint, FragmentAssembly> _fragments = new();
    private readonly Dictionary<uint, FecGroup> _fecGroups = new();
    private readonly Queue<byte[]> _completedApdus = new();
    private PgmNetworkAddress? _sourcePath;
    private uint _expectedSequence;
    private bool _hasExpectedSequence;

    public ReceiveWindow(
        SourceKey sourceKey,
        PgmReceiver receiver,
        PgmReceiverOptions options,
        TimeProvider timeProvider)
    {
        _sourceKey = sourceKey;
        _receiver = receiver;
        _options = options;
        _timeProvider = timeProvider;
    }

    public bool ProcessPacket(PgmPacket packet)
    {
        if (packet.TryGetSourcePathMessage(out var spm))
        {
            ProcessSpm(spm);
            return true;
        }

        if (packet.Header.Type == PgmPacketType.NakConfirmation && packet.TryGetNak(out var nak))
        {
            ProcessNakConfirmation(nak);
            return false;
        }

        if ((packet.Header.Type == PgmPacketType.OriginalData || packet.Header.Type == PgmPacketType.RepairData)
            && packet.TryGetData(out var data))
        {
            return ProcessData(packet, data);
        }

        return false;
    }

    public void CollectDueNaks(long now, List<byte[]> datagramsToSend)
    {
        var lost = new List<uint>();

        foreach (NakState nak in _naks.Values)
        {
            if (_packets.ContainsKey(nak.SequenceNumber))
            {
                lost.Add(nak.SequenceNumber);
                continue;
            }

            if (now < nak.DueTimestamp)
            {
                continue;
            }

            if (nak.Attempts >= _options.MaximumNakAttempts)
            {
                lost.Add(nak.SequenceNumber);
                continue;
            }

            datagramsToSend.Add(CreateNak(nak.SequenceNumber));
            nak.MarkSent(now, _options.InitialNakBackoff, _options.MaximumNakBackoff,
                TimestampFromTimeSpan(_options.NakConfirmationTimeout));
        }

        for (int index = 0; index < lost.Count; index++)
        {
            _naks.Remove(lost[index]);
        }

        if (lost.Count > 0)
        {
            AdvancePastLostData();
        }
    }

    public bool TryDequeueCompletedApdu(out byte[]? apdu)
    {
        if (_completedApdus.Count == 0)
        {
            apdu = null;
            return false;
        }

        apdu = _completedApdus.Dequeue();
        return true;
    }

    private void ProcessSpm(PgmSourcePathMessage spm)
    {
        _sourcePath = spm.Path;

        if (!_hasExpectedSequence)
        {
            _expectedSequence = spm.TrailingEdgeSequenceNumber;
            _hasExpectedSequence = true;
        }

        RemoveBefore(spm.TrailingEdgeSequenceNumber);

        while (_hasExpectedSequence && SequenceLessThan(_expectedSequence, spm.TrailingEdgeSequenceNumber))
        {
            _naks.Remove(_expectedSequence);
            _expectedSequence++;
        }
    }

    private bool ProcessData(PgmPacket packet, PgmDataPacket data)
    {
        if (!_hasExpectedSequence)
        {
            _expectedSequence = data.TrailingEdgeSequenceNumber;
            _hasExpectedSequence = true;
        }

        RemoveBefore(data.TrailingEdgeSequenceNumber);

        if (SequenceLessThan(data.SequenceNumber, _expectedSequence))
        {
            return false;
        }

        if (!_packets.ContainsKey(data.SequenceNumber))
        {
            _packets.Add(data.SequenceNumber, ReceivedPacket.FromPacket(packet, data));
        }

        _naks.Remove(data.SequenceNumber);
        TrackFec(packet, data);
        DetectGaps(data.SequenceNumber);
        DeliverAvailable();
        return true;
    }

    private void ProcessNakConfirmation(PgmNakPacket nak)
    {
        if (_naks.TryGetValue(nak.SequenceNumber, out var state))
        {
            state.MarkConfirmed(_timeProvider.GetTimestamp(), TimestampFromTimeSpan(state.CurrentBackoff));
        }
    }

    private void DetectGaps(uint seenSequence)
    {
        for (uint sequence = _expectedSequence; SequenceLessThan(sequence, seenSequence); sequence++)
        {
            if (!_packets.ContainsKey(sequence) && !_naks.ContainsKey(sequence))
            {
                _naks.Add(sequence, new NakState(sequence, _timeProvider.GetTimestamp()));
            }
        }
    }

    private void DeliverAvailable()
    {
        while (_packets.TryGetValue(_expectedSequence, out var packet))
        {
            _packets.Remove(_expectedSequence);
            _naks.Remove(_expectedSequence);
            QueueApdu(packet);
            _expectedSequence++;
        }
    }

    private void QueueApdu(ReceivedPacket packet)
    {
        if (!packet.HasFragment)
        {
            _completedApdus.Enqueue(packet.Data);
            return;
        }

        if (!_fragments.TryGetValue(packet.FragmentFirstSequence, out var assembly))
        {
            assembly = new FragmentAssembly(packet.FragmentLength);
            _fragments.Add(packet.FragmentFirstSequence, assembly);
        }

        if (assembly.TryAdd(packet.FragmentOffset, packet.Data) && assembly.IsComplete)
        {
            _completedApdus.Enqueue(assembly.ToArray());
            _fragments.Remove(packet.FragmentFirstSequence);
        }
    }

    private void TrackFec(PgmPacket packet, PgmDataPacket data)
    {
        OptionDetails details = OptionDetails.Parse(packet);

        if (!details.HasParityGroup || details.TransmissionGroupSize <= 0 || details.TransmissionGroupSize > 255)
        {
            return;
        }

        if (!_fecGroups.TryGetValue(details.ParityGroupNumber, out var group))
        {
            group = new FecGroup(details.ParityGroupNumber, (int)details.TransmissionGroupSize);
            _fecGroups.Add(details.ParityGroupNumber, group);
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
            if (!_packets.ContainsKey(sequence))
            {
                _packets.Add(sequence, ReceivedPacket.FromRepairedSource(sequence, decoded[index]));
                _naks.Remove(sequence);
            }
        }

        DeliverAvailable();
    }

    private void AdvancePastLostData()
    {
        while (_hasExpectedSequence
            && !_packets.ContainsKey(_expectedSequence)
            && !_naks.ContainsKey(_expectedSequence))
        {
            _expectedSequence++;

            if (_packets.Count == 0)
            {
                break;
            }
        }

        DeliverAvailable();
    }

    private void RemoveBefore(uint sequence)
    {
        while (_packets.Count > _options.ReceiveWindowSize)
        {
            _packets.Remove(_packets.Keys.First());
        }

        var removeNaks = new List<uint>();
        foreach (uint nak in _naks.Keys)
        {
            if (SequenceLessThan(nak, sequence))
            {
                removeNaks.Add(nak);
            }
        }

        for (int index = 0; index < removeNaks.Count; index++)
        {
            _naks.Remove(removeNaks[index]);
        }
    }

    private byte[] CreateNak(uint sequenceNumber)
    {
        var body = new PgmNakPacket(
            sequenceNumber,
            _sourcePath ?? _options.DefaultSourceAddress,
            _options.GroupAddress);
        PgmPacket packet = PgmPacket.CreateNakLike(
            new PgmHeader(
                _options.SourcePort,
                _options.DestinationPort,
                PgmPacketType.NegativeAcknowledgment,
                PgmHeaderOptions.None,
                0,
                _options.ReceiverGlobalSourceId,
                0),
            body,
            ReadOnlySpan<byte>.Empty);
        var datagram = new byte[packet.EncodedLength];
        _ = packet.TryWrite(datagram);
        return datagram;
    }

    private long TimestampFromTimeSpan(TimeSpan value)
    {
        double seconds = value.TotalSeconds;
        if (seconds <= 0)
        {
            return 0;
        }

        return (long)(seconds * _timeProvider.TimestampFrequency);
    }

    private static bool SequenceLessThan(uint left, uint right)
    {
        return left != right && (int)(left - right) < 0;
    }
}
