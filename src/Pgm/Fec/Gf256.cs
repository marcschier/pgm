// Copyright (c) marcschier. Licensed under the MIT License.

#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Pgm.Fec;

/// <summary>Arithmetic over GF(2^8) using the RFC 3208 Reed-Solomon field polynomial.</summary>
public static class Gf256
{
    private const int FieldSize = 256;
    private const int FieldMask = 255;
    private const int PrimitivePolynomial = 0x11d;

    private static readonly byte[] Exp = CreateExpTable();
    private static readonly byte[] Log = CreateLogTable();

    /// <summary>Adds two field elements.</summary>
    /// <param name="left">The left field element.</param>
    /// <param name="right">The right field element.</param>
    /// <returns>The sum of <paramref name="left"/> and <paramref name="right"/>.</returns>
    public static byte Add(byte left, byte right)
    {
        return (byte)(left ^ right);
    }

    /// <summary>Subtracts one field element from another.</summary>
    /// <param name="left">The left field element.</param>
    /// <param name="right">The right field element.</param>
    /// <returns>The difference of <paramref name="left"/> and <paramref name="right"/>.</returns>
    public static byte Subtract(byte left, byte right)
    {
        return (byte)(left ^ right);
    }

    /// <summary>Multiplies two field elements.</summary>
    /// <param name="left">The left field element.</param>
    /// <param name="right">The right field element.</param>
    /// <returns>The product of <paramref name="left"/> and <paramref name="right"/>.</returns>
    public static byte Multiply(byte left, byte right)
    {
        if (left == 0 || right == 0)
        {
            return 0;
        }

        return Exp[Log[left] + Log[right]];
    }

    /// <summary>Divides one field element by another.</summary>
    /// <param name="left">The dividend field element.</param>
    /// <param name="right">The divisor field element.</param>
    /// <returns>The quotient of <paramref name="left"/> divided by <paramref name="right"/>.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    public static byte Divide(byte left, byte right)
    {
        if (right == 0)
        {
            throw new DivideByZeroException();
        }

        if (left == 0)
        {
            return 0;
        }

        return Exp[Log[left] - Log[right] + FieldMask];
    }

    /// <summary>Raises a field element to an integer power.</summary>
    /// <param name="value">The field element.</param>
    /// <param name="power">The non-negative exponent.</param>
    /// <returns><paramref name="value"/> raised to <paramref name="power"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="power"/> is negative.</exception>
    public static byte Pow(byte value, int power)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(power);
#else
        if (power < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(power));
        }
#endif

        if (power == 0)
        {
            return 1;
        }

        if (value == 0)
        {
            return 0;
        }

        return Exp[(Log[value] * power) % FieldMask];
    }

    /// <summary>Gets the multiplicative inverse of a non-zero field element.</summary>
    /// <param name="value">The field element.</param>
    /// <returns>The multiplicative inverse of <paramref name="value"/>.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="value"/> is zero.</exception>
    public static byte Inverse(byte value)
    {
        if (value == 0)
        {
            throw new DivideByZeroException();
        }

        return Exp[FieldMask - Log[value]];
    }

    internal static void MultiplyAdd(ReadOnlySpan<byte> source, Span<byte> destination, byte coefficient)
    {
        if (coefficient == 0)
        {
            return;
        }

        if (source.Length < destination.Length)
        {
            MultiplyAddScalar(source, destination, coefficient);
            return;
        }

#if NET8_0_OR_GREATER
        if (Ssse3.IsSupported && !source.Overlaps(destination))
        {
            if (coefficient == 1)
            {
                XorVectorized(source, destination);
            }
            else
            {
                MultiplyAddVectorized(source, destination, coefficient);
            }

            return;
        }
#endif

        MultiplyAddScalar(source, destination, coefficient);
    }

    internal static void MultiplyAddScalar(ReadOnlySpan<byte> source, Span<byte> destination, byte coefficient)
    {
        if (coefficient == 0)
        {
            return;
        }

        if (coefficient == 1)
        {
            for (int index = 0; index < destination.Length; index++)
            {
                destination[index] ^= source[index];
            }

            return;
        }

        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] ^= Multiply(source[index], coefficient);
        }
    }

