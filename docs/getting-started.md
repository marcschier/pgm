# Getting started

## Install

```shell
dotnet add package Pgm
```

## Publish and subscribe over UDP multicast

A publisher sends application data units (APDUs) to a multicast group; every subscriber on that group receives the
same stream, with loss repaired automatically.

```csharp
using System.Net;
using System.Text;
using Pgm;

await using var publisher = new PgmPublisher(new PgmPublisherOptions
{
    MulticastGroup = IPAddress.Parse("239.192.0.1"),
    Port = 3055,
});

await publisher.PublishAsync(Encoding.UTF8.GetBytes("hello multicast"));
```

```csharp
using System.Net;
using System.Text;
using Pgm;

await using var subscriber = new PgmSubscriber(new PgmSubscriberOptions
{
    MulticastGroup = IPAddress.Parse("239.192.0.1"),
    Port = 3055,
});

byte[] payload = await subscriber.ReceiveAsync();
Console.WriteLine(Encoding.UTF8.GetString(payload));
```

`PublishAsync` and `ReceiveAsync` start the underlying state machine on first use, so an explicit `StartAsync` is
optional. Dispose both ends with `await using` to flush and release the socket.

## Run without a network (tests, demos)

The whole protocol runs over an `IPgmDatagramChannel`. `InMemoryMulticastBus` gives every publisher and subscriber a
channel that shares one in-process group — no real multicast needed — and can inject loss and reordering to exercise
repair logic:

```csharp
using Pgm;
using Pgm.Net;

var bus = new InMemoryMulticastBus(datagramLossRate: 0.1, datagramReorderRate: 0.05, seed: 42);
await using var publisher = new PgmPublisher(bus.CreateChannel());
await using var subscriber = new PgmSubscriber(bus.CreateChannel());

await publisher.PublishAsync(new byte[] { 1, 2, 3 });
byte[] received = await subscriber.ReceiveAsync();
```

## Tuning

Loss repair and FEC are tuned through options — see the [API reference](api.md). For larger payloads, raise
`MaximumDataPayloadLength`; to ride through bursty loss without round-trips, set `ProactiveParityPacketCount`.
