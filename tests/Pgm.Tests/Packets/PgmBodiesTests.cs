// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using Pgm.Packets;

namespace Pgm.Tests.Packets;

public sealed class PgmBodiesTests
{
    private static readonly PgmNetworkAddress Address =
        new(PgmAddressFamily.IPv4, new byte[] { 192, 0, 2, 1 });

    [Test]
    public async Task PgmSourcePathMessage_TryWriteBody_SmallDestination_ReturnsFalse()
    {
        var body = new PgmSourcePathMessage(1, 2, 3, Address);

        await Assert.That(body.TryWriteBody(new byte[body.BodyLength - 1])).IsFalse();
    }

    [Test]
    public async Task PgmSourcePathMessage_TryParseBody_ShortSource_ReturnsFalse()
    {
        await Assert.That(PgmSourcePathMessage.TryParseBody(new byte[10], out _, out var read)).IsFalse();
        await Assert.That(read).IsEqualTo(0);
    }

    [Test]
    public async Task PgmDataPacket_DataLongerThanMax_Throws()
    {
        await Assert.That(() =>
        {
            _ = new PgmDataPacket(1, 1, new byte[ushort.MaxValue + 1]);
        }).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task PgmDataPacket_BodyLength_And_CopyData()
    {
        var data = new byte[] { 1, 2, 3 };
        var copy = new byte[3];
        int bodyLength;
        ushort tsduLength;
        {
            var body = new PgmDataPacket(11, 7, data);
            bodyLength = body.BodyLength;
            tsduLength = body.TsduLength;
            body.CopyDataTo(copy);
        }

        await Assert.That(bodyLength).IsEqualTo(11);
        await Assert.That(tsduLength).IsEqualTo((ushort)3);
        await Assert.That(copy).IsEquivalentTo(data);
    }

    [Test]
    public async Task PgmDataPacket_TryWriteBodyPrefix_WritesPrefix_And_RejectsSmall()
    {
        var body = new PgmDataPacket(11, 7, new byte[] { 1, 2, 3 });
        var destination = new byte[8];

        var written = body.TryWriteBodyPrefix(destination);
        var rejected = body.TryWriteBodyPrefix(new byte[7]);

        await Assert.That(written).IsTrue();
        await Assert.That(rejected).IsFalse();
        await Assert.That(BinaryPrimitives.ReadUInt32BigEndian(destination)).IsEqualTo(11U);
        await Assert.That(BinaryPrimitives.ReadUInt32BigEndian(destination.AsSpan(4))).IsEqualTo(7U);
    }

    [Test]
    public async Task PgmNakPacket_TryWriteBody_SmallDestination_ReturnsFalse()
    {
        var body = new PgmNakPacket(99, Address, Address);

        await Assert.That(body.TryWriteBody(new byte[body.BodyLength - 1])).IsFalse();
    }

    [Test]
    public async Task PgmSourcePathMessageRequest_HasEmptyBody()
    {
        var body = default(PgmSourcePathMessageRequest);

        await Assert.That(body.BodyLength).IsEqualTo(0);
        await Assert.That(body.TryWriteBody(Array.Empty<byte>())).IsTrue();
    }
}
