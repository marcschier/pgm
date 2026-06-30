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

## Strong naming

The `Pgm` assembly is strong-named with a committed key (`eng/Pgm.snk`) so it can be referenced from other strong-named assemblies. A strong-name key is an identity, not a secret — committing it keeps builds reproducible and needs no CI secret wiring.

```text
Public key token: 83cf9066eeb59688
Public key:       002400000480000094000000060200000024000052534131000400000100010015BEC79DF79E711E2063918595128D0AF97FD0D0258B7FFAC1D666F4B484EB7F2BA081749D435F76B018CEA4025021F3BE3463CBA366305E0F1EAB77693CD0DECD7B6012A4A95C322DD58481310BD23EA1F0189D5DAEAF282FC76CFC5B490129618E45075A1E0FD4DEA62E1FCC919B957D1ACE892829AE49244BC94FA2BD47C0
```

Only the shipping library and its friend test assembly (`Pgm.Tests`) are signed; the samples and benchmarks use the public API only and stay unsigned.
