// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Tests.Packets;

public sealed class PgmOptionCodecTests
{
    [Test]
    public async Task PgmOptionHeader_TryWrite_SmallDestination_ReturnsFalse()
    {
        var header = new PgmOptionHeader(PgmOptionType.Length, 4, 0, 0, true);

        await Assert.That(header.TryWrite(new byte[PgmOptionHeader.EncodedLength - 1])).IsFalse();
    }

    [Test]
    public async Task PgmOptionHeader_TryParse_ShortSource_ReturnsFalse()
    {
        await Assert.That(PgmOptionHeader.TryParse(new byte[3], out var header)).IsFalse();
        await Assert.That(header).IsNull();
    }

    [Test]
    public async Task PgmOptionHeader_TryParse_LengthBelowMinimum_ReturnsFalse()
    {
        await Assert.That(PgmOptionHeader.TryParse(new byte[] { 0x00, 0x02, 0, 0 }, out _)).IsFalse();
    }

    [Test]
    public async Task PgmOptionHeader_TryParse_LengthExceedsSource_ReturnsFalse()
    {
        await Assert.That(PgmOptionHeader.TryParse(new byte[] { 0x00, 0x10, 0, 0 }, out _)).IsFalse();
    }

    [Test]
    public async Task TryWriteLength_SmallDestination_ReturnsFalse()
    {
        await Assert.That(PgmOptionCodec.TryWriteLength(new byte[3], 4, true)).IsFalse();
    }

    [Test]
    public async Task TryWriteFragment_SmallDestination_ReturnsFalse()
    {
        await Assert.That(PgmOptionCodec.TryWriteFragment(new byte[3], 1, 2, 3, true)).IsFalse();
    }

    [Test]
    public async Task TryWriteNakList_TooManySequences_ReturnsFalse()
    {
        await Assert.That(PgmOptionCodec.TryWriteNakList(new byte[256], new uint[63], true)).IsFalse();
    }

    [Test]
    public async Task TryWriteNakList_SmallDestination_ReturnsFalse()
    {
        await Assert.That(PgmOptionCodec.TryWriteNakList(new byte[5], new uint[] { 4, 5 }, true)).IsFalse();
    }

    [Test]
    public async Task TryWriteParityParameters_SmallDestination_ReturnsFalse()
    {
        await Assert.That(PgmOptionCodec.TryWriteParityParameters(new byte[3], 64, true, true, true)).IsFalse();
    }

    [Test]
    public async Task TryWriteParityGroup_SmallDestination_ReturnsFalse()
    {
        await Assert.That(PgmOptionCodec.TryWriteParityGroup(new byte[3], 2, true)).IsFalse();
    }

    [Test]
    public async Task TryReadLength_WrongType_ReturnsFalse()
    {
        var fragment = new byte[16];
        _ = PgmOptionCodec.TryWriteFragment(fragment, 1, 2, 3, true);

        await Assert.That(PgmOptionCodec.TryReadLength(fragment, out var total)).IsFalse();
        await Assert.That(total).IsEqualTo((ushort)0);
    }

    [Test]
    public async Task TryReadFragment_WrongType_ReturnsFalse()
    {
        var length = new byte[4];
        _ = PgmOptionCodec.TryWriteLength(length, 4, true);

        await Assert.That(PgmOptionCodec.TryReadFragment(length, out _, out _, out _)).IsFalse();
    }

    [Test]
    public async Task TryReadParityParameters_WrongType_ReturnsFalse()
    {
        var parityGroup = new byte[8];
        _ = PgmOptionCodec.TryWriteParityGroup(parityGroup, 1, true);

        await Assert.That(PgmOptionCodec.TryReadParityParameters(parityGroup, out _, out _, out _)).IsFalse();
    }

    [Test]
    public async Task TryReadParityGroup_WrongType_ReturnsFalse()
    {
        var fec = new byte[8];
        _ = PgmOptionCodec.TryWriteFec(fec, 64, true, true, true);

        await Assert.That(PgmOptionCodec.TryReadParityGroup(fec, out _)).IsFalse();
    }

    [Test]
    public async Task GetHeaderOptions_NakList_SetsNetworkSignificant()
    {
        var nakList = new byte[12];
        _ = PgmOptionCodec.TryWriteNakList(nakList, new uint[] { 4, 5 }, true);

        var options = PgmOptionCodec.GetHeaderOptions(nakList);

        await Assert.That(options.HasFlag(PgmHeaderOptions.OptionsPresent)).IsTrue();
        await Assert.That(options.HasFlag(PgmHeaderOptions.NetworkSignificant)).IsTrue();
    }

    [Test]
    public async Task GetHeaderOptions_Empty_ReturnsNone()
    {
        await Assert.That(PgmOptionCodec.GetHeaderOptions(ReadOnlySpan<byte>.Empty))
            .IsEqualTo(PgmHeaderOptions.None);
    }
}
