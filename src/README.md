# src

The `Pgm` library lives here under [`Pgm/`](Pgm) — a pure-managed implementation of PGM (RFC 3208) reliable multicast
over UDP. Everything below the public facade is layered so each concern is independently testable over a single
`IPgmDatagramChannel`.

## Folders (`src/Pgm`)

| Folder | Contents |
| --- | --- |
| `Packets` | Wire-faithful codecs: common header, SPM/ODATA/RDATA/NAK/NCF/SPMR/POLL/POLR bodies, option extensions, UDP encapsulation, and the 16-bit checksum. `TryParse`/`TryWrite` over spans. |
| `Fec` | Reed-Solomon erasure coding over GF(2^8) for proactive and on-demand parity repair. |
| `Net` | `IPgmDatagramChannel` plus `UdpMulticastChannel` (real socket) and `InMemoryMulticastBus` (in-process, loss/reorder/duplicate injection). |
| `Sender` | `PgmSender`: fragments APDUs, transmit window, SPM heartbeats, NAK→RDATA repair, rate limiting. |
| `Receiver` | `PgmReceiver`: receive window, gap detection, NAK backoff/confirmation, FEC repair, fragment reassembly. |
| `Congestion` | PGMCC: ACKer election, congestion window, token-bucket rate limiter. |

## Public facade

`PgmPublisher` / `PgmSubscriber` and their options compose the layers; `PgmAddressConversion` and `PgmNetworkDefaults`
are small helpers. See the [API reference](../docs/api.md) and [architecture](../docs/architecture.md).

TFMs: `netstandard2.0;netstandard2.1;net8.0;net9.0;net10.0`; NativeAOT-clean on net8+.
