// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Tests.Packets;

public sealed class PgmPacketParseTests
{
    [Test]
    public async Task Options_Getter_ReturnsOptionBlock()
    {
        await Assert.That(CreatedDataPacketOptionsLength()).IsEqualTo(4);
    }

    [Test]
    public async Task CreateData_WrongType_Throws()
    {
        await Assert.That(() =>
        {
            _ = PgmPacket.CreateData(
                Header(PgmPacketType.SourcePathMessage, 0),
                new PgmDataPacket(1, 1, ReadOnlySpan<byte>.Empty),
                ReadOnlySpan<byte>.Empty);
        }).Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateData_TsduLengthMismatch_Throws()
    {
        await Assert.That(() =>
        {
            _ = PgmPacket.CreateData(
                Header(PgmPacketType.OriginalData, 5),
                new PgmDataPacket(1, 1, new byte[] { 1, 2, 3 }),
                ReadOnlySpan<byte>.Empty);
        }).Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateNakLike_WrongType_Throws()
    {
        await Assert.That(() =>
        {
            _ = PgmPacket.CreateNakLike(
                Header(PgmPacketType.OriginalData, 0),
                new PgmNakPacket(1, Ipv4(203, 0, 113, 1), Ipv4(239, 192, 0, 1)),
                ReadOnlySpan<byte>.Empty);
        }).Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateSourcePathMessage_WrongType_Throws()
    {
        await Assert.That(() =>
        {
            _ = PgmPacket.CreateSourcePathMessage(
                Header(PgmPacketType.OriginalData, 0),
                new PgmSourcePathMessage(1, 2, 3, Ipv4(192, 0, 2, 1)),
                ReadOnlySpan<byte>.Empty);
        }).Throws<ArgumentException>();
    }

    [Test]
    public async Task TryGet_OnMismatchedKind_ReturnFalse()
    {
        var (data, nak, poll, pollResponse) = SpmPacketGetterResults();

        await Assert.That(data).IsFalse();
        await Assert.That(nak).IsFalse();
        await Assert.That(poll).IsFalse();
        await Assert.That(pollResponse).IsFalse();
        await Assert.That(DataPacketSpmGetterResult()).IsFalse();
    }

    [Test]
    public async Task TryWrite_SmallDestination_ReturnsFalse()
    {
        await Assert.That(TryWriteSpmToSmallBuffer()).IsFalse();
    }

    [Test]
    public async Task TryParse_SpmWithMissingOptions_ReturnsFalse()
    {
        var parsed = PgmPacket.TryParse(EncodeWithMissingOptions(PgmPacketType.SourcePathMessage), out _);

        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_DataWithMissingOptions_ReturnsFalse()
    {
        var parsed = PgmPacket.TryParse(EncodeWithMissingOptions(PgmPacketType.OriginalData), out _);

        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_SpmrWithMissingOptions_ReturnsFalse()
    {
        var parsed = PgmPacket.TryParse(EncodeWithMissingOptions(PgmPacketType.SourcePathMessageRequest), out _);

        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_PollWithMissingOptions_ReturnsFalse()
    {
        var parsed = PgmPacket.TryParse(EncodeWithMissingOptions(PgmPacketType.Poll), out _);

        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_PollResponseWithMissingOptions_ReturnsFalse()
    {
        var parsed = PgmPacket.TryParse(EncodeWithMissingOptions(PgmPacketType.PollResponse), out _);

        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_DataWithTruncatedTsdu_ReturnsFalse()
    {
        var buffer = EncodeDataPacket();

        var parsed = PgmPacket.TryParse(buffer.AsSpan(0, buffer.Length - 1), out _);

        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_InvalidSecondOption_ReturnsFalse()
    {
        var parsed = PgmPacket.TryParse(EncodeSpmrWithInvalidOptions(), out _);

        await Assert.That(parsed).IsFalse();
    }

    private static int CreatedDataPacketOptionsLength()
    {
        var options = new byte[4];
        _ = PgmOptionCodec.TryWriteLength(options, 4, true);
        var packet = PgmPacket.CreateData(
            Header(PgmPacketType.OriginalData, 3, PgmHeaderOptions.OptionsPresent),
            new PgmDataPacket(11, 7, new byte[] { 1, 2, 3 }),
            options);
        return packet.Options.Length;
    }

    private static (bool Data, bool Nak, bool Poll, bool PollResponse) SpmPacketGetterResults()
    {
        var packet = PgmPacket.CreateSourcePathMessage(
            Header(PgmPacketType.SourcePathMessage, 0),
            new PgmSourcePathMessage(1, 2, 3, Ipv4(192, 0, 2, 1)),
            ReadOnlySpan<byte>.Empty);
        return (
            packet.TryGetData(out _),
            packet.TryGetNak(out _),
            packet.TryGetPoll(out _),
            packet.TryGetPollResponse(out _));
    }

    private static bool DataPacketSpmGetterResult()
    {
        var packet = PgmPacket.CreateData(
            Header(PgmPacketType.OriginalData, 0),
            new PgmDataPacket(1, 1, ReadOnlySpan<byte>.Empty),
            ReadOnlySpan<byte>.Empty);
        return packet.TryGetSourcePathMessage(out _);
    }

    private static bool TryWriteSpmToSmallBuffer()
    {
        var packet = PgmPacket.CreateSourcePathMessage(
            Header(PgmPacketType.SourcePathMessage, 0),
            new PgmSourcePathMessage(1, 2, 3, Ipv4(192, 0, 2, 1)),
            ReadOnlySpan<byte>.Empty);
        return packet.TryWrite(new byte[1]);
    }

    private static byte[] EncodeWithMissingOptions(PgmPacketType type)
    {
        var header = Header(type, 0, PgmHeaderOptions.OptionsPresent);
        return type switch
        {
            PgmPacketType.SourcePathMessage => Encode(PgmPacket.CreateSourcePathMessage(
                header,
                new PgmSourcePathMessage(1, 2, 3, Ipv4(192, 0, 2, 1)),
                ReadOnlySpan<byte>.Empty)),
            PgmPacketType.OriginalData => Encode(PgmPacket.CreateData(
                header,
                new PgmDataPacket(1, 1, ReadOnlySpan<byte>.Empty),
                ReadOnlySpan<byte>.Empty)),
            PgmPacketType.SourcePathMessageRequest => Encode(PgmPacket.CreateSourcePathMessageRequest(
                header,
                ReadOnlySpan<byte>.Empty)),
            PgmPacketType.Poll => Encode(PgmPacket.CreatePoll(
                header,
                new PgmPollPacket(123, 2, 1, Ipv4(198, 51, 100, 8), 250_000, 0xAABBCCDD, 0x0000FFFF),
                ReadOnlySpan<byte>.Empty)),
            PgmPacketType.PollResponse => Encode(PgmPacket.CreatePollResponse(
                header,
                new PgmPollResponsePacket(123, 2),
                ReadOnlySpan<byte>.Empty)),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    private static byte[] EncodeDataPacket()
    {
        return Encode(PgmPacket.CreateData(
            Header(PgmPacketType.OriginalData, 3),
            new PgmDataPacket(8, 1, new byte[] { 7, 8, 9 }),
            ReadOnlySpan<byte>.Empty));
    }

    private static byte[] EncodeSpmrWithInvalidOptions()
    {
        var options = new byte[] { 0x00, 0x04, 0x00, 0x08, 0x01, 0x00, 0x00, 0x00 };
        return Encode(PgmPacket.CreateSourcePathMessageRequest(
            Header(PgmPacketType.SourcePathMessageRequest, 0, PgmHeaderOptions.OptionsPresent),
            options));
    }

    private static byte[] Encode(PgmPacket packet)
    {
        var buffer = new byte[packet.EncodedLength];
        _ = packet.TryWrite(buffer);
        return buffer;
    }

    private static PgmHeader Header(
        PgmPacketType type,
        ushort tsduLength,
        PgmHeaderOptions options = PgmHeaderOptions.None)
    {
        return new PgmHeader(7500, 7501, type, options, 0, new PgmGlobalSourceId(0x010203040506), tsduLength);
    }

    private static PgmNetworkAddress Ipv4(byte a, byte b, byte c, byte d)
    {
        return new PgmNetworkAddress(PgmAddressFamily.IPv4, new[] { a, b, c, d });
    }
}
