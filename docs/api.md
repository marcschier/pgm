# API reference

The package surface is two classes plus their options. Both are `IAsyncDisposable`; dispose to release the socket.

## PgmPublisher

| Member | Description |
| --- | --- |
| `new PgmPublisher(PgmPublisherOptions)` | UDP multicast publisher. |
| `new PgmPublisher(IPgmDatagramChannel, PgmPublisherOptions?)` | Publish over any channel (e.g. in-memory). |
| `ValueTask StartAsync(CancellationToken)` | Optional; called implicitly by `PublishAsync`. |
| `ValueTask PublishAsync(ReadOnlyMemory<byte>, CancellationToken)` | Send one APDU. |

## PgmSubscriber

| Member | Description |
| --- | --- |
| `new PgmSubscriber(PgmSubscriberOptions)` | UDP multicast subscriber. |
| `new PgmSubscriber(IPgmDatagramChannel, PgmSubscriberOptions?)` | Subscribe over any channel. |
| `ValueTask<byte[]> ReceiveAsync(CancellationToken)` | Next repaired, in-order APDU. |
| `ChannelReader<byte[]> Messages` | Drain many APDUs concurrently. |

## PgmPublisherOptions

| Property | Default | Meaning |
| --- | --- | --- |
| `MulticastGroup` | `239.192.0.1` | Group address. |
| `Port` | `3055` | UDP port. |
| `InterfaceAddress` / `InterfaceIndex` | any | Outbound interface. |
| `MaximumDataPayloadLength` | `1200` | APDU bytes per packet before fragmentation. |
| `TransmitWindowPacketCount` | `1024` | Packets retained for repair. |
| `SourcePathMessageInterval` | `1s` | SPM heartbeat cadence. |
| `FecTransmissionGroupSize` | `4` | Source packets per FEC group. |
| `ProactiveParityPacketCount` | `0` | Parity sent ahead of loss. |
| `OnDemandParityPacketCount` | `1` | Parity sent when direct repair is unavailable. |

## PgmSubscriberOptions

| Property | Default | Meaning |
| --- | --- | --- |
| `MulticastGroup` | `239.192.0.1` | Group address. |
| `Port` | `3055` | UDP port. |
| `InitialNakBackoff` | `20ms` | First NAK delay. |
| `MaximumNakBackoff` | `250ms` | NAK backoff cap. |
| `NakConfirmationTimeout` | `20ms` | Wait for NCF before retransmit. |
| `MaximumNakAttempts` | `5` | NAKs before declaring loss. |

For network-free testing use `InMemoryMulticastBus` (`Pgm.Net`) with `datagramLossRate` / `datagramReorderRate` /
`datagramDuplicateRate` / `seed` and pass `bus.CreateChannel()` to each end.
