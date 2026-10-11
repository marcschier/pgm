# tests

[TUnit](https://github.com/thomhurst/TUnit) test suite for the `Pgm` library, in [`Pgm.Tests/`](Pgm.Tests). Folders
mirror `src/Pgm`, so a test lives beside the area it covers.

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
tests/Pgm.Tests/bin/Release/net10.0/win-x64/publish/Pgm.Tests.exe
```

`AotTest=true` enables `PublishAot`; run the published native executable to qualify the suite.
Use `linux-x64` or `osx-arm64` and the executable without `.exe` on the corresponding CI host.

Collection equivalency assertions pass an explicit `EqualityComparer<byte>.Default` or
`EqualityComparer<string>.Default` to TUnit's AOT-safe overload. This retains unordered equivalence,
duplicate multiplicity, null handling, and assertion diagnostics without reflection-based structural comparison.

```shell
dotnet test --project tests/Pgm.Tests/Pgm.Tests.csproj -c Release -f net10.0 --results-directory TestResults \
  --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
pwsh -NoProfile -File eng/check-coverage.ps1 -CoverageFile TestResults/coverage.cobertura.xml -MinLineRate 0.85
```

Tests are deterministic; network-free cases use `InMemoryMulticastBus`, and any real-socket test tolerates hosts
without multicast delivery.
