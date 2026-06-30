// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;
using Pgm.Receiver;
using Pgm.Tests.Congestion;

namespace Pgm.Tests.Receiver;

public sealed class ReceiveWindowTests
{
    private static readonly PgmGlobalSourceId SourceId = new(0x010203040506);
    private static readonly PgmNetworkAddress Source = new(PgmAddressFamily.IPv4, new byte[] { 192, 0, 2, 10 });
    private static readonly PgmNetworkAddress Group = new(PgmAddressFamily.IPv4, new byte[] { 239, 192, 0, 1 });

    [Test]
    public async Task DataBeforeSpm_EstablishesSequence_And_DeliversInOrder()
    {
        string[] expected = ["one", "two", "three"];

        await Assert.That(RunDataBeforeSpm()).IsEquivalentTo(expected);
    }

    [Test]
    public async Task SourcePathMessage_AdvancesExpectedSequencePastTrailingEdge()
    {
        string[] expected = ["one", "five"];

        await Assert.That(RunSpmAdvance()).IsEquivalentTo(expected);
    }

    [Test]
    public async Task ReceiveWindowSizeExceeded_EvictsBufferedPackets()
    {
        await Assert.That(RunWindowEviction()).IsEqualTo(0);
    }

    [Test]
    public async Task TrailingEdgeAdvance_RemovesObsoleteNaks()
    {
        await Assert.That(RunNakRemovalBelowTrailingEdge()).IsEqualTo(0);
    }

    [Test]
    public async Task ExhaustedNaks_AbandonLostData_And_DeliverFollowingPackets()
    {
        var (firstNakCount, delivered) = RunNakExhaustion();
        string[] expected = ["two"];

        await Assert.That(firstNakCount).IsEqualTo(1);
        await Assert.That(delivered).IsEquivalentTo(expected);
    }

    [Test]
    public async Task IncompleteFecGroup_DoesNotAttemptRepair()
    {
        string[] expected = ["abc"];

        await Assert.That(RunIncompleteFecGroup()).IsEquivalentTo(expected);
    }

    private static List<string> RunDataBeforeSpm()
    {
        var window = CreateWindow(Options(), new ManualTimeProvider());
        ProcessData(window, 1, 1, Bytes("one"));
        ProcessData(window, 2, 1, Bytes("two"));
        ProcessData(window, 3, 1, Bytes("three"));
        return Drain(window);
    }

    private static List<string> RunSpmAdvance()
    {
        var window = CreateWindow(Options(), new ManualTimeProvider());
        ProcessData(window, 1, 1, Bytes("one"));
        ProcessSpm(window, 5, 5);
        ProcessData(window, 5, 5, Bytes("five"));
        return Drain(window);
    }

    private static int RunWindowEviction()
    {
        var options = Options();
        options.ReceiveWindowSize = 2;
        var window = CreateWindow(options, new ManualTimeProvider());
        ProcessData(window, 5, 1, Bytes("e5"));
        ProcessData(window, 6, 1, Bytes("e6"));
        ProcessData(window, 7, 1, Bytes("e7"));
        ProcessData(window, 8, 1, Bytes("e8"));
        return Drain(window).Count;
    }

    private static int RunNakRemovalBelowTrailingEdge()
    {
        var window = CreateWindow(Options(), new ManualTimeProvider());
        ProcessData(window, 5, 1, Bytes("e5"));
        ProcessSpm(window, 10, 10);
        var naks = new List<byte[]>();
        window.CollectDueNaks(1000, naks);
        return naks.Count;
    }

    private static (int FirstNakCount, List<string> Delivered) RunNakExhaustion()
    {
        var options = Options();
        options.MaximumNakAttempts = 1;
        options.NakConfirmationTimeout = TimeSpan.Zero;
        var window = CreateWindow(options, new ManualTimeProvider());
        ProcessData(window, 2, 1, Bytes("two"));

        var first = new List<byte[]>();
        window.CollectDueNaks(0, first);
        var second = new List<byte[]>();
        window.CollectDueNaks(1, second);

        return (first.Count, Drain(window));
    }

    private static List<string> RunIncompleteFecGroup()
    {
        var window = CreateWindow(Options(), new ManualTimeProvider());
        var options = FecOptions(parityGroupNumber: 1, transmissionGroupSize: 3);
        var header = Header(PgmPacketType.OriginalData, 3, PgmHeaderOptions.OptionsPresent);
        window.ProcessPacket(PgmPacket.CreateData(header, new PgmDataPacket(1, 1, Bytes("abc")), options));
        return Drain(window);
    }

    private static ReceiveWindow CreateWindow(PgmReceiverOptions options, ManualTimeProvider timeProvider)
    {
        var header = Header(PgmPacketType.OriginalData, 0);
        return new ReceiveWindow(SourceKey.FromHeader(header), null!, options, timeProvider);
    }

    private static void ProcessData(ReceiveWindow window, uint sequence, uint trailingEdge, byte[] data)
    {
        var header = Header(PgmPacketType.OriginalData, (ushort)data.Length);
        _ = window.ProcessPacket(PgmPacket.CreateData(
            header,
            new PgmDataPacket(sequence, trailingEdge, data),
            ReadOnlySpan<byte>.Empty));
    }

    private static void ProcessSpm(ReceiveWindow window, uint trailingEdge, uint leadingEdge)
    {
        var header = Header(PgmPacketType.SourcePathMessage, 0);
        _ = window.ProcessPacket(PgmPacket.CreateSourcePathMessage(
            header,
            new PgmSourcePathMessage(1, trailingEdge, leadingEdge, Source),
            ReadOnlySpan<byte>.Empty));
    }

    private static List<string> Drain(ReceiveWindow window)
    {
        var result = new List<string>();
        while (window.TryDequeueCompletedApdu(out var apdu))
        {
            result.Add(Text(apdu!));
        }

        return result;
    }

    private static PgmReceiverOptions Options()
    {
        return new PgmReceiverOptions
        {
            SourcePort = 7501,
            DestinationPort = 7500,
            GroupAddress = Group,
            DefaultSourceAddress = Source,
            InitialNakBackoff = TimeSpan.FromMilliseconds(10),
            MaximumNakBackoff = TimeSpan.FromMilliseconds(20),
            NakConfirmationTimeout = TimeSpan.FromMilliseconds(10),
            MaximumNakAttempts = 5,
        };
    }

    private static byte[] FecOptions(uint parityGroupNumber, uint transmissionGroupSize)
    {
        var options = new byte[20];
        _ = PgmOptionCodec.TryWriteLength(options, (ushort)options.Length, false);
        _ = PgmOptionCodec.TryWriteFec(options.AsSpan(4), transmissionGroupSize, true, true, false);
        _ = PgmOptionCodec.TryWriteParityGroup(options.AsSpan(12), parityGroupNumber, true);
        return options;
    }

    private static PgmHeader Header(
        PgmPacketType type,
        ushort tsduLength,
        PgmHeaderOptions options = PgmHeaderOptions.None)
    {
        return new PgmHeader(7500, 7501, type, options, 0, SourceId, tsduLength);
    }

    private static byte[] Bytes(string value)
    {
        var bytes = new byte[value.Length];
        for (int index = 0; index < value.Length; index++)
        {
            bytes[index] = (byte)value[index];
        }

        return bytes;
    }

    private static string Text(byte[] value)
    {
        var chars = new char[value.Length];
        for (int index = 0; index < value.Length; index++)
        {
            chars[index] = (char)value[index];
        }

        return new string(chars);
    }
}
