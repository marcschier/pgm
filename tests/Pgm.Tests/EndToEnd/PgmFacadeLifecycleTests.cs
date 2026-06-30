// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Net;

namespace Pgm.Tests.EndToEnd;

public sealed class PgmFacadeLifecycleTests
{
    [Test]
    public async Task Publisher_NullChannel_Throws()
    {
        await Assert.That(() => new PgmPublisher((IPgmDatagramChannel)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Publisher_StartAsync_CalledTwice_IsIdempotent()
    {
        var bus = new InMemoryMulticastBus();
        await using var publisher = new PgmPublisher(bus.CreateChannel());
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

        await publisher.StartAsync(timeout.Token);
        await publisher.StartAsync(timeout.Token);

        await Assert.That(timeout.IsCancellationRequested).IsFalse();
    }

    [Test]
    public async Task Publisher_StartAsync_AfterDispose_Throws()
    {
        var bus = new InMemoryMulticastBus();
        var publisher = new PgmPublisher(bus.CreateChannel());
        await publisher.DisposeAsync();

        await Assert.That(async () => await publisher.StartAsync()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Publisher_PublishAsync_AfterDispose_Throws()
    {
        var bus = new InMemoryMulticastBus();
        var publisher = new PgmPublisher(bus.CreateChannel());
        byte[] payload = [1];

        await publisher.DisposeAsync();
        await publisher.DisposeAsync();

        await Assert.That(async () => await publisher.PublishAsync(payload)).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Publisher_NullOptions_Throws()
    {
        await Assert.That(() => new PgmPublisher((PgmPublisherOptions)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Subscriber_NullChannel_Throws()
    {
        await Assert.That(() => new PgmSubscriber((IPgmDatagramChannel)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Subscriber_NullOptions_Throws()
    {
        await Assert.That(() => new PgmSubscriber((PgmSubscriberOptions)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Subscriber_Messages_Exposes_Reader()
    {
        var bus = new InMemoryMulticastBus();
        await using var subscriber = new PgmSubscriber(bus.CreateChannel());

        await Assert.That(subscriber.Messages).IsNotNull();
    }

    [Test]
    public async Task Subscriber_StartAsync_CalledTwice_IsIdempotent()
    {
        var bus = new InMemoryMulticastBus();
        await using var subscriber = new PgmSubscriber(bus.CreateChannel());
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

        await subscriber.StartAsync(timeout.Token);
        await subscriber.StartAsync(timeout.Token);

        await Assert.That(timeout.IsCancellationRequested).IsFalse();
    }

    [Test]
    public async Task Subscriber_StartAsync_AfterDispose_Throws()
    {
        var bus = new InMemoryMulticastBus();
        var subscriber = new PgmSubscriber(bus.CreateChannel());
        await subscriber.DisposeAsync();

        await Assert.That(async () => await subscriber.StartAsync()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Subscriber_DisposeAsync_BeforeStart_IsIdempotent()
    {
        var bus = new InMemoryMulticastBus();
        var subscriber = new PgmSubscriber(bus.CreateChannel());

        await subscriber.DisposeAsync();
        await subscriber.DisposeAsync();

        await Assert.That(async () => await subscriber.ReceiveAsync()).Throws<ObjectDisposedException>();
    }
}