#if NET8_0_OR_GREATER
    private static void XorVectorized(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int index = 0;
        ref readonly byte sourceRef = ref MemoryMarshal.GetReference(source);
        ref byte destinationRef = ref MemoryMarshal.GetReference(destination);

        if (Avx2.IsSupported)
        {
            for (; index <= destination.Length - Vector256<byte>.Count; index += Vector256<byte>.Count)
            {
                Vector256<byte> sourceVector = Vector256.LoadUnsafe(in sourceRef, (nuint)index);
                Vector256<byte> destinationVector = Vector256.LoadUnsafe(ref destinationRef, (nuint)index);
                Avx2.Xor(sourceVector, destinationVector).StoreUnsafe(ref destinationRef, (nuint)index);
            }
        }

        for (; index <= destination.Length - Vector128<byte>.Count; index += Vector128<byte>.Count)
        {
            Vector128<byte> sourceVector = Vector128.LoadUnsafe(in sourceRef, (nuint)index);
            Vector128<byte> destinationVector = Vector128.LoadUnsafe(ref destinationRef, (nuint)index);
            Sse2.Xor(sourceVector, destinationVector).StoreUnsafe(ref destinationRef, (nuint)index);
        }

        for (; index < destination.Length; index++)
        {
            destination[index] ^= source[index];
        }
    }

    private static void MultiplyAddVectorized(ReadOnlySpan<byte> source, Span<byte> destination, byte coefficient)
    {
        Span<byte> shuffleTables = stackalloc byte[64];

        for (int nibble = 0; nibble < 16; nibble++)
        {
            byte low = Multiply((byte)nibble, coefficient);
            byte high = Multiply((byte)(nibble << 4), coefficient);

            shuffleTables[nibble] = low;
            shuffleTables[16 + nibble] = low;
            shuffleTables[32 + nibble] = high;
            shuffleTables[48 + nibble] = high;
        }

        int index = 0;
        ref byte tableRef = ref MemoryMarshal.GetReference(shuffleTables);
        ref readonly byte sourceRef = ref MemoryMarshal.GetReference(source);
        ref byte destinationRef = ref MemoryMarshal.GetReference(destination);

        if (Avx2.IsSupported)
        {
            Vector256<byte> lowTable = Vector256.LoadUnsafe(ref tableRef);
            Vector256<byte> highTable = Vector256.LoadUnsafe(ref Unsafe.Add(ref tableRef, 32));
            Vector256<byte> lowMask = Vector256.Create((byte)0x0f);

            for (; index <= destination.Length - Vector256<byte>.Count; index += Vector256<byte>.Count)
            {
                Vector256<byte> sourceVector = Vector256.LoadUnsafe(in sourceRef, (nuint)index);
                Vector256<byte> lowNibbles = Avx2.And(sourceVector, lowMask);
                Vector256<byte> highNibbles = Avx2.And(
                    Avx2.ShiftRightLogical(sourceVector.AsUInt16(), 4).AsByte(),
                    lowMask);
                Vector256<byte> product = Avx2.Xor(
                    Avx2.Shuffle(lowTable, lowNibbles),
                    Avx2.Shuffle(highTable, highNibbles));
                Vector256<byte> destinationVector = Vector256.LoadUnsafe(ref destinationRef, (nuint)index);

                Avx2.Xor(destinationVector, product).StoreUnsafe(ref destinationRef, (nuint)index);
            }
        }

        Vector128<byte> lowTable128 = Vector128.LoadUnsafe(ref tableRef);
        Vector128<byte> highTable128 = Vector128.LoadUnsafe(ref Unsafe.Add(ref tableRef, 32));
        Vector128<byte> lowMask128 = Vector128.Create((byte)0x0f);

        for (; index <= destination.Length - Vector128<byte>.Count; index += Vector128<byte>.Count)
        {
            Vector128<byte> sourceVector = Vector128.LoadUnsafe(in sourceRef, (nuint)index);
            Vector128<byte> lowNibbles = Sse2.And(sourceVector, lowMask128);
            Vector128<byte> highNibbles = Sse2.And(
                Sse2.ShiftRightLogical(sourceVector.AsUInt16(), 4).AsByte(),
                lowMask128);
            Vector128<byte> product = Sse2.Xor(
                Ssse3.Shuffle(lowTable128, lowNibbles),
                Ssse3.Shuffle(highTable128, highNibbles));
            Vector128<byte> destinationVector = Vector128.LoadUnsafe(ref destinationRef, (nuint)index);

            Sse2.Xor(destinationVector, product).StoreUnsafe(ref destinationRef, (nuint)index);
        }

        for (; index < destination.Length; index++)
        {
            destination[index] ^= Multiply(source[index], coefficient);
        }
    }
#endif

    private static byte[] CreateExpTable()
    {
        byte[] exp = new byte[FieldSize * 2];
        int value = 1;

        for (int index = 0; index < FieldMask; index++)
        {
            exp[index] = (byte)value;
            value <<= 1;

            if ((value & FieldSize) != 0)
            {
                value ^= PrimitivePolynomial;
            }
        }

        for (int index = FieldMask; index < exp.Length; index++)
        {
            exp[index] = exp[index - FieldMask];
        }

        return exp;
    }

    private static byte[] CreateLogTable()
    {
        byte[] log = new byte[FieldSize];

        for (int index = 0; index < FieldMask; index++)
        {
            log[Exp[index]] = (byte)index;
        }

        return log;
    }
}
