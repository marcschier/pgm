// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Fec;

namespace Pgm.Receiver;

internal sealed class FecGroup
{
    private readonly SortedDictionary<int, ReceivedPacket> blocks = new();

    public FecGroup(uint firstSequenceNumber, int sourceCount)
    {
        FirstSequenceNumber = firstSequenceNumber;
        SourceCount = sourceCount;
    }

    public uint FirstSequenceNumber { get; }

    public int SourceCount { get; }

    public bool CanDecode => blocks.Count >= SourceCount && GetParityCount() > 0;

    public bool TryAdd(int index, ReceivedPacket packet)
    {
        if (index < 0 || index >= 256 || blocks.ContainsKey(index))
        {
            return false;
        }

        blocks.Add(index, packet);
        return CanDecode;
    }

    public byte[][] Decode()
    {
        var received = new List<byte[]>();
        var indices = new List<int>();
        int blockSize = -1;

        foreach (KeyValuePair<int, ReceivedPacket> block in blocks)
        {
            if (blockSize < 0)
            {
                blockSize = block.Value.Data.Length;
            }

            if (block.Value.Data.Length == blockSize)
            {
                received.Add(block.Value.Data);
                indices.Add(block.Key);
            }

            if (received.Count == SourceCount)
            {
                break;
            }
        }

        return new ReedSolomon(SourceCount, Math.Max(1, GetParityCount())).Decode(received, indices);
    }

    private int GetParityCount()
    {
        int count = 0;
        foreach (int index in blocks.Keys)
        {
            if (index >= SourceCount)
            {
                count = Math.Max(count, index - SourceCount + 1);
            }
        }

        return count;
    }
}
