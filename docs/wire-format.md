# Wire format

Packets follow [RFC 3208](https://datatracker.ietf.org/doc/html/rfc3208) and are encapsulated in UDP (default port
`3055`). Every packet starts with the 16-byte PGM common header:

| Field | Bytes | Notes |
| --- | --- | --- |
| Source / destination port | 4 | Demultiplexing |
| Type | 1 | SPM, ODATA, RDATA, NAK, NCF, SPMR, POLL, POLR |
| Options | 1 | flags (options present, parity, var-length) |
| Checksum | 2 | 16-bit one's-complement (`PgmChecksum`) |
| Global source ID (GSI) | 6 | session identity |
| TSDU length | 2 | payload length |

Bodies implemented: **SPM** (window edges + path), **ODATA/RDATA** (sequence + fragment payload), **NAK/NNAK/NCF**
(requested sequence + source/group), **SPMR**, and **POLL/POLR** for congestion. Option extensions cover total length,
fragment, NAK list, FEC group, and parity group.

All codecs are `TryParse`/`TryWrite` over `Span<byte>` and reject truncated input. To inspect a datagram:

```csharp
using Pgm.Packets;

if (PgmUdpEncapsulation.TryParsePayload(datagram, out var packet) &&
    packet!.Header.Type == PgmPacketType.OriginalData)
{
    var data = (PgmDataPacket)packet.Body;
    Console.WriteLine($"seq {data.SequenceNumber}, {data.GetDataBytes().Length} bytes");
}
```

The 16-bit checksum is verifiable directly: `PgmChecksum.Compute(bytes)`.
