// Copyright (c) marcschier. Licensed under the MIT License.

namespace Pgm.Fec;

/// <summary>Systematic Reed-Solomon forward-error-correction encoder and erasure decoder.</summary>
public sealed class ReedSolomon
{
    private const int MaxTotalBlocks = 256;

    private readonly byte[][] _matrix;
    private readonly byte[][] _parityRows;

    /// <summary>Initializes a new instance of the <see cref="ReedSolomon"/> class.</summary>
    /// <param name="sourceBlockCount">The number of source blocks in each transmission group.</param>
    /// <param name="parityBlockCount">The number of parity blocks to generate.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sourceBlockCount"/> or <paramref name="parityBlockCount"/> is outside the supported range.
    /// </exception>
    public ReedSolomon(int sourceBlockCount, int parityBlockCount)
    {
        if (sourceBlockCount <= 0 || sourceBlockCount > MaxTotalBlocks)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceBlockCount));
        }

        if (parityBlockCount <= 0 || parityBlockCount > MaxTotalBlocks - sourceBlockCount)
        {
            throw new ArgumentOutOfRangeException(nameof(parityBlockCount));
        }

        SourceBlockCount = sourceBlockCount;
        ParityBlockCount = parityBlockCount;
        TotalBlockCount = sourceBlockCount + parityBlockCount;

        _matrix = BuildSystematicMatrix(SourceBlockCount, TotalBlockCount);
        _parityRows = new byte[ParityBlockCount][];

        for (int index = 0; index < ParityBlockCount; index++)
        {
            _parityRows[index] = _matrix[SourceBlockCount + index];
        }
    }

    /// <summary>Gets the number of source blocks in each transmission group.</summary>
    public int SourceBlockCount { get; }

    /// <summary>Gets the number of parity blocks generated for each transmission group.</summary>
    public int ParityBlockCount { get; }

    /// <summary>Gets the total number of source and parity blocks in each transmission group.</summary>
    public int TotalBlockCount { get; }

    /// <summary>Encodes source blocks into parity blocks.</summary>
    /// <param name="sourceBlocks">Exactly <see cref="SourceBlockCount"/> fixed-size source blocks.</param>
    /// <returns>Exactly <see cref="ParityBlockCount"/> parity blocks.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sourceBlocks"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The block count or block sizes are invalid.</exception>
    public byte[][] Encode(IReadOnlyList<byte[]> sourceBlocks)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sourceBlocks);
#else
        if (sourceBlocks is null)
        {
            throw new ArgumentNullException(nameof(sourceBlocks));
        }
#endif

        int blockSize = ValidateSourceBlocks(sourceBlocks);
        byte[][] parityBlocks = CreateBlocks(ParityBlockCount, blockSize);

        for (int parityIndex = 0; parityIndex < parityBlocks.Length; parityIndex++)
        {
            Span<byte> parity = parityBlocks[parityIndex];
            byte[] parityRow = _parityRows[parityIndex];

            for (int sourceIndex = 0; sourceIndex < SourceBlockCount; sourceIndex++)
            {
                Gf256.MultiplyAdd(sourceBlocks[sourceIndex], parity, parityRow[sourceIndex]);
            }
        }

        return parityBlocks;
    }

    /// <summary>Decodes source blocks from any valid set of received source and parity blocks.</summary>
    /// <param name="receivedBlocks">At least <see cref="SourceBlockCount"/> fixed-size received blocks.</param>
    /// <param name="presentIndices">
    /// The transmission-group indices for <paramref name="receivedBlocks"/>. Source indices are zero-based and parity
    /// indices continue after the last source block.
    /// </param>
    /// <returns>Exactly <see cref="SourceBlockCount"/> reconstructed source blocks.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="receivedBlocks"/> or <paramref name="presentIndices"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">The received block count, indices, or block sizes are invalid.</exception>
    public byte[][] Decode(IReadOnlyList<byte[]> receivedBlocks, IReadOnlyList<int> presentIndices)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(receivedBlocks);
        ArgumentNullException.ThrowIfNull(presentIndices);
