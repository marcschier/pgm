// Copyright (c) marcschier. Licensed under the MIT License.

#if NET8_0_OR_GREATER
using System.Numerics;
#endif

namespace Pgm.Packets;

/// <summary>Computes the RFC 3208 one's-complement checksum over a PGM packet.</summary>
public static class PgmChecksum
{
#if NET8_0_OR_GREATER
    private const int MaxVectorBytesPerBlock = 64 * 1024;

    private static readonly Vector<byte> EvenByteMask = CreateParityMask(0);
    private static readonly Vector<byte> OddByteMask = CreateParityMask(1);
#endif

    /// <summary>Computes the checksum value for a PGM packet.</summary>
    /// <param name="packet">The complete encoded PGM packet with the checksum field cleared.</param>
    /// <returns>The checksum to write to the fixed PGM header.</returns>
    public static ushort Compute(ReadOnlySpan<byte> packet)
    {
#if NET8_0_OR_GREATER
        if (Vector.IsHardwareAccelerated && (Vector<byte>.Count & 1) == 0 && packet.Length >= Vector<byte>.Count)
        {
            return ComputeVectorized(packet);
        }
#endif

        return ComputeScalar(packet);
    }

    private static ushort ComputeScalar(ReadOnlySpan<byte> packet)
    {
        ulong sum = 0;
        var offset = 0;
        while (offset + 1 < packet.Length)
        {
            sum += (uint)((packet[offset] << 8) | packet[offset + 1]);
            offset += 2;
        }

        if (offset < packet.Length)
        {
            sum += (uint)(packet[offset] << 8);
        }

        return Finish(sum);
    }

#if NET8_0_OR_GREATER
    private static ushort ComputeVectorized(ReadOnlySpan<byte> packet)
    {
        ulong sum = 0;
        var offset = 0;
        int vectorByteCount = Vector<byte>.Count;

        while (offset + vectorByteCount <= packet.Length)
        {
            Vector<uint> evenSum = Vector<uint>.Zero;
            Vector<uint> oddSum = Vector<uint>.Zero;
            int blockEnd = Math.Min(packet.Length - vectorByteCount, offset + MaxVectorBytesPerBlock - vectorByteCount);

            do
            {
                AddByteSums(new Vector<byte>(packet.Slice(offset, vectorByteCount)), ref evenSum, ref oddSum);
                offset += vectorByteCount;
            }
            while (offset <= blockEnd);

            sum += (Reduce(evenSum) << 8) + Reduce(oddSum);
        }

        while (offset + 1 < packet.Length)
        {
            sum += (uint)((packet[offset] << 8) | packet[offset + 1]);
            offset += 2;
        }

        if (offset < packet.Length)
        {
            sum += (uint)(packet[offset] << 8);
        }

        return Finish(sum);
    }

    private static Vector<byte> CreateParityMask(int parity)
    {
        Span<byte> mask = stackalloc byte[Vector<byte>.Count];
        for (var index = parity; index < mask.Length; index += 2)
        {
            mask[index] = byte.MaxValue;
        }

        return new Vector<byte>(mask);
    }

    private static void AddByteSums(Vector<byte> bytes, ref Vector<uint> evenSum, ref Vector<uint> oddSum)
    {
        AddMaskedByteSums(bytes & EvenByteMask, ref evenSum);
        AddMaskedByteSums(bytes & OddByteMask, ref oddSum);
    }

    private static void AddMaskedByteSums(Vector<byte> bytes, ref Vector<uint> sum)
    {
        Vector.Widen(bytes, out Vector<ushort> lower, out Vector<ushort> upper);
        AddUShortSums(lower, ref sum);
        AddUShortSums(upper, ref sum);
    }

    private static void AddUShortSums(Vector<ushort> values, ref Vector<uint> sum)
    {
        Vector.Widen(values, out Vector<uint> lower, out Vector<uint> upper);
        sum += lower;
        sum += upper;
    }

    private static ulong Reduce(Vector<uint> values)
    {
        ulong sum = 0;
        for (var index = 0; index < Vector<uint>.Count; index++)
        {
            sum += values[index];
        }

        return sum;
    }

#endif

    private static ushort Finish(ulong sum)
    {
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        var checksum = (ushort)~sum;
        return checksum == 0 ? ushort.MaxValue : checksum;
    }
}
