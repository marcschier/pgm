# Pgm

A pure-managed, **NativeAOT-ready** implementation of **PGM — Pragmatic General Multicast
([RFC 3208](https://datatracker.ietf.org/doc/html/rfc3208))** — reliable multicast over UDP, for modern .NET.

PGM lets one sender reliably deliver a byte stream to many receivers over an unreliable multicast network: lost
packets are repaired with NAK/NCF/RDATA exchanges and Reed-Solomon FEC, while a PGMCC congestion controller keeps the
source fair to the slowest receiver. This library implements that protocol with no native dependency, so it runs the
same on Windows, Linux and macOS, on `netstandard2.0` through `net10.0`.

## Documentation

- [Getting started](getting-started.md) — install, publish, subscribe.
- [Architecture](architecture.md) — sender, receiver, FEC and congestion internals.
- [Wire format](wire-format.md) — PGM-over-UDP packets and options.
- [API reference](api.md) — `PgmPublisher`, `PgmSubscriber`, and options.

## At a glance

| Feature | Status |
| --- | --- |
| SPM / ODATA / RDATA / NAK / NCF / SPMR / POLL / POLR | ✅ |
| NAK-based repair with backoff + confirmation | ✅ |
| Transmit / receive windows and fragmentation reassembly | ✅ |
| Reed-Solomon FEC (proactive + on-demand parity) | ✅ |
| PGMCC congestion control (ACKer election, rate limiting) | ✅ |
| Pluggable datagram channel (UDP multicast or in-memory) | ✅ |
| TFMs: `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0` | ✅ |
| NativeAOT (net8+) | ✅ |

```shell
dotnet add package Pgm
```
