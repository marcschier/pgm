# Copilot instructions — Pgm

Pure-managed, NativeAOT-ready **PGM (Pragmatic General Multicast, RFC 3208)** reliable multicast over UDP. No native
dependency. Treat this file as the source of truth for conventions before generating or reviewing code.

## Project layout

| Path | Purpose |
| --- | --- |
| `src/Pgm` | The library (single `Pgm` package). Folders: `Packets`, `Fec`, `Net`, `Sender`, `Receiver`, `Congestion`; root holds the `PgmPublisher`/`PgmSubscriber` facade. |
| `tests/Pgm.Tests` | TUnit tests mirroring the source folders, plus `EndToEnd`. |
| `samples/Pgm.Samples` | Runnable publisher/subscriber demo over the in-memory bus. |
| `docs` | Developer docs (getting started, architecture, wire format, API). |
| `eng` | `check-line-length.ps1`, `check-coverage.ps1`. |

## Target frameworks

`netstandard2.0;netstandard2.1;net8.0;net9.0;net10.0`. Use the fast modern APIs (`Span`, `BinaryPrimitives`,
`Memory`-based sockets) on net8+ and guard with `#if NET8_0_OR_GREATER`, falling back to polyfills/`ArrayPool` on
netstandard. Keep everything reflection-free and AOT-clean.

## Coding conventions

- File header: `// Copyright (c) marcschier. Licensed under the MIT License.`
- File-scoped namespaces, `sealed` by default, Allman braces, ≤120 columns, LF line endings.
- Private fields use a leading underscore: `_field` (enforced by `.editorconfig`; analyzers run as warnings = errors).
- Full XML docs on public members (CS1591). Build is **0-warning**.
- `Try*`/`Span`-based codecs; no allocations on hot paths. Prefer `ValueTask`, `CancellationToken`, `IAsyncDisposable`.

## Build, test, lint

```shell
dotnet build Pgm.slnx -c Release
dotnet test tests/Pgm.Tests/Pgm.Tests.csproj -c Release -f net10.0   # or -f net8.0
dotnet format Pgm.slnx --verify-no-changes --severity warn
pwsh -NoProfile -File eng/check-line-length.ps1
```

NativeAOT gate: `dotnet publish tests/Pgm.Tests/Pgm.Tests.csproj -c Release -f net10.0 -r <rid> -p:AotTest=true`,
then run the native binary. Coverage gate ≥ 85% lines. New Windows-created files: convert CRLF→LF before formatting.

## Testing

TUnit only. Mirror the source folder per area; keep tests deterministic. Network-free tests use
`InMemoryMulticastBus` (inject loss/reorder/duplicate). Any real-socket test must tolerate hosts without multicast.

## Versioning & release

Nerdbank.GitVersioning (`version.json` = `0.9`). Pushing a `v*` tag publishes the `Pgm` package to GitHub Packages;
the manual `nuget` workflow publishes to nuget.org via OIDC. CI checkouts need `fetch-depth: 0`.
