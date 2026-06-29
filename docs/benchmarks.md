# Benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) results for the `Pgm` hot paths. The harness lives in [`benchmarks/Pgm.Benchmarks`](../benchmarks/Pgm.Benchmarks/README.md) and every benchmark uses `[MemoryDiagnoser]`.

Reproduce with:

```shell
dotnet run --project benchmarks/Pgm.Benchmarks -c Release -f net10.0 -- --filter '*'
```

## Environment

```
BenchmarkDotNet v0.14.0, Windows
.NET 10.0.9 (X64 RyuJIT AVX-512F+CD+BW+DQ+VL)
Job=ShortRun (quick run; absolute numbers are noisy, the allocation column is the headline)
```

Numbers below are machine-specific and from a `--job Short` run, so timings carry wide error margins. The point is the **Allocated** column: the codec and checksum hot paths are zero-allocation, and FEC only allocates its parity output blocks.

## Packet codec — `PgmPacket` write/parse (ODATA)

| Method     | PayloadSize | Mean     | Allocated |
| ---------- | ----------- | -------- | --------- |
| WriteOdata | 64          | 29.9 ns  | 0 B       |
| ParseOdata | 64          | 32.8 ns  | 0 B       |
| WriteOdata | 512         | 40.6 ns  | 0 B       |
| ParseOdata | 512         | 35.2 ns  | 0 B       |
| WriteOdata | 1400        | 49.0 ns  | 0 B       |
| ParseOdata | 1400        | 28.9 ns  | 0 B       |

Encode/decode are zero-alloc — `PgmPacket` is a `readonly ref struct` and payload stays a `ReadOnlySpan<byte>`.

## Checksum — `PgmChecksum.Compute` (SIMD on net8+)

| Method  | Size  | Mean      | Allocated |
| ------- | ----- | --------- | --------- |
| Compute | 64    | 35.5 ns   | 0 B       |
| Compute | 1400  | 453 ns    | 0 B       |
| Compute | 65000 | 15.7 µs   | 0 B       |

## FEC — `ReedSolomon.Encode` (GF256 SSSE3/AVX2 on net8+)

| Method | SourceBlocks | BlockSize | Mean    | Allocated |
| ------ | ------------ | --------- | ------- | --------- |
| Encode | 8            | 1024      | 6.0 µs  | 4.15 KB   |
| Encode | 16           | 1024      | 9.9 µs  | 4.15 KB   |

`Encode` allocates only the parity output arrays; the multiply-add inner loop is vectorized and allocation-free.

CI runs the same suite as a `--job Dry` smoke (build + execute once) so it never gates on timings.
