// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using Pgm.Net;

namespace Pgm.Tests.Net;

public sealed class InMemoryMulticastBusTests
{
    [Test]
    public async Task FanOut_Delivers_Datagram_To_All_Channels()
    {
        InMemoryMulticastBus bus = new(seed: 42);
        await using InMemoryDatagramChannel first = bus.CreateChannel();
        await using InMemoryDatagramChannel second = bus.CreateChannel();
        byte[] sent = Encoding.UTF8.GetBytes("hello");

        await first.SendAsync(sent);

        await Assert.That(await ReceiveStringAsync(first)).IsEqualTo("hello");
        await Assert.That(await ReceiveStringAsync(second)).IsEqualTo("hello");
    }

    [Test]
    public async Task LossInjection_Drops_Datagram()
    {
        InMemoryMulticastBus bus = new(datagramLossRate: 1, seed: 42);
        await using InMemoryDatagramChannel sender = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();
        byte[] sent = Encoding.UTF8.GetBytes("lost");

        await sender.SendAsync(sent);

        using CancellationTokenSource cancellation = new();
        CancelSoon(cancellation);
        bool canceled = false;

        try
        {
            await ReceiveStringAsync(receiver, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        await Assert.That(canceled).IsTrue();
    }

    [Test]
    public async Task ReorderInjection_Reorders_Datagrams()
    {
        InMemoryMulticastBus bus = new(datagramReorderRate: 1, seed: 42);
        await using InMemoryDatagramChannel sender = bus.CreateChannel();
        await using InMemoryDatagramChannel receiver = bus.CreateChannel();

        await sender.SendAsync(Encoding.UTF8.GetBytes("one"));
        await sender.SendAsync(Encoding.UTF8.GetBytes("two"));

        await Assert.That(await ReceiveStringAsync(receiver)).IsEqualTo("two");
        await Assert.That(await ReceiveStringAsync(receiver)).IsEqualTo("one");
    }

    private static async Task<string> ReceiveStringAsync(
        InMemoryDatagramChannel channel,
        CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[32];
        int byteCount = await channel.ReceiveAsync(buffer, cancellationToken);
        return Encoding.UTF8.GetString(buffer, 0, byteCount);
    }

    private static void CancelSoon(CancellationTokenSource cancellation)
    {
#if NET8_0_OR_GREATER
        cancellation.CancelAsync();
#else
        cancellation.Cancel();
#endif
    }
}
