// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Net;
using Pgm.Packets;
using Pgm.Sender;

namespace Pgm.Tests.Sender;

public sealed class PgmSenderStateTests
{
    [Test]
    public async Task PgmSender_Sends_Odata_And_Fragments_Large_Apdus()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();
        await using PgmSender sender = new PgmSender(source, Options(maximumDataPayloadLength: 3));

        await sender.StartAsync();
        await sender.SendAsync(new byte[] { 1, 2, 3, 4, 5 });

        byte[] firstDatagram = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);
        byte[] secondDatagram = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);
        _ = PgmPacket.TryParse(firstDatagram, out var first);
        _ = PgmPacket.TryParse(secondDatagram, out var second);
        _ = first.TryGetData(out var firstData);
        _ = second.TryGetData(out var secondData);
        uint firstSeq = firstData.SequenceNumber;
        byte[] firstBytes = firstData.GetDataBytes();
        uint secondSeq = secondData.SequenceNumber;
        byte[] secondBytes = secondData.GetDataBytes();
        bool fragmentRead = PgmOptionCodec.TryReadFragment(
            first.GetOptionBytes().AsSpan(4),
            out uint firstSequenceNumber,
            out uint offset,
            out uint length);

        await Assert.That(firstSeq).IsEqualTo(1U);
        await Assert.That(firstBytes).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(secondSeq).IsEqualTo(2U);
        await Assert.That(secondBytes).IsEquivalentTo(new byte[] { 4, 5 });
        await Assert.That(fragmentRead).IsTrue();
        await Assert.That(firstSequenceNumber).IsEqualTo(1U);
        await Assert.That(offset).IsEqualTo(0U);
        await Assert.That(length).IsEqualTo(5U);
    }

    [Test]
    public async Task PgmSender_Emits_Heartbeat_Spm_With_Window_Edges()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();
        await using PgmSender sender = new PgmSender(source, Options(), timeProvider);

        await sender.StartAsync();
        await ReceivePacketAsync(receiver, PgmPacketType.SourcePathMessage);
        await sender.SendAsync(new byte[] { 9 });
        await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);

        timeProvider.FireTimers();

        _ = PgmPacket.TryParse(await ReceivePacketAsync(receiver, PgmPacketType.SourcePathMessage), out var heartbeat);
        _ = heartbeat.TryGetSourcePathMessage(out var body);
        uint trailingEdge = body.TrailingEdgeSequenceNumber;
        uint leadingEdge = body.LeadingEdgeSequenceNumber;

        await Assert.That(trailingEdge).IsEqualTo(1U);
        await Assert.That(leadingEdge).IsEqualTo(1U);
    }

    [Test]
    public async Task PgmSender_Repairs_Windowed_Data_After_Nak()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();
        PgmSenderOptions options = Options();
        await using PgmSender sender = new PgmSender(source, options);

        await sender.StartAsync();
        await sender.SendAsync(new byte[] { 7, 8, 9 });
        byte[] odataDatagram = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);
        _ = PgmPacket.TryParse(odataDatagram, out var odata);
        _ = odata.TryGetData(out var odataBody);
        uint sequenceNumber = odataBody.SequenceNumber;

        await receiver.SendAsync(CreateNak(options, sequenceNumber));

        byte[] ncfDatagram = await ReceivePacketAsync(receiver, PgmPacketType.NakConfirmation);
        byte[] repairDatagram = await ReceivePacketAsync(receiver, PgmPacketType.RepairData);
        _ = PgmPacket.TryParse(ncfDatagram, out var ncf);
        _ = PgmPacket.TryParse(repairDatagram, out var repair);
        _ = ncf.TryGetNak(out var ncfBody);
        _ = repair.TryGetData(out var repairBody);
        uint ncfSeq = ncfBody.SequenceNumber;
        uint repairSeq = repairBody.SequenceNumber;
        byte[] repairBytes = repairBody.GetDataBytes();

        await Assert.That(ncfSeq).IsEqualTo(sequenceNumber);
        await Assert.That(repairSeq).IsEqualTo(sequenceNumber);
        await Assert.That(repairBytes).IsEquivalentTo(new byte[] { 7, 8, 9 });
    }

    [Test]
    public async Task PgmSender_Repairs_Every_Sequence_Listed_In_Nak_List()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();
        PgmSenderOptions options = Options();
        await using PgmSender sender = new PgmSender(source, options);

        await sender.StartAsync();
        await sender.SendAsync(new byte[] { 1 });
        await sender.SendAsync(new byte[] { 2 });
        _ = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);
        _ = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);

        await receiver.SendAsync(CreateNakWithList(options, sequenceNumber: 1, additional: 2));

        List<uint> repairedSequences = await CollectRepairSequencesAsync(receiver, count: 2);

        await Assert.That(repairedSequences).Contains(1U);
        await Assert.That(repairedSequences).Contains(2U);
    }

    [Test]
    public async Task PgmSender_Repairs_With_OnDemand_Parity_When_Sequence_Not_Windowed()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();
        PgmSenderOptions options = Options(onDemandParityPacketCount: 1);
        await using PgmSender sender = new PgmSender(source, options);

        await sender.StartAsync();
        await sender.SendAsync(new byte[] { 5, 6 });
        _ = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);

        await receiver.SendAsync(CreateNak(options, sequenceNumber: 2));

        byte[] ncfDatagram = await ReceivePacketAsync(receiver, PgmPacketType.NakConfirmation);
        byte[] repairDatagram = await ReceivePacketAsync(receiver, PgmPacketType.RepairData);
        _ = PgmPacket.TryParse(ncfDatagram, out var ncf);
        _ = PgmPacket.TryParse(repairDatagram, out var repair);
        _ = ncf.TryGetNak(out var ncfBody);
        uint ncfSeq = ncfBody.SequenceNumber;
        bool repairIsParity = (repair.Header.Options & PgmHeaderOptions.Parity) != 0;

        await Assert.That(ncfSeq).IsEqualTo(2U);
        await Assert.That(repairIsParity).IsTrue();
    }

    [Test]
    public async Task PgmSender_Ignores_Nak_For_Unknown_Sequence_Without_Parity()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();
        PgmSenderOptions options = Options(onDemandParityPacketCount: 0);
        await using PgmSender sender = new PgmSender(source, options);

        await sender.StartAsync();
        await sender.SendAsync(new byte[] { 4 });
        _ = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);

        await receiver.SendAsync(CreateNak(options, sequenceNumber: 9999));
        await receiver.SendAsync(CreateNak(options, sequenceNumber: 1));

        byte[] ncfDatagram = await ReceivePacketAsync(receiver, PgmPacketType.NakConfirmation);
        _ = PgmPacket.TryParse(ncfDatagram, out var ncf);
        _ = ncf.TryGetNak(out var ncfBody);
        uint ncfSeq = ncfBody.SequenceNumber;

        await Assert.That(ncfSeq).IsEqualTo(1U);
    }

    [Test]
    public async Task PgmSender_Sends_Proactive_Fec_Parity_For_Complete_Group()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();
        await using PgmSender sender = new PgmSender(source, Options(fecTransmissionGroupSize: 2));

        await sender.StartAsync();
        await sender.SendAsync(new byte[] { 1, 2 });
        await sender.SendAsync(new byte[] { 3, 4 });

        _ = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData, requireParity: false);
        _ = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData, requireParity: false);
        _ = PgmPacket.TryParse(
            await ReceivePacketAsync(receiver, PgmPacketType.OriginalData, requireParity: true), out var parity);

        byte[] options = parity.GetOptionBytes();
        bool isParity = (parity.Header.Options & PgmHeaderOptions.Parity) != 0;

        await Assert.That(isParity).IsTrue();
        await Assert.That(PgmOptionCodec.TryReadFec(options.AsSpan(4), out uint size, out bool proactive, out _))
            .IsTrue();
        await Assert.That(PgmOptionCodec.TryReadParityGroup(options.AsSpan(12), out uint groupNumber)).IsTrue();
        await Assert.That(size).IsEqualTo(2U);
        await Assert.That(proactive).IsTrue();
        await Assert.That(groupNumber).IsEqualTo(0U);
    }

    [Test]
    public async Task PgmSender_Evicts_Oldest_Sequence_When_Window_Is_Full()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();
        PgmSenderOptions options = Options(onDemandParityPacketCount: 0, transmitWindowPacketCount: 1);
        await using PgmSender sender = new PgmSender(source, options);

        await sender.StartAsync();
        await sender.SendAsync(new byte[] { 1 });
        await sender.SendAsync(new byte[] { 2 });
        _ = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);
        _ = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);

        // Sequence 1 was evicted from the single-packet window, so only sequence 2 can be repaired.
        await receiver.SendAsync(CreateNak(options, sequenceNumber: 1));
        await receiver.SendAsync(CreateNak(options, sequenceNumber: 2));

        byte[] ncfDatagram = await ReceivePacketAsync(receiver, PgmPacketType.NakConfirmation);
        _ = PgmPacket.TryParse(ncfDatagram, out var ncf);
        _ = ncf.TryGetNak(out var ncfBody);
        uint ncfSeq = ncfBody.SequenceNumber;

        await Assert.That(ncfSeq).IsEqualTo(2U);
    }

    private static PgmSenderOptions Options(
        int maximumDataPayloadLength = 1200,
        int fecTransmissionGroupSize = 4,
        int onDemandParityPacketCount = 1,
        int transmitWindowPacketCount = 1024)
    {
        return new PgmSenderOptions
        {
            MaximumDataPayloadLength = maximumDataPayloadLength,
            FecTransmissionGroupSize = fecTransmissionGroupSize,
            OnDemandParityPacketCount = onDemandParityPacketCount,
            TransmitWindowPacketCount = transmitWindowPacketCount,
            SourcePathMessageInterval = TimeSpan.FromSeconds(30),
            GlobalSourceId = new PgmGlobalSourceId(0x010203040506),
        };
    }

    private static byte[] CreateNak(PgmSenderOptions options, uint sequenceNumber)
    {
        PgmHeader header = new PgmHeader(
            options.SourcePort,
            options.DestinationPort,
            PgmPacketType.NegativeAcknowledgment,
            PgmHeaderOptions.None,
            0,
            options.GlobalSourceId,
            0);
        PgmNakPacket body = new PgmNakPacket(sequenceNumber, options.SourceAddress, options.GroupAddress);
        PgmPacket packet = PgmPacket.CreateNakLike(header, body, ReadOnlySpan<byte>.Empty);
        byte[] datagram = new byte[packet.EncodedLength];
        _ = packet.TryWrite(datagram);
        return datagram;
    }

    private static byte[] CreateNakWithList(PgmSenderOptions options, uint sequenceNumber, params uint[] additional)
    {
        byte[] optionBytes = new byte[4 + 4 + (additional.Length * 4)];
        _ = PgmOptionCodec.TryWriteLength(optionBytes, (ushort)optionBytes.Length, isLast: false);
        _ = PgmOptionCodec.TryWriteNakList(optionBytes.AsSpan(4), additional, isLast: true);

        PgmHeader header = new PgmHeader(
            options.SourcePort,
            options.DestinationPort,
            PgmPacketType.NegativeAcknowledgment,
            PgmHeaderOptions.OptionsPresent,
            0,
            options.GlobalSourceId,
            0);
        PgmNakPacket body = new PgmNakPacket(sequenceNumber, options.SourceAddress, options.GroupAddress);
        PgmPacket packet = PgmPacket.CreateNakLike(header, body, optionBytes);
        byte[] datagram = new byte[packet.EncodedLength];
        _ = packet.TryWrite(datagram);
        return datagram;
    }

    private static async Task<List<uint>> CollectRepairSequencesAsync(InMemoryDatagramChannel channel, int count)
    {
        using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] buffer = new byte[2048];
        List<uint> sequences = new List<uint>();

        while (sequences.Count < count)
        {
            int byteCount = await channel.ReceiveAsync(buffer, cancellation.Token);
            if (!PgmUdpEncapsulation.TryParsePayload(buffer.AsSpan(0, byteCount), out var packet))
            {
                continue;
            }

            if (packet.Header.Type == PgmPacketType.RepairData && packet.TryGetData(out var data))
            {
                sequences.Add(data.SequenceNumber);
            }
        }

        return sequences;
    }

    private static async Task<byte[]> ReceivePacketAsync(
        InMemoryDatagramChannel channel,
        PgmPacketType type,
        bool? requireParity = null)
    {
        using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] buffer = new byte[2048];

        while (true)
        {
            int byteCount = await channel.ReceiveAsync(buffer, cancellation.Token);
            if (!PgmUdpEncapsulation.TryParsePayload(buffer.AsSpan(0, byteCount), out var packet))
            {
                continue;
            }

            if (packet.Header.Type != type)
            {
                continue;
            }

            bool isParity = (packet.Header.Options & PgmHeaderOptions.Parity) != 0;
            if (requireParity.HasValue && requireParity.Value != isParity)
            {
                continue;
            }

            return buffer.AsSpan(0, byteCount).ToArray();
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = new List<ManualTimer>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            _timers.Add(timer);
            return timer;
        }

        internal void FireTimers()
        {
            for (int i = 0; i < _timers.Count; i++)
            {
                _timers[i].Fire();
            }
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private bool _disposed;

        internal ManualTimer(TimerCallback callback, object? state)
        {
            _callback = callback;
            _state = state;
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _ = dueTime;
            _ = period;
            return !_disposed;
        }

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return default;
        }

        public void Dispose()
        {
            _disposed = true;
        }

        internal void Fire()
        {
            if (!_disposed)
            {
                _callback(_state);
            }
        }
    }
}
