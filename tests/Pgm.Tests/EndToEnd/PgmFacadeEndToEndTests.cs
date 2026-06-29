// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using System.Threading.Channels;
using Pgm.Net;
using Pgm.Packets;

namespace Pgm.Tests.EndToEnd;

public sealed class EndToEndPgmFacadeTests
{
    [Test]
    public async Task PgmFacade_InMemoryBus_DeliversStreamToTwoSubscribers()
    {
        IMulticastBus bus = new InMemoryBusAdapter(new InMemoryMulticastBus(seed: 42));

        await RunScenarioAsync(bus, messageCount: 12);
    }

    [Test]
    public async Task PgmFacade_LossyInMemoryBus_RepairsStreamWithNakAndFec()
    {
        LossyMulticastBus bus = new();

        await RunScenarioAsync(bus, messageCount: 10);
    }

    private static async Task RunScenarioAsync(IMulticastBus bus, int messageCount)
    {
        await using PgmPublisher publisher = new PgmPublisher(bus.CreatePublisherChannel(), PublisherOptions());
        await using PgmSubscriber first = new PgmSubscriber(bus.CreateSubscriberChannel(), SubscriberOptions());
        await using PgmSubscriber second = new PgmSubscriber(bus.CreateSubscriberChannel(), SubscriberOptions());
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        await first.StartAsync(timeout.Token);
        await second.StartAsync(timeout.Token);
        await publisher.StartAsync(timeout.Token);

        for (int index = 0; index < messageCount; index++)
        {
            await publisher.PublishAsync(Encoding.UTF8.GetBytes("message-" + index), timeout.Token);
        }

        string[] expected = CreateExpectedMessages(messageCount);

        await Assert.That(await ReceiveMessagesAsync(first, messageCount, timeout.Token)).IsEquivalentTo(expected);
        await Assert.That(await ReceiveMessagesAsync(second, messageCount, timeout.Token)).IsEquivalentTo(expected);
    }

    private static PgmPublisherOptions PublisherOptions()
    {
        return new PgmPublisherOptions
        {
            SourcePathMessageInterval = TimeSpan.FromMilliseconds(100),
            FecTransmissionGroupSize = 4,
            ProactiveParityPacketCount = 0,
            OnDemandParityPacketCount = 2,
        };
    }

    private static PgmSubscriberOptions SubscriberOptions()
    {
        return new PgmSubscriberOptions
        {
            InitialNakBackoff = TimeSpan.FromMilliseconds(5),
            MaximumNakBackoff = TimeSpan.FromMilliseconds(25),
            NakConfirmationTimeout = TimeSpan.FromMilliseconds(5),
            MaximumNakAttempts = 20,
        };
    }

    private static async Task<string[]> ReceiveMessagesAsync(
        PgmSubscriber subscriber,
        int count,
        CancellationToken cancellationToken)
    {
        string[] messages = new string[count];

        for (int index = 0; index < count; index++)
        {
            byte[] message = await subscriber.ReceiveAsync(cancellationToken);
            messages[index] = Encoding.UTF8.GetString(message);
        }

        return messages;
    }

    private static string[] CreateExpectedMessages(int count)
    {
        string[] messages = new string[count];

        for (int index = 0; index < count; index++)
        {
            messages[index] = "message-" + index;
        }

        return messages;
    }

    private interface IMulticastBus
    {
        IPgmDatagramChannel CreatePublisherChannel();

        IPgmDatagramChannel CreateSubscriberChannel();
    }

    private sealed class LossyMulticastBus : IMulticastBus
    {
        private readonly object gate = new();
        private readonly List<LossyDatagramChannel> channels = new();

        public IPgmDatagramChannel CreatePublisherChannel()
        {
            return CreateChannel(false);
        }

        public IPgmDatagramChannel CreateSubscriberChannel()
        {
            return CreateChannel(true);
        }

        internal void Unregister(LossyDatagramChannel channel)
        {
            lock (gate)
            {
                channels.Remove(channel);
            }
        }

        internal void Publish(ReadOnlyMemory<byte> datagram)
        {
            lock (gate)
            {
                foreach (LossyDatagramChannel channel in channels)
                {
                    if (TryCreateSourceNak(datagram.Span, out byte[]? sourceNak))
                    {
                        if (!channel.DropOriginalData)
                        {
                            channel.Enqueue(sourceNak);
                        }

                        continue;
                    }

                    if (!ShouldDrop(channel, datagram.Span))
                    {
                        channel.Enqueue(datagram);
                    }
                }
            }
        }

        private LossyDatagramChannel CreateChannel(bool dropOriginalData)
        {
            var channel = new LossyDatagramChannel(this, dropOriginalData);

            lock (gate)
            {
                channels.Add(channel);
            }

            return channel;
        }

        private static bool ShouldDrop(LossyDatagramChannel channel, ReadOnlySpan<byte> datagram)
        {
            if (!channel.DropOriginalData
                || !PgmUdpEncapsulation.TryParsePayload(datagram, out var packet)
                || packet?.Header.Type != PgmPacketType.OriginalData
                || packet.Body is not PgmDataPacket data)
            {
                return false;
            }

            return data.SequenceNumber % 5 == 3;
        }

        private static bool TryCreateSourceNak(ReadOnlySpan<byte> datagram, out byte[]? sourceNak)
        {
            sourceNak = null;

            if (!PgmUdpEncapsulation.TryParsePayload(datagram, out var packet)
                || packet?.Header.Type != PgmPacketType.NegativeAcknowledgment
                || packet.Body is not PgmNakPacket nak)
            {
                return false;
            }

            PgmPacket sourcePacket = PgmPacket.CreateNakLike(
                new PgmHeader(
                    7500,
                    7501,
                    PgmPacketType.NegativeAcknowledgment,
                    PgmHeaderOptions.None,
                    0,
                    packet.Header.GlobalSourceId,
                    0),
                nak,
                Array.Empty<byte>());
            sourceNak = new byte[sourcePacket.EncodedLength];
            _ = PgmUdpEncapsulation.TryWritePayload(sourcePacket, sourceNak);
            return true;
        }
    }

    private sealed class LossyDatagramChannel : IPgmDatagramChannel
    {
        private readonly LossyMulticastBus bus;
        private readonly Channel<byte[]> received = Channel.CreateUnbounded<byte[]>();
        private bool disposed;

        internal LossyDatagramChannel(LossyMulticastBus bus, bool dropOriginalData)
        {
            this.bus = bus;
            DropOriginalData = dropOriginalData;
        }

        internal bool DropOriginalData { get; }

        public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bus.Publish(datagram);
            return default;
        }

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            byte[] datagram = await received.Reader.ReadAsync(cancellationToken);
            datagram.AsMemory().CopyTo(buffer);
            return datagram.Length;
        }

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                bus.Unregister(this);
                received.Writer.TryComplete();
            }

            return default;
        }

        internal void Enqueue(ReadOnlyMemory<byte> datagram)
        {
            if (!disposed)
            {
                received.Writer.TryWrite(datagram.ToArray());
            }
        }
    }

    private sealed class InMemoryBusAdapter : IMulticastBus
    {
        private readonly InMemoryMulticastBus bus;

        internal InMemoryBusAdapter(InMemoryMulticastBus bus)
        {
            this.bus = bus;
        }

        public IPgmDatagramChannel CreatePublisherChannel()
        {
            return bus.CreateChannel();
        }

        public IPgmDatagramChannel CreateSubscriberChannel()
        {
            return bus.CreateChannel();
        }
    }
}
