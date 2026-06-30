// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using Pgm.Packets;

namespace Pgm.Tests;

public sealed class PgmAddressConversionTests
{
    [Test]
    public async Task ToPgmAddress_Ipv4_Maps_Family_And_Bytes()
    {
        var address = IPAddress.Parse("239.1.2.3");

        PgmNetworkAddress result = PgmAddressConversion.ToPgmAddress(address);

        await Assert.That(result.AddressFamily).IsEqualTo(PgmAddressFamily.IPv4);
        await Assert.That(result.GetAddressBytes()).IsEquivalentTo(address.GetAddressBytes());
    }

    [Test]
    public async Task ToPgmAddress_Ipv6_Maps_Family_And_Bytes()
    {
        var address = IPAddress.Parse("ff05::1:2:3");

        PgmNetworkAddress result = PgmAddressConversion.ToPgmAddress(address);

        await Assert.That(result.AddressFamily).IsEqualTo(PgmAddressFamily.IPv6);
        await Assert.That(result.GetAddressBytes()).IsEquivalentTo(address.GetAddressBytes());
    }

    [Test]
    public async Task ToPgmAddress_Null_Throws_ArgumentNull()
    {
        await Assert.That(() => PgmAddressConversion.ToPgmAddress(null!))
            .Throws<ArgumentNullException>();
    }
}
