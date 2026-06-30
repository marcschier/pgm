// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;
using Pgm.Receiver;

namespace Pgm.Tests.Receiver;

public sealed class SourceKeyTests
{
    [Test]
    public async Task FromHeader_SameSource_AreEqual()
    {
        var first = SourceKey.FromHeader(Header(0x010203040506, 7501));
        var second = SourceKey.FromHeader(Header(0x010203040506, 7501));

        await Assert.That(first.Equals(second)).IsTrue();
        await Assert.That(first.Equals((object)second)).IsTrue();
        await Assert.That(first.Equals("not-a-key")).IsFalse();
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    [Test]
    public async Task FromHeader_DifferentGlobalSourceId_AreNotEqual()
    {
        var first = SourceKey.FromHeader(Header(0x010203040506, 7501));
        var second = SourceKey.FromHeader(Header(0x0000000000FF, 7501));

        await Assert.That(first.Equals(second)).IsFalse();
    }

    [Test]
    public async Task FromHeader_DifferentSourcePort_AreNotEqual()
    {
        var first = SourceKey.FromHeader(Header(0x010203040506, 7501));
        var second = SourceKey.FromHeader(Header(0x010203040506, 7502));

        await Assert.That(first.Equals(second)).IsFalse();
    }

    private static PgmHeader Header(ulong globalSourceId, ushort sourcePort)
    {
        return new PgmHeader(
            sourcePort,
            7500,
            PgmPacketType.OriginalData,
            PgmHeaderOptions.None,
            0,
            new PgmGlobalSourceId(globalSourceId),
            0);
    }
}
