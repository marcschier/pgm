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

        PgmPacket first = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);
        PgmPacket second = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);
        var firstData = (PgmDataPacket)first.Body;
        var secondData = (PgmDataPacket)second.Body;

        await Assert.That(firstData.SequenceNumber).IsEqualTo(1U);
        await Assert.That(firstData.GetDataBytes()).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(secondData.SequenceNumber).IsEqualTo(2U);
        await Assert.That(secondData.GetDataBytes()).IsEquivalentTo(new byte[] { 4, 5 });
        await Assert.That(PgmOptionCodec.TryReadFragment(
            first.GetOptionBytes().AsSpan(4),
            out uint firstSequenceNumber,
            out uint offset,
            out uint length)).IsTrue();
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

        PgmPacket heartbeat = await ReceivePacketAsync(receiver, PgmPacketType.SourcePathMessage);
        var body = (PgmSourcePathMessage)heartbeat.Body;

        await Assert.That(body.TrailingEdgeSequenceNumber).IsEqualTo(1U);
        await Assert.That(body.LeadingEdgeSequenceNumber).IsEqualTo(1U);
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
        PgmPacket odata = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);
        uint sequenceNumber = ((PgmDataPacket)odata.Body).SequenceNumber;

        await receiver.SendAsync(Encode(CreateNak(options, sequenceNumber)));

        PgmPacket ncf = await ReceivePacketAsync(receiver, PgmPacketType.NakConfirmation);
        PgmPacket repair = await ReceivePacketAsync(receiver, PgmPacketType.RepairData);
        var ncfBody = (PgmNakPacket)ncf.Body;
        var repairBody = (PgmDataPacket)repair.Body;

        await Assert.That(ncfBody.SequenceNumber).IsEqualTo(sequenceNumber);
        await Assert.That(repairBody.SequenceNumber).IsEqualTo(sequenceNumber);
        await Assert.That(repairBody.GetDataBytes()).IsEquivalentTo(new byte[] { 7, 8, 9 });
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
        PgmPacket parity = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData, requireParity: true);

        byte[] options = parity.GetOptionBytes();

        await Assert.That((parity.Header.Options & PgmHeaderOptions.Parity) != 0).IsTrue();
        await Assert.That(PgmOptionCodec.TryReadFec(options.AsSpan(4), out uint size, out bool proactive, out _))
            .IsTrue();
        await Assert.That(PgmOptionCodec.TryReadParityGroup(options.AsSpan(12), out uint groupNumber)).IsTrue();
        await Assert.That(size).IsEqualTo(2U);
        await Assert.That(proactive).IsTrue();
        await Assert.That(groupNumber).IsEqualTo(0U);
    }

    private static PgmSenderOptions Options(int maximumDataPayloadLength = 1200, int fecTransmissionGroupSize = 4)
    {
        return new PgmSenderOptions
        {
            MaximumDataPayloadLength = maximumDataPayloadLength,
            FecTransmissionGroupSize = fecTransmissionGroupSize,
            SourcePathMessageInterval = TimeSpan.FromSeconds(30),
            GlobalSourceId = new PgmGlobalSourceId(0x010203040506),
        };
    }

    private static PgmPacket CreateNak(PgmSenderOptions options, uint sequenceNumber)
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
        return PgmPacket.CreateNakLike(header, body, Array.Empty<byte>());
    }

    private static byte[] Encode(PgmPacket packet)
    {
        byte[] datagram = new byte[packet.EncodedLength];
        _ = PgmUdpEncapsulation.TryWritePayload(packet, datagram);
        return datagram;
    }

    private static async Task<PgmPacket> ReceivePacketAsync(
        InMemoryDatagramChannel channel,
        PgmPacketType type,
        bool? requireParity = null)
    {
        using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] buffer = new byte[2048];

        while (true)
        {
            int byteCount = await channel.ReceiveAsync(buffer, cancellation.Token);
            if (!PgmUdpEncapsulation.TryParsePayload(buffer.AsSpan(0, byteCount), out var packet) || packet is null)
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

            return packet;
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
