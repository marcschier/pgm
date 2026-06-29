# samples

A tiny console demo in [`Pgm.Samples/`](Pgm.Samples): one `PgmPublisher` sends three messages to two
`PgmSubscriber`s over an `InMemoryMulticastBus`, so it runs anywhere — no real multicast needed.

## Run

```shell
dotnet run --project samples/Pgm.Samples -c Release
```

(Requires the .NET 10 SDK; the sample targets `net10.0`.)

## Expected output

```text
subscriber-1 received: hello
subscriber-1 received: from
subscriber-1 received: PGM
subscriber-2 received: hello
subscriber-2 received: from
subscriber-2 received: PGM
```

To send over a real network, construct the publisher and subscriber from `PgmPublisherOptions` /
`PgmSubscriberOptions` (multicast group + port) instead of `bus.CreateChannel()` — see
[getting started](../docs/getting-started.md).
