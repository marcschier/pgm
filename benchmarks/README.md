# Pgm.Benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) micro-benchmarks for the hot paths of the `Pgm` library: packet encode/decode, the one's-complement checksum, and Reed-Solomon FEC. Every benchmark uses `[MemoryDiagnoser]` so allocations are reported alongside timings.

## Run

```shell
# all benchmarks (full, ~minutes)
dotnet run --project benchmarks/Pgm.Benchmarks -c Release -f net10.0 -- --filter '*'

# quick run while iterating
dotnet run --project benchmarks/Pgm.Benchmarks -c Release -f net10.0 -- --filter '*' --job Short

# smoke (build + execute once, no measurement) — this is what CI runs
dotnet run --project benchmarks/Pgm.Benchmarks -c Release -f net10.0 -- --filter '*' --job Dry
```

Filter by name, e.g. `--filter '*Checksum*'`. Results are written to `BenchmarkDotNet.Artifacts/`.

## Benchmarks

| Class | Measures |
| --- | --- |
| `PacketCodecBenchmarks` | `PgmPacket` ODATA write + parse (zero-alloc span codec) across payload sizes. |
| `ChecksumBenchmarks` | `PgmChecksum.Compute` (SIMD on net8+, scalar fallback) across packet sizes. |
| `FecBenchmarks` | `ReedSolomon.Encode` (GF256 multiply-add, SSSE3/AVX2 on net8+) across group/block sizes. |

A representative captured run lives in [docs/benchmarks.md](../docs/benchmarks.md). Numbers are machine-specific; re-run locally to compare. CI runs the `--job Dry` smoke to keep the harness green without gating on timings.
