// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Tests.Packets;

public sealed class PgmPrimitivesTests
{
    [Test]
    public async Task PgmGlobalSourceId_HighBitsSet_Throws()
    {
        await Assert.That(() => new PgmGlobalSourceId(1UL << 48)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task PgmGlobalSourceId_EqualityMembers_CompareValues()
    {
        var a = new PgmGlobalSourceId(0x010203040506);
        var b = new PgmGlobalSourceId(0x010203040506);
        var c = new PgmGlobalSourceId(0x0000000000FF);

        await Assert.That(a == b).IsTrue();
        await Assert.That(a != c).IsTrue();
        await Assert.That(a == c).IsFalse();
        await Assert.That(a.Equals(b)).IsTrue();
        await Assert.That(a.Equals((object)b)).IsTrue();
        await Assert.That(a.Equals("not-an-id")).IsFalse();
        await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
    }

    [Test]
    public async Task PgmGlobalSourceId_TryWrite_SmallDestination_ReturnsFalse()
    {
        var id = new PgmGlobalSourceId(0x010203040506);

        await Assert.That(id.TryWrite(new byte[PgmGlobalSourceId.EncodedLength - 1])).IsFalse();
    }

    [Test]
    public async Task PgmGlobalSourceId_TryParse_ShortSource_ReturnsFalse()
    {
        await Assert.That(PgmGlobalSourceId.TryParse(new byte[PgmGlobalSourceId.EncodedLength - 1], out _))
            .IsFalse();
    }

    [Test]
    public async Task PgmNetworkAddress_WrongAddressLength_Throws()
    {
        await Assert.That(() => new PgmNetworkAddress(PgmAddressFamily.IPv4, new byte[] { 1, 2, 3 }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PgmNetworkAddress_GetAddressLength_ReturnsFamilySize()
    {
        await Assert.That(PgmNetworkAddress.GetAddressLength(PgmAddressFamily.IPv4)).IsEqualTo(4);
        await Assert.That(PgmNetworkAddress.GetAddressLength(PgmAddressFamily.IPv6)).IsEqualTo(16);
        await Assert.That(PgmNetworkAddress.GetAddressLength((PgmAddressFamily)999)).IsEqualTo(0);
    }

    [Test]
    public async Task PgmNetworkAddress_EqualityMembers_CompareValues()
    {
        var a = new PgmNetworkAddress(PgmAddressFamily.IPv4, new byte[] { 192, 0, 2, 1 });
        var b = new PgmNetworkAddress(PgmAddressFamily.IPv4, new byte[] { 192, 0, 2, 1 });
        var c = new PgmNetworkAddress(PgmAddressFamily.IPv4, new byte[] { 192, 0, 2, 2 });

        await Assert.That(a == b).IsTrue();
        await Assert.That(a != c).IsTrue();
        await Assert.That(a.Equals(b)).IsTrue();
        await Assert.That(a.Equals((object)b)).IsTrue();
        await Assert.That(a.Equals(42)).IsFalse();
        await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
    }

    [Test]
    public async Task PgmNetworkAddress_TryCopyAddress_SmallDestination_ReturnsFalse()
    {
        var address = new PgmNetworkAddress(PgmAddressFamily.IPv4, new byte[] { 192, 0, 2, 1 });

        await Assert.That(address.TryCopyAddress(new byte[3])).IsFalse();
    }

    [Test]
    public async Task PgmNetworkAddress_TryWrite_SmallDestination_ReturnsFalse()
    {
        var address = new PgmNetworkAddress(PgmAddressFamily.IPv4, new byte[] { 192, 0, 2, 1 });

        await Assert.That(address.TryWrite(new byte[address.EncodedLength - 1])).IsFalse();
    }

    [Test]
    public async Task PgmNetworkAddress_IPv6_RoundTrips()
    {
        var bytes = new byte[]
        {
            0x20, 0x01, 0x0D, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1,
        };
        var address = new PgmNetworkAddress(PgmAddressFamily.IPv6, bytes);
        var buffer = new byte[address.EncodedLength];

        var written = address.TryWrite(buffer);
        var parsed = PgmNetworkAddress.TryParse(buffer, out var roundTripped, out var read);

        await Assert.That(written).IsTrue();
        await Assert.That(parsed).IsTrue();
        await Assert.That(read).IsEqualTo(address.EncodedLength);
        await Assert.That(roundTripped == address).IsTrue();
        await Assert.That(roundTripped.GetAddressBytes()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task PgmHeader_TryWrite_SmallDestination_ReturnsFalse()
    {
        var header = new PgmHeader(
            7500,
            7501,
            PgmPacketType.OriginalData,
            PgmHeaderOptions.None,
            0,
            new PgmGlobalSourceId(0x010203040506),
            0);

        await Assert.That(header.TryWrite(new byte[PgmHeader.EncodedLength - 1])).IsFalse();
    }

    [Test]
    public async Task PgmHeader_TryParse_UnknownPacketType_ReturnsFalse()
    {
        var buffer = new byte[PgmHeader.EncodedLength];
        buffer[4] = 0x03;

        await Assert.That(PgmHeader.TryParse(buffer, out _)).IsFalse();
    }
}
