// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Fec;

namespace Pgm.Tests.Fec;

public sealed class ReedSolomonTests
{
    [Test]
    public async Task Decode_WithAllSourcesPresent_ReturnsOriginalSources()
    {
        var codec = new ReedSolomon(4, 2);
        byte[][] sources = CreateSourceBlocks(4, 17, 123);
        byte[][] decoded = codec.Decode(sources, [0, 1, 2, 3]);

        await AssertBlocksEqual(sources, decoded);
    }

    [Test]
    public async Task Decode_WithAllParityNeeded_ReturnsOriginalSources()
    {
        var codec = new ReedSolomon(4, 4);
        byte[][] sources = CreateSourceBlocks(4, 23, 456);
        byte[][] parity = codec.Encode(sources);
        byte[][] received =
        [
            sources[0],
            parity[0],
            parity[1],
            parity[2],
        ];

        byte[][] decoded = codec.Decode(received, [0, 4, 5, 6]);

        await AssertBlocksEqual(sources, decoded);
    }

    [Test]
    public async Task Encode_WithSystematicMatrix_DoesNotChangeSourceBlocks()
    {
        var codec = new ReedSolomon(6, 3);
        byte[][] sources = CreateSourceBlocks(6, 31, 789);
        byte[][] snapshot = CloneBlocks(sources);

        _ = codec.Encode(sources);

        await AssertBlocksEqual(snapshot, sources);
    }

    [Test]
    public async Task Decode_WithRandomErasuresUpToParityCount_ReturnsOriginalSources()
    {
        var random = new Random(98765);

        for (int iteration = 0; iteration < 100; iteration++)
        {
            var codec = new ReedSolomon(10, 4);
            byte[][] sources = CreateSourceBlocks(10, 64 + iteration, random.Next());
            byte[][] parity = codec.Encode(sources);
            byte[][] transmitted = Combine(sources, parity);
            int[] indices = ChoosePresentIndices(10, 4, random);
            byte[][] received = SelectBlocks(transmitted, indices);

            byte[][] decoded = codec.Decode(received, indices);

            await AssertBlocksEqual(sources, decoded);
        }
    }

    [Test]
    public async Task Decode_WithMoreThanSourceBlockCountReceived_UsesFirstDecodableSet()
    {
        var codec = new ReedSolomon(3, 2);
        byte[][] sources = CreateSourceBlocks(3, 13, 321);
        byte[][] parity = codec.Encode(sources);
        byte[][] received =
        [
            sources[0],
            parity[0],
            sources[2],
            parity[1],
        ];

        byte[][] decoded = codec.Decode(received, [0, 3, 2, 4]);

        await AssertBlocksEqual(sources, decoded);
    }

    private static byte[][] CreateSourceBlocks(int count, int blockSize, int seed)
    {
        var random = new Random(seed);
        byte[][] blocks = new byte[count][];

        for (int block = 0; block < blocks.Length; block++)
        {
            blocks[block] = new byte[blockSize];
            random.NextBytes(blocks[block]);
        }

        return blocks;
    }

    private static byte[][] CloneBlocks(byte[][] blocks)
    {
        byte[][] clone = new byte[blocks.Length][];

        for (int index = 0; index < blocks.Length; index++)
        {
            clone[index] = new byte[blocks[index].Length];
            blocks[index].AsSpan().CopyTo(clone[index]);
        }

        return clone;
    }

    private static byte[][] Combine(byte[][] sources, byte[][] parity)
    {
        byte[][] combined = new byte[sources.Length + parity.Length][];
        Array.Copy(sources, 0, combined, 0, sources.Length);
        Array.Copy(parity, 0, combined, sources.Length, parity.Length);
        return combined;
    }

    private static byte[][] SelectBlocks(byte[][] blocks, int[] indices)
    {
        byte[][] selected = new byte[indices.Length][];

        for (int index = 0; index < indices.Length; index++)
        {
            selected[index] = blocks[indices[index]];
        }

        return selected;
    }

    private static int[] ChoosePresentIndices(int sourceCount, int parityCount, Random random)
    {
        int totalCount = sourceCount + parityCount;
        int erasureCount = random.Next(parityCount + 1);
        bool[] erased = new bool[totalCount];

        for (int count = 0; count < erasureCount; count++)
        {
            int erasedIndex;

            do
            {
                erasedIndex = random.Next(totalCount);
            }
            while (erased[erasedIndex]);

            erased[erasedIndex] = true;
        }

        int[] present = new int[sourceCount];
        int presentIndex = 0;

        for (int index = 0; index < totalCount && presentIndex < present.Length; index++)
        {
            if (!erased[index])
            {
                present[presentIndex] = index;
                presentIndex++;
            }
        }

        return present;
    }

    private static async Task AssertBlocksEqual(byte[][] expected, byte[][] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);

        for (int index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Length).IsEqualTo(expected[index].Length);

            for (int offset = 0; offset < expected[index].Length; offset++)
            {
                await Assert.That(actual[index][offset]).IsEqualTo(expected[index][offset]);
            }
        }
    }
}
