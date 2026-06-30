// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Net;
using Pgm.Packets;
using Pgm.Sender;

namespace Pgm.Tests.Sender;

public sealed class PgmSenderLifecycleTests
{
    [Test]
    public async Task Constructor_NullChannel_Throws()
    {
        await Assert.That(() => new PgmSender(null!, new PgmSenderOptions())).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullOptions_Throws()
    {
        var bus = new InMemoryMulticastBus();

        await Assert.That(() => new PgmSender(bus.CreateChannel(), null!)).Throws<ArgumentNullException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(ushort.MaxValue + 1)]
    public async Task Constructor_InvalidMaximumDataPayloadLength_Throws(int payloadLength)
    {
        var bus = new InMemoryMulticastBus();
        var options = new PgmSenderOptions { MaximumDataPayloadLength = payloadLength };

        await Assert.That(() => new PgmSender(bus.CreateChannel(), options))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Constructor_NonPositiveTransmitWindow_Throws()
    {
        var bus = new InMemoryMulticastBus();
        var options = new PgmSenderOptions { TransmitWindowPacketCount = 0 };

        await Assert.That(() => new PgmSender(bus.CreateChannel(), options))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Constructor_NonPositiveSourcePathMessageInterval_Throws()
    {
        var bus = new InMemoryMulticastBus();
        var options = new PgmSenderOptions { SourcePathMessageInterval = TimeSpan.Zero };

        await Assert.That(() => new PgmSender(bus.CreateChannel(), options))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Constructor_NegativeMaximumDatagramsPerSecond_Throws()
    {
        var bus = new InMemoryMulticastBus();
        var options = new PgmSenderOptions { MaximumDatagramsPerSecond = -1 };

        await Assert.That(() => new PgmSender(bus.CreateChannel(), options))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(256)]
    public async Task Constructor_InvalidFecTransmissionGroupSize_Throws(int groupSize)
    {
        var bus = new InMemoryMulticastBus();
        var options = new PgmSenderOptions { FecTransmissionGroupSize = groupSize };

        await Assert.That(() => new PgmSender(bus.CreateChannel(), options))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Constructor_NegativeParityCount_Throws()
    {
        var bus = new InMemoryMulticastBus();
        var options = new PgmSenderOptions { ProactiveParityPacketCount = -1 };

        await Assert.That(() => new PgmSender(bus.CreateChannel(), options))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task SendAsync_BeforeStart_Throws()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using PgmSender sender = new PgmSender(source, Options());
        byte[] payload = [1];

        await Assert.That(async () => await sender.SendAsync(payload)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StartAsync_CalledTwice_IsIdempotent()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using PgmSender sender = new PgmSender(source, Options());
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

        await sender.StartAsync(timeout.Token);
        await sender.StartAsync(timeout.Token);

        await Assert.That(timeout.IsCancellationRequested).IsFalse();
    }

    [Test]
    public async Task StartAsync_AfterDispose_Throws()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        PgmSender sender = new PgmSender(source, Options());
        await sender.DisposeAsync();

        await Assert.That(async () => await sender.StartAsync()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task SendAsync_AfterDispose_Throws()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        PgmSender sender = new PgmSender(source, Options());
        byte[] payload = [1];
        await sender.DisposeAsync();

        await Assert.That(async () => await sender.SendAsync(payload)).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task DisposeAsync_BeforeStart_IsIdempotent()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        PgmSender sender = new PgmSender(source, Options());
        byte[] payload = [1];

        await sender.DisposeAsync();
        await sender.DisposeAsync();

        await Assert.That(async () => await sender.SendAsync(payload)).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task SendAsync_WhenRateLimited_StillDeliversData()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel source = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();
        PgmSenderOptions options = Options();
        options.MaximumDatagramsPerSecond = 100;
        await using PgmSender sender = new PgmSender(source, options);
        byte[] payload = [7, 8, 9];

        await sender.StartAsync();
        await sender.SendAsync(payload);

        byte[] datagram = await ReceivePacketAsync(receiver, PgmPacketType.OriginalData);
        _ = PgmPacket.TryParse(datagram, out var packet);
        _ = packet.TryGetData(out var data);
        byte[] received = data.GetDataBytes();

        await Assert.That(received).IsEquivalentTo(payload);
    }

    private static PgmSenderOptions Options()
    {
        return new PgmSenderOptions
        {
            SourcePathMessageInterval = TimeSpan.FromSeconds(30),
            GlobalSourceId = new PgmGlobalSourceId(0x010203040506),
        };
    }

    private static async Task<byte[]> ReceivePacketAsync(InMemoryDatagramChannel channel, PgmPacketType type)
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(5));
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

            return buffer.AsSpan(0, byteCount).ToArray();
        }
    }
}
