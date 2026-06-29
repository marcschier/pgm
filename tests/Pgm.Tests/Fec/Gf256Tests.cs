// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Fec;

namespace Pgm.Tests.Fec;

public sealed class Gf256Tests
{
    [Test]
    public async Task Add_AllValues_IsExclusiveOr()
    {
        for (int left = 0; left <= byte.MaxValue; left++)
        {
            for (int right = 0; right <= byte.MaxValue; right++)
            {
                await Assert.That(Gf256.Add((byte)left, (byte)right)).IsEqualTo((byte)(left ^ right));
                await Assert.That(Gf256.Subtract((byte)left, (byte)right)).IsEqualTo((byte)(left ^ right));
            }
        }
    }

    [Test]
    public async Task Multiply_WithIdentity_ReturnsInput()
    {
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            await Assert.That(Gf256.Multiply((byte)value, 1)).IsEqualTo((byte)value);
            await Assert.That(Gf256.Multiply(1, (byte)value)).IsEqualTo((byte)value);
        }
    }

    [Test]
    public async Task Divide_BySameNonZeroValue_ReturnsOne()
    {
        for (int value = 1; value <= byte.MaxValue; value++)
        {
            await Assert.That(Gf256.Divide((byte)value, (byte)value)).IsEqualTo((byte)1);
        }
    }

    [Test]
    public async Task Inverse_ForNonZeroValues_MultipliesToOne()
    {
        for (int value = 1; value <= byte.MaxValue; value++)
        {
            byte inverse = Gf256.Inverse((byte)value);

            await Assert.That(Gf256.Multiply((byte)value, inverse)).IsEqualTo((byte)1);
        }
    }

    [Test]
    public async Task MultiplyAdd_AcrossLengthsAndCoefficients_MatchesScalar()
    {
        var random = new Random(24680);
        int[] lengths =
        [
            0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33, 47, 48, 49, 63, 64, 65, 79, 95, 111, 127,
            128, 129, 191, 255, 256, 257, 511, 512, 513, 1024,
        ];

        for (int coefficient = 0; coefficient <= byte.MaxValue; coefficient++)
        {
            foreach (int length in lengths)
            {
                byte[] source = new byte[length];
                byte[] expected = new byte[length];
                byte[] actual = new byte[length];

                random.NextBytes(source);
                random.NextBytes(expected);
                expected.AsSpan().CopyTo(actual);

                Gf256.MultiplyAddScalar(source, expected, (byte)coefficient);
                Gf256.MultiplyAdd(source, actual, (byte)coefficient);

                await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
            }
        }
    }
}
