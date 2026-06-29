// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Fec;
using Pgm.Net;
using Pgm.Packets;
using Pgm.Receiver;

namespace Pgm.Tests.Receiver;

public sealed class PgmReceiverTests
{
    private static readonly PgmGlobalSourceId SourceId = new(0x010203040506);
    private static readonly PgmNetworkAddress SourceAddress = Ipv4(192, 0, 2, 10);
    private static readonly PgmNetworkAddress GroupAddress = Ipv4(239, 192, 0, 1);

    [Test]
    public async Task PgmReceiver_OrderedData_YieldsApdusInSequence()
    {
        var bus = new InMemoryMulticastBus(datagramReorderRate: 1, seed: 123);
        await using InMemoryDatagramChannel sourceChannel = bus.CreateChannel();
        await using InMemoryDatagramChannel receiverChannel = bus.CreateChannel();
        await using var receiver = CreateReceiver(receiverChannel);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await receiver.StartAsync(timeout.Token);
        await SendAsync(sourceChannel, CreateSpm(1, 3), timeout.Token);
        await SendAsync(sourceChannel, CreateData(1, 1, Bytes("one")), timeout.Token);
        await SendAsync(sourceChannel, CreateData(2, 1, Bytes("two")), timeout.Token);
        await SendAsync(sourceChannel, CreateData(3, 1, Bytes("three")), timeout.Token);
        await SendAsync(sourceChannel, CreateSpm(1, 3), timeout.Token);

        await Assert.That(await receiver.ReceiveAsync(timeout.Token)).IsEquivalentTo(Bytes("one"));
        await Assert.That(await receiver.ReceiveAsync(timeout.Token)).IsEquivalentTo(Bytes("two"));
        await Assert.That(await receiver.ReceiveAsync(timeout.Token)).IsEquivalentTo(Bytes("three"));
    }

    [Test]
    public async Task PgmReceiver_FragmentedData_ReassemblesApdu()
    {
        var bus = new InMemoryMulticastBus();
        await using InMemoryDatagramChannel sourceChannel = bus.CreateChannel();
        await using InMemoryDatagramChannel receiverChannel = bus.CreateChannel();
        await using var receiver = CreateReceiver(receiverChannel);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await receiver.StartAsync(timeout.Token);
        await SendAsync(sourceChannel, CreateSpm(1, 2), timeout.Token);
        await SendAsync(sourceChannel, CreateFragment(1, 1, 0, 5, Bytes("he")), timeout.Token);
        await SendAsync(sourceChannel, CreateFragment(2, 1, 2, 5, Bytes("llo")), timeout.Token);

        await Assert.That(await receiver.ReceiveAsync(timeout.Token)).IsEquivalentTo(Bytes("hello"));
    }

    [Test]
    public async Task PgmReceiver_GapBeforeLaterData_SendsNakAndAcceptsRepair()
    {
        var bus = new InMemoryMulticastBus();
        await using InMemoryDatagramChannel sourceChannel = bus.CreateChannel();
        await using InMemoryDatagramChannel receiverChannel = bus.CreateChannel();
        await using var receiver = CreateReceiver(receiverChannel);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observedNak = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task responder = RunRepairResponderAsync(sourceChannel, observedNak, timeout.Token);

        await receiver.StartAsync(timeout.Token);
        await SendAsync(sourceChannel, CreateSpm(1, 3), timeout.Token);
        await SendAsync(sourceChannel, CreateData(1, 1, Bytes("one")), timeout.Token);
        await SendAsync(sourceChannel, CreateData(3, 1, Bytes("three")), timeout.Token);

        await Assert.That(await receiver.ReceiveAsync(timeout.Token)).IsEquivalentTo(Bytes("one"));
        await Assert.That(await observedNak.Task.WaitAsync(timeout.Token)).IsEqualTo(2U);
        await Assert.That(await receiver.ReceiveAsync(timeout.Token)).IsEquivalentTo(Bytes("two"));
        await Assert.That(await receiver.ReceiveAsync(timeout.Token)).IsEquivalentTo(Bytes("three"));

        timeout.Cancel();
        await IgnoreCanceledAsync(responder);
    }

    [Test]
    public async Task PgmReceiver_FecParityWithOneErasure_RepairsMissingSourceBlock()
    {
        var bus = new InMemoryMulticastBus();
        await using InMemoryDatagramChannel sourceChannel = bus.CreateChannel();
        await using InMemoryDatagramChannel receiverChannel = bus.CreateChannel();
        await using var receiver = CreateReceiver(receiverChannel);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[][] sourceBlocks =
        [
            Bytes("aaaa"),
            Bytes("bbbb"),
            Bytes("cccc"),
        ];
        byte[][] parity = new ReedSolomon(3, 1).Encode(sourceBlocks);

        await receiver.StartAsync(timeout.Token);
        await SendAsync(sourceChannel, CreateSpm(1, 4), timeout.Token);
        await SendAsync(sourceChannel, CreateFecData(1, sourceBlocks[0], false), timeout.Token);
        await SendAsync(sourceChannel, CreateFecData(3, sourceBlocks[2], false), timeout.Token);
        await SendAsync(sourceChannel, CreateFecData(4, parity[0], true), timeout.Token);

        await Assert.That(await receiver.ReceiveAsync(timeout.Token)).IsEquivalentTo(sourceBlocks[0]);
        await Assert.That(await receiver.ReceiveAsync(timeout.Token)).IsEquivalentTo(sourceBlocks[1]);
        await Assert.That(await receiver.ReceiveAsync(timeout.Token)).IsEquivalentTo(sourceBlocks[2]);
    }

