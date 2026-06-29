// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using Pgm.Net;

namespace Pgm.Tests.Net;

public sealed class UdpMulticastChannelTests
{
    [Test]
    public async Task Constructor_NonMulticastAddress_Throws()
    {
        await Assert.That(() => new UdpMulticastChannel(IPAddress.Parse("203.0.113.1"), 3055))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_PortOutOfRange_Throws()
    {
        await Assert.That(() => new UdpMulticastChannel(IPAddress.Parse("239.192.0.1"), 70000))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Constructor_NullAddress_Throws()
    {
        await Assert.That(() => new UdpMulticastChannel(null!, 3055)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Send_AfterDispose_Throws()
    {
        var channel = new UdpMulticastChannel(IPAddress.Parse("239.192.0.7"), 3056, IPAddress.Loopback);
        await channel.DisposeAsync();

        await Assert.That(async () => await channel.SendAsync(new byte[] { 1 })).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task SendReceive_LoopbackMulticast_RoundTrips()
    {
        var group = IPAddress.Parse("239.192.42.9");
        await using var sender = new UdpMulticastChannel(group, 3057, IPAddress.Loopback);
        await using var receiver = new UdpMulticastChannel(group, 3057, IPAddress.Loopback);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new byte[] { 9, 8, 7, 6, 5 };
        var buffer = new byte[64];
        var receive = receiver.ReceiveAsync(buffer, timeout.Token);
        for (var attempt = 0; attempt < 10 && !receive.IsCompleted; attempt++)
        {
            await sender.SendAsync(payload, timeout.Token);
            await Task.Delay(20, timeout.Token);
        }

        var length = await receive;
        await Assert.That(buffer.AsSpan(0, length).ToArray()).IsEquivalentTo(payload);
    }
}
