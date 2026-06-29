// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Pgm.Fec;
using Pgm.Packets;

namespace Pgm.Benchmarks;

[MemoryDiagnoser]
public class PacketCodecBenchmarks
{
    private readonly byte[] _writeBuffer = new byte[2048];
    private byte[] _odataDatagram = [];
    private byte[] _payload = [];

    [Params(64, 512, 1400)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _payload = new byte[PayloadSize];
        new Random(42).NextBytes(_payload);
        PgmHeader header = new(7500, 7501, PgmPacketType.OriginalData, PgmHeaderOptions.None, 0, new(1), (ushort)PayloadSize);
        PgmPacket packet = PgmPacket.CreateData(header, new PgmDataPacket(1, 0, _payload), ReadOnlySpan<byte>.Empty);
        _odataDatagram = new byte[packet.EncodedLength];
        _ = packet.TryWrite(_odataDatagram);
    }

    [Benchmark]
    public int WriteOdata()
    {
        PgmHeader header = new(7500, 7501, PgmPacketType.OriginalData, PgmHeaderOptions.None, 0, new(1), (ushort)PayloadSize);
        PgmPacket packet = PgmPacket.CreateData(header, new PgmDataPacket(1, 0, _payload), ReadOnlySpan<byte>.Empty);
        _ = packet.TryWrite(_writeBuffer);
        return packet.EncodedLength;
    }

    [Benchmark]
    public int ParseOdata()
    {
        _ = PgmPacket.TryParse(_odataDatagram, out var packet);
        _ = packet.TryGetData(out var data);
        return data.TsduLength;
    }
}

[MemoryDiagnoser]
public class ChecksumBenchmarks
{
    private byte[] _buffer = [];

    [Params(64, 1400, 65000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new byte[Size];
        new Random(7).NextBytes(_buffer);
    }

    [Benchmark]
    public ushort Compute() => PgmChecksum.Compute(_buffer);
}

[MemoryDiagnoser]
public class FecBenchmarks
{
    private ReedSolomon _codec = null!;
    private byte[][] _sources = [];

    [Params(8, 16)]
    public int SourceBlocks { get; set; }

    [Params(1024)]
    public int BlockSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _codec = new ReedSolomon(SourceBlocks, 4);
        _sources = new byte[SourceBlocks][];
        var random = new Random(11);
        for (int i = 0; i < SourceBlocks; i++)
        {
            _sources[i] = new byte[BlockSize];
            random.NextBytes(_sources[i]);
        }
    }

    [Benchmark]
    public byte[][] Encode() => _codec.Encode(_sources);
}