    private static PgmReceiver CreateReceiver(IPgmDatagramChannel channel)
    {
        return new PgmReceiver(channel, TimeProvider.System, new PgmReceiverOptions
        {
            SourcePort = 7501,
            DestinationPort = 7500,
            GroupAddress = GroupAddress,
            DefaultSourceAddress = SourceAddress,
            InitialNakBackoff = TimeSpan.FromMilliseconds(10),
            MaximumNakBackoff = TimeSpan.FromMilliseconds(20),
            NakConfirmationTimeout = TimeSpan.FromMilliseconds(10),
            MaximumNakAttempts = 10,
        });
    }

    private static async Task RunRepairResponderAsync(
        InMemoryDatagramChannel channel,
        TaskCompletionSource<uint> observedNak,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[65535];

        while (!cancellationToken.IsCancellationRequested)
        {
            int length = await channel.ReceiveAsync(buffer, cancellationToken);
            if (!PgmPacket.TryParse(buffer.AsSpan(0, length), out var packet) || packet is null)
            {
                continue;
            }

            if (packet.Header.Type != PgmPacketType.NegativeAcknowledgment || packet.Body is not PgmNakPacket nak)
            {
                continue;
            }

            observedNak.TrySetResult(nak.SequenceNumber);
            await SendAsync(channel, CreateNcf(nak.SequenceNumber), cancellationToken);
            await SendAsync(channel, CreateRepair(nak.SequenceNumber, Bytes("two")), cancellationToken);
            return;
        }
    }

    private static async Task IgnoreCanceledAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static PgmPacket CreateSpm(uint trailingEdge, uint leadingEdge)
    {
        return PgmPacket.CreateSourcePathMessage(
            Header(PgmPacketType.SourcePathMessage, 0),
            new PgmSourcePathMessage(1, trailingEdge, leadingEdge, SourceAddress),
            Array.Empty<byte>());
    }

    private static PgmPacket CreateData(uint sequenceNumber, uint trailingEdge, byte[] data)
    {
        return PgmPacket.CreateData(
            Header(PgmPacketType.OriginalData, (ushort)data.Length),
            new PgmDataPacket(sequenceNumber, trailingEdge, data),
            Array.Empty<byte>());
    }

    private static PgmPacket CreateRepair(uint sequenceNumber, byte[] data)
    {
        return PgmPacket.CreateData(
            Header(PgmPacketType.RepairData, (ushort)data.Length),
            new PgmDataPacket(sequenceNumber, 1, data),
            Array.Empty<byte>());
    }

    private static PgmPacket CreateFragment(
        uint sequenceNumber,
        uint firstSequenceNumber,
        uint offset,
        uint apduLength,
        byte[] data)
    {
        byte[] options = CreateFragmentOptions(firstSequenceNumber, offset, apduLength);
        return PgmPacket.CreateData(
            Header(PgmPacketType.OriginalData, (ushort)data.Length, PgmHeaderOptions.OptionsPresent),
            new PgmDataPacket(sequenceNumber, firstSequenceNumber, data),
            options);
    }

    private static PgmPacket CreateFecData(uint sequenceNumber, byte[] data, bool parity)
    {
        byte[] options = CreateFecOptions(1, 3);
        return PgmPacket.CreateData(
            Header(
                parity ? PgmPacketType.RepairData : PgmPacketType.OriginalData,
                (ushort)data.Length,
                PgmHeaderOptions.OptionsPresent | (parity ? PgmHeaderOptions.Parity : PgmHeaderOptions.None)),
            new PgmDataPacket(sequenceNumber, 1, data),
            options);
    }

    private static PgmPacket CreateNcf(uint sequenceNumber)
    {
        return PgmPacket.CreateNakLike(
            Header(PgmPacketType.NakConfirmation, 0),
            new PgmNakPacket(sequenceNumber, SourceAddress, GroupAddress),
            Array.Empty<byte>());
    }

    private static PgmHeader Header(
        PgmPacketType type,
        ushort tsduLength,
        PgmHeaderOptions options = PgmHeaderOptions.None)
    {
        return new PgmHeader(7500, 7501, type, options, 0, SourceId, tsduLength);
    }

    private static byte[] CreateFecOptions(uint parityGroupNumber, uint transmissionGroupSize)
    {
        var options = new byte[20];
        _ = PgmOptionCodec.TryWriteLength(options, (ushort)options.Length, false);
        _ = PgmOptionCodec.TryWriteFec(options.AsSpan(4), transmissionGroupSize, true, true, false);
        _ = PgmOptionCodec.TryWriteParityGroup(options.AsSpan(12), parityGroupNumber, true);
        return options;
    }

    private static byte[] CreateFragmentOptions(uint firstSequenceNumber, uint offset, uint apduLength)
    {
        var options = new byte[20];
        _ = PgmOptionCodec.TryWriteLength(options, (ushort)options.Length, false);
        _ = PgmOptionCodec.TryWriteFragment(options.AsSpan(4), firstSequenceNumber, offset, apduLength, true);
        return options;
    }

    private static async ValueTask SendAsync(
        InMemoryDatagramChannel channel,
        PgmPacket packet,
        CancellationToken cancellationToken)
    {
        var datagram = new byte[packet.EncodedLength];
        _ = packet.TryWrite(datagram);
        await channel.SendAsync(datagram, cancellationToken);
    }

    private static byte[] Bytes(string value)
    {
        var bytes = new byte[value.Length];

        for (int index = 0; index < value.Length; index++)
        {
            bytes[index] = (byte)value[index];
        }

        return bytes;
    }

    private static PgmNetworkAddress Ipv4(byte a, byte b, byte c, byte d)
    {
        return new PgmNetworkAddress(PgmAddressFamily.IPv4, new[] { a, b, c, d });
    }
}
