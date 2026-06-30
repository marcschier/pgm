// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Net;
using Pgm.Receiver;

namespace Pgm.Tests.Receiver;

public sealed class PgmReceiverLifecycleTests
{
    [Test]
    public async Task Constructor_NullChannel_Throws()
    {
        await Assert.That(() => new PgmReceiver(null!, TimeProvider.System)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullTimeProvider_Throws()
    {
        var bus = new InMemoryMulticastBus();

        await Assert.That(() => new PgmReceiver(bus.CreateChannel(), null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StartAsync_CalledTwice_Throws()
    {
        await using PgmReceiver receiver = CreateReceiver();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

        await receiver.StartAsync(timeout.Token);

        await Assert.That(async () => await receiver.StartAsync(timeout.Token))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StartAsync_AfterDispose_Throws()
    {
        PgmReceiver receiver = CreateReceiver();
        await receiver.DisposeAsync();

        await Assert.That(async () => await receiver.StartAsync()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ReceiveAsync_AfterDispose_Throws()
    {
        PgmReceiver receiver = CreateReceiver();
        await receiver.DisposeAsync();

        await Assert.That(async () => await receiver.ReceiveAsync()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task DisposeAsync_BeforeStart_IsIdempotent()
    {
        PgmReceiver receiver = CreateReceiver();

        await receiver.DisposeAsync();
        await receiver.DisposeAsync();

        await Assert.That(async () => await receiver.ReceiveAsync()).Throws<ObjectDisposedException>();
    }

    private static PgmReceiver CreateReceiver()
    {
        var bus = new InMemoryMulticastBus();
        return new PgmReceiver(bus.CreateChannel(), TimeProvider.System);
    }
}
