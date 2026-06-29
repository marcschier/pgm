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
}
