// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Tests.Packets;

public sealed class PgmPacketCodecTests
{
    [Test]
    public async Task PgmPacket_SourcePathMessage_RoundTrips()
    {
        var packet = PgmPacket.CreateSourcePathMessage(
            Header(PgmPacketType.SourcePathMessage, 0),
            new PgmSourcePathMessage(1, 2, 3, Ipv4(192, 0, 2, 1)),
            Array.Empty<byte>());
        var parsed = RoundTrip(packet);
        var body = (PgmSourcePathMessage)parsed.Body;

        await Assert.That(parsed.Header.Type).IsEqualTo(PgmPacketType.SourcePathMessage);
        await Assert.That(body.Path.GetAddressBytes()).IsEquivalentTo(new byte[] { 192, 0, 2, 1 });
        await Assert.That(body.LeadingEdgeSequenceNumber).IsEqualTo(3U);
    }

    [Test]
    public async Task PgmPacket_DataWithOptions_RoundTrips()
    {
        var options = new byte[28];
        _ = PgmOptionCodec.TryWriteLength(options, (ushort)options.Length, false);
        _ = PgmOptionCodec.TryWriteFragment(options.AsSpan(4), 10, 20, 30, false);
        _ = PgmOptionCodec.TryWriteParityGroup(options.AsSpan(20), 2, true);
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var header = Header(
            PgmPacketType.OriginalData,
            (ushort)data.Length,
            PgmHeaderOptions.OptionsPresent | PgmHeaderOptions.Parity);
        var packet = PgmPacket.CreateData(header, new PgmDataPacket(11, 7, data), options);
        var parsed = RoundTrip(packet);
        var body = (PgmDataPacket)parsed.Body;

        await Assert.That(parsed.Header.Type).IsEqualTo(PgmPacketType.OriginalData);
        await Assert.That(parsed.GetOptionBytes()).IsEquivalentTo(options);
        await Assert.That(body.GetDataBytes()).IsEquivalentTo(data);
    }

    [Test]
    public async Task PgmPacket_NakFamilies_RoundTrip()
    {
        var types = new[]
        {
            PgmPacketType.NegativeAcknowledgment,
            PgmPacketType.NullNegativeAcknowledgment,
            PgmPacketType.NakConfirmation,
        };

        foreach (var type in types)
        {
            var packet = PgmPacket.CreateNakLike(Header(type, 0), new PgmNakPacket(
                99,
                Ipv4(203, 0, 113, 1),
                Ipv4(239, 192, 0, 1)), Array.Empty<byte>());
            var parsed = RoundTrip(packet);
            var body = (PgmNakPacket)parsed.Body;

            await Assert.That(parsed.Header.Type).IsEqualTo(type);
            await Assert.That(body.SequenceNumber).IsEqualTo(99U);
            await Assert.That(body.Group.GetAddressBytes()).IsEquivalentTo(new byte[] { 239, 192, 0, 1 });
        }
    }

    [Test]
    public async Task PgmPacket_PollFamilies_RoundTrip()
    {
        var poll = PgmPacket.CreatePoll(Header(PgmPacketType.Poll, 0), new PgmPollPacket(
            123,
            2,
            1,
            Ipv4(198, 51, 100, 8),
            250_000,
            0xAABBCCDD,
            0x0000FFFF), Array.Empty<byte>());
        var polr = PgmPacket.CreatePollResponse(
            Header(PgmPacketType.PollResponse, 0),
            new PgmPollResponsePacket(123, 2),
            Array.Empty<byte>());

        await Assert.That(((PgmPollPacket)RoundTrip(poll).Body).BackoffInterval).IsEqualTo(250_000U);
        await Assert.That(((PgmPollResponsePacket)RoundTrip(polr).Body).Round).IsEqualTo((ushort)2);
    }

    [Test]
    public async Task PgmPacket_SourcePathMessageRequest_RoundTrips()
    {
        var packet = PgmPacket.CreateSourcePathMessageRequest(
            Header(PgmPacketType.SourcePathMessageRequest, 0),
            Array.Empty<byte>());
        var parsed = RoundTrip(packet);

        await Assert.That(parsed.Header.Type).IsEqualTo(PgmPacketType.SourcePathMessageRequest);
        await Assert.That(parsed.Body).IsTypeOf<PgmSourcePathMessageRequest>();
    }

    [Test]
    public async Task PgmPacket_Options_ParseKnownExtensions()
    {
        var nakList = new byte[12];
        _ = PgmOptionCodec.TryWriteNakList(nakList, new uint[] { 4, 5 }, true);
        var fec = new byte[8];
        _ = PgmOptionCodec.TryWriteFec(fec, 64, true, true, true);

        var nakHeader = PgmOptionHeader.TryParse(nakList, out var optionHeader) ? optionHeader : null;
        _ = PgmOptionCodec.TryReadFec(fec, out var groupSize, out var proactive, out var onDemand);

        await Assert.That(nakHeader?.Type).IsEqualTo(PgmOptionType.NakList);
        await Assert.That(groupSize).IsEqualTo(64U);
        await Assert.That(proactive).IsTrue();
        await Assert.That(onDemand).IsTrue();
    }

    [Test]
    public async Task PgmPacket_UdpEncapsulation_ParsesPayload()
    {
        var packet = PgmPacket.CreateData(
            Header(PgmPacketType.RepairData, 3),
            new PgmDataPacket(8, 1, new byte[] { 7, 8, 9 }),
            Array.Empty<byte>());
        var buffer = new byte[packet.EncodedLength];

        _ = PgmUdpEncapsulation.TryWritePayload(packet, buffer);
        var parsed = PgmUdpEncapsulation.TryParsePayload(buffer, out var udpPacket);

        await Assert.That(parsed).IsTrue();
        await Assert.That(udpPacket?.Header.Type).IsEqualTo(PgmPacketType.RepairData);
        var defaultPort = PgmUdpEncapsulation.DefaultPort;
        await Assert.That(defaultPort).IsEqualTo(3055);
    }

    [Test]
    public async Task PgmPacket_TryParse_RejectsTruncatedPackets()
    {
        var packet = PgmPacket.CreateNakLike(Header(PgmPacketType.NegativeAcknowledgment, 0), new PgmNakPacket(
            99,
            Ipv4(203, 0, 113, 1),
            Ipv4(239, 192, 0, 1)), Array.Empty<byte>());
        var buffer = new byte[packet.EncodedLength];
        _ = packet.TryWrite(buffer);

        for (var length = 0; length < buffer.Length; length++)
        {
            await Assert.That(PgmPacket.TryParse(buffer.AsSpan(0, length), out _)).IsFalse();
        }
    }

    [Test]
    public async Task PgmPacket_Checksum_ComputesExpectedValue()
    {
        var bytes = new byte[] { 0x00, 0x01, 0xF2, 0x03, 0xF4, 0xF5 };

        await Assert.That(PgmChecksum.Compute(bytes)).IsEqualTo((ushort)0x1905);
    }

    private static PgmPacket RoundTrip(PgmPacket packet)
    {
        var buffer = new byte[packet.EncodedLength];
        _ = packet.TryWrite(buffer);
        _ = PgmPacket.TryParse(buffer, out var parsed);
        return parsed ?? throw new InvalidOperationException("Packet did not parse.");
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
