# Architecture

The library is layered so each concern is independently testable. The public facade composes them; everything below
the facade operates on a single `IPgmDatagramChannel` abstraction and never touches sockets directly.

```
PgmPublisher / PgmSubscriber          (facade: lifecycle, APDU queueing)
        |                    \
   PgmSender                PgmReceiver
        |        \              |        \
  TransmitWindow  Pgmcc    ReceiveWindow  NakState / FecGroup
        |                       |
        +------- Packets -------+        (SPM/ODATA/RDATA/NAK/NCF/SPMR/POLL/POLR + options)
        +-------- Fec ----------+        (GF(256), Reed-Solomon)
        +---- IPgmDatagramChannel -------+ (UdpMulticastChannel | InMemoryMulticastBus)
```

## Packets (`Pgm.Packets`)

Wire-faithful codecs for the PGM common header and every packet body, UDP encapsulation, the PGM 16-bit checksum, and
option extensions (length, fragment, NAK list, FEC, parity-group). Each codec is `TryParse`/`TryWrite` over spans —
allocation-free on the hot path and AOT-safe. See [wire format](wire-format.md).

## Datagram channel (`Pgm.Net`)

`IPgmDatagramChannel` is the seam between protocol and network: a duplex datagram pipe. `UdpMulticastChannel` binds a
real multicast socket (using `Memory`-based socket APIs on net8+, pooled `ArraySegment` fallback on netstandard).
`InMemoryMulticastBus` fans out datagrams to all members in-process and can drop, reorder and duplicate them
deterministically — the foundation for fast, network-free repair tests.

## Sender (`Pgm.Sender`)

`PgmSender` fragments APDUs into ODATA, retains them in a `TransmitWindow`, advances the trailing edge, beats SPM
heartbeats, answers NAKs with RDATA (or parity), and paces output through the congestion limiter.

## Receiver (`Pgm.Receiver`)

`PgmReceiver` tracks the window per source, detects gaps, schedules NAKs with backoff and NCF confirmation, repairs
from RDATA or FEC, reassembles fragments into APDUs and delivers them in order.

## FEC (`Pgm.Fec`)

`Gf256` and `ReedSolomon` provide systematic erasure coding so a group survives up to *k* losses without a round-trip
when proactive parity is enabled, and lets a single RDATA repair many receivers' distinct losses.

## Congestion (`Pgm.Congestion`)

PGMCC: receivers report loss/throughput/RTT, the worst becomes the ACKer, and a TCP-friendly `CongestionWindow` plus
token-bucket `RateLimiter` cap the source rate so the slowest receiver stays fair.