#else
        if (receivedBlocks is null)
        {
            throw new ArgumentNullException(nameof(receivedBlocks));
        }

        if (presentIndices is null)
        {
            throw new ArgumentNullException(nameof(presentIndices));
        }
#endif

        int blockSize = ValidateReceivedBlocks(receivedBlocks, presentIndices);
        byte[][] decodeMatrix = new byte[SourceBlockCount][];
        byte[][] selectedBlocks = new byte[SourceBlockCount][];

        for (int row = 0; row < SourceBlockCount; row++)
        {
            int presentIndex = presentIndices[row];
            decodeMatrix[row] = CopyRow(_matrix[presentIndex]);
            selectedBlocks[row] = receivedBlocks[row];
        }

        byte[][] inverse = Invert(decodeMatrix);
        byte[][] sourceBlocks = CreateBlocks(SourceBlockCount, blockSize);

        for (int sourceIndex = 0; sourceIndex < SourceBlockCount; sourceIndex++)
        {
            Span<byte> source = sourceBlocks[sourceIndex];
            byte[] inverseRow = inverse[sourceIndex];

            for (int receivedIndex = 0; receivedIndex < SourceBlockCount; receivedIndex++)
            {
                Gf256.MultiplyAdd(selectedBlocks[receivedIndex], source, inverseRow[receivedIndex]);
            }
        }

        return sourceBlocks;
    }

    private static byte[][] BuildSystematicMatrix(int sourceBlockCount, int totalBlockCount)
    {
        byte[][] vandermonde = CreateVandermondeMatrix(totalBlockCount, sourceBlockCount);
        byte[][] top = new byte[sourceBlockCount][];

        for (int row = 0; row < sourceBlockCount; row++)
        {
            top[row] = CopyRow(vandermonde[row]);
        }

        byte[][] inverseTop = Invert(top);
        return Multiply(vandermonde, inverseTop);
    }

    private static byte[][] CreateVandermondeMatrix(int rowCount, int columnCount)
    {
        byte[][] matrix = new byte[rowCount][];

        for (int row = 0; row < rowCount; row++)
        {
            matrix[row] = new byte[columnCount];

            for (int column = 0; column < columnCount; column++)
            {
                matrix[row][column] = Gf256.Pow((byte)row, column);
            }
        }

        return matrix;
    }

    private static byte[][] Multiply(byte[][] left, byte[][] right)
    {
        byte[][] result = new byte[left.Length][];
        int columnCount = right[0].Length;
        int sharedCount = right.Length;

        for (int row = 0; row < left.Length; row++)
        {
            result[row] = new byte[columnCount];

            for (int column = 0; column < columnCount; column++)
            {
                byte value = 0;

                for (int shared = 0; shared < sharedCount; shared++)
                {
                    value ^= Gf256.Multiply(left[row][shared], right[shared][column]);
                }

                result[row][column] = value;
            }
        }

        return result;
    }

    private static byte[][] Invert(byte[][] matrix)
    {
        int size = matrix.Length;
        byte[][] inverse = CreateIdentity(size);

        for (int column = 0; column < size; column++)
        {
            int pivotRow = column;

            while (pivotRow < size && matrix[pivotRow][column] == 0)
            {
                pivotRow++;
            }

            if (pivotRow == size)
            {
                throw new ArgumentException("The selected blocks do not form an invertible Reed-Solomon matrix.");
            }

            if (pivotRow != column)
            {
                Swap(matrix, pivotRow, column);
                Swap(inverse, pivotRow, column);
            }

            byte pivot = matrix[column][column];

            if (pivot != 1)
            {
                byte scale = Gf256.Inverse(pivot);
                ScaleRow(matrix[column], scale);
                ScaleRow(inverse[column], scale);
            }

            for (int row = 0; row < size; row++)
            {
                if (row == column)
                {
                    continue;
                }

                byte factor = matrix[row][column];

                if (factor == 0)
                {
                    continue;
                }

                AddScaledRow(matrix[column], matrix[row], factor);
                AddScaledRow(inverse[column], inverse[row], factor);
            }
        }

        return inverse;
    }

    private static byte[][] CreateIdentity(int size)
    {
        byte[][] matrix = new byte[size][];

        for (int row = 0; row < size; row++)
        {
            matrix[row] = new byte[size];
            matrix[row][row] = 1;
        }

        return matrix;
    }

    private static void ScaleRow(byte[] row, byte factor)
    {
        for (int column = 0; column < row.Length; column++)
        {
            row[column] = Gf256.Multiply(row[column], factor);
        }
    }

    private static void AddScaledRow(byte[] source, byte[] destination, byte factor)
    {
        for (int column = 0; column < destination.Length; column++)
        {
            destination[column] ^= Gf256.Multiply(source[column], factor);
        }
    }

    private static byte[] CopyRow(byte[] row)
    {
        byte[] copy = new byte[row.Length];
        row.AsSpan().CopyTo(copy);
        return copy;
    }

    private static void Swap(byte[][] matrix, int left, int right)
    {
        byte[] row = matrix[left];
        matrix[left] = matrix[right];
        matrix[right] = row;
    }

    private static byte[][] CreateBlocks(int count, int blockSize)
    {
        byte[][] blocks = new byte[count][];

        for (int index = 0; index < blocks.Length; index++)
        {
            blocks[index] = new byte[blockSize];
        }

        return blocks;
    }

    private int ValidateSourceBlocks(IReadOnlyList<byte[]> sourceBlocks)
    {
        if (sourceBlocks.Count != SourceBlockCount)
        {
            throw new ArgumentException("The number of source blocks does not match the codec.", nameof(sourceBlocks));
        }

        return ValidateBlockSizes(sourceBlocks, SourceBlockCount, nameof(sourceBlocks));
    }

    private int ValidateReceivedBlocks(IReadOnlyList<byte[]> receivedBlocks, IReadOnlyList<int> presentIndices)
    {
        if (receivedBlocks.Count < SourceBlockCount)
        {
            throw new ArgumentException("Not enough blocks were received to decode.", nameof(receivedBlocks));
        }

        if (presentIndices.Count < SourceBlockCount)
        {
            throw new ArgumentException("Not enough present indices were supplied.", nameof(presentIndices));
        }

        if (receivedBlocks.Count != presentIndices.Count)
        {
            throw new ArgumentException("The received block and index counts must match.", nameof(presentIndices));
        }

        bool[] used = new bool[TotalBlockCount];

        for (int index = 0; index < presentIndices.Count; index++)
        {
            int presentIndex = presentIndices[index];

            if ((uint)presentIndex >= (uint)TotalBlockCount)
            {
                throw new ArgumentException(
                    "A present index is outside the transmission group.",
                    nameof(presentIndices));
            }

            if (used[presentIndex])
            {
                throw new ArgumentException("Present indices must be unique.", nameof(presentIndices));
            }

            used[presentIndex] = true;
        }

        return ValidateBlockSizes(receivedBlocks, receivedBlocks.Count, nameof(receivedBlocks));
    }

    private static int ValidateBlockSizes(IReadOnlyList<byte[]> blocks, int count, string paramName)
    {
        if (blocks[0] is null)
        {
            throw new ArgumentException("Blocks cannot be null.", paramName);
        }

        int blockSize = blocks[0].Length;

        for (int index = 1; index < count; index++)
        {
            if (blocks[index] is null)
            {
                throw new ArgumentException("Blocks cannot be null.", paramName);
            }

            if (blocks[index].Length != blockSize)
            {
                throw new ArgumentException("Blocks must all have the same size.", paramName);
            }
        }

        return blockSize;
    }
}
