# Pgm

[![CI](https://github.com/marcschier/pgm/actions/workflows/ci.yml/badge.svg)](https://github.com/marcschier/pgm/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/Pgm.svg?logo=nuget&label=NuGet)](https://www.nuget.org/packages/Pgm) [![GitHub Packages](https://img.shields.io/badge/GitHub%20Packages-Pgm-2088FF?logo=github&logoColor=white)](https://github.com/marcschier/pgm/pkgs/nuget/Pgm)

A pure-managed, **NativeAOT-ready** implementation of **PGM (Pragmatic General Multicast, [RFC 3208](https://datatracker.ietf.org/doc/html/rfc3208))** — reliable multicast — over UDP, for modern .NET. No native dependency.

- 📡 **Wire-faithful** PGM-over-UDP (SPM / ODATA / RDATA / NAK / NCF / SPMR), NAK-based repair, transmit/receive windows, FEC (Reed-Solomon), and congestion control.
- **Multi-TFM**: `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`; NativeAOT-clean on net8+.
- **Testable anywhere**: the protocol runs over a pluggable datagram channel, so it is fully exercised over an in-memory bus (with injected loss/reorder) without needing real multicast.

## 📦 Install

```shell
dotnet add package Pgm
```

## Build & test

```shell
dotnet build Pgm.slnx -c Release
dotnet test Pgm.slnx -c Release
```

## Documentation

- [Getting started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [Wire format](docs/wire-format.md)
- [API reference](docs/api.md)

## Samples

- [`samples/Pgm.Samples`](samples/README.md) — 🚀 publisher + two subscribers over the in-memory bus; runs anywhere, no real multicast.

## License

[MIT](./LICENSE)
