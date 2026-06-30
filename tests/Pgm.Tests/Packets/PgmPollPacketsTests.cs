// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Tests.Packets;

public sealed class PgmPollPacketsTests
{
    private static readonly PgmNetworkAddress Path =
        new(PgmAddressFamily.IPv4, new byte[] { 198, 51, 100, 8 });

    [Test]
    public async Task PgmPollPacket_TryWriteBody_SmallDestination_ReturnsFalse()
    {
        var body = new PgmPollPacket(123, 2, 1, Path, 250_000, 0xAABBCCDD, 0x0000FFFF);

        await Assert.That(body.TryWriteBody(new byte[body.BodyLength - 1])).IsFalse();
    }

    [Test]
    public async Task PgmPollPacket_TryParseBody_ShortSource_ReturnsFalse()
    {
        await Assert.That(PgmPollPacket.TryParseBody(new byte[20], out _, out var read)).IsFalse();
        await Assert.That(read).IsEqualTo(0);
    }

    [Test]
    public async Task PgmPollPacket_TryParseBody_TruncatedAfterPath_ReturnsFalse()
    {
        // 24 bytes is long enough for the initial length check and a valid IPv4 NLA at offset 8,
        // but leaves fewer than 12 bytes for the back-off, random string, and mask fields.
        var source = new byte[24];
        source[9] = (byte)PgmAddressFamily.IPv4;

        await Assert.That(PgmPollPacket.TryParseBody(source, out _, out var read)).IsFalse();
        await Assert.That(read).IsEqualTo(0);
    }

    [Test]
    public async Task PgmPollResponsePacket_TryWriteBody_SmallDestination_ReturnsFalse()
    {
        var body = new PgmPollResponsePacket(123, 2);

        await Assert.That(body.TryWriteBody(new byte[body.BodyLength - 1])).IsFalse();
    }

    [Test]
    public async Task PgmPollResponsePacket_TryParseBody_ShortSource_ReturnsFalse()
    {
        await Assert.That(PgmPollResponsePacket.TryParseBody(new byte[7], out _, out var read)).IsFalse();
        await Assert.That(read).IsEqualTo(0);
    }
}
