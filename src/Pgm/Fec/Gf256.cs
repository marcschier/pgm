// Copyright (c) marcschier. Licensed under the MIT License.

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
