# tests/Pgm.Tests

[TUnit](https://github.com/thomhurst/TUnit) test suite for the `Pgm` library. Folders mirror `src/Pgm`, so a test
lives beside the area it covers.

| Folder | Covers |
| --- | --- |
| `Packets` | Header/body round-trips, options, UDP encapsulation, checksum, truncation rejection. |
| `Fec` | GF(2^8) arithmetic and Reed-Solomon encode/erasure-decode. |
| `Net` | In-memory bus delivery (loss/reorder) and UDP multicast channel argument/dispose paths. |
| `Sender` / `Receiver` | Transmit/receive windows, SPM, NAK/NCF repair, fragmentation. |
| `Congestion` | PGMCC window, rate limiter, ACKer election. |
| `EndToEnd` | Publisher↔subscriber convergence over a lossy bus. |

## Run

```shell
dotnet test tests/Pgm.Tests/Pgm.Tests.csproj -c Release -f net10.0   # or -f net8.0
```

The suite must also pass as a **NativeAOT** binary and keep line coverage ≥ 85%:

```shell
dotnet publish tests/Pgm.Tests/Pgm.Tests.csproj -c Release -f net10.0 -r win-x64 -p:AotTest=true
```

Tests are deterministic; network-free cases use `InMemoryMulticastBus`, and any real-socket test tolerates hosts
without multicast delivery.
