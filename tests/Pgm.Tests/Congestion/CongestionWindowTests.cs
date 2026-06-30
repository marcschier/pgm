// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Congestion;

namespace Pgm.Tests.Congestion;

public sealed class CongestionWindowTests
{
    [Test]
    public async Task CongestionWindow_AcknowledgedPackets_GrowWindowAdditively()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        CongestionWindow window = new CongestionWindow(timeProvider, initialPackets: 2, maximumPackets: 10);

        window.OnAcknowledged(2);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        window.OnAcknowledged(3);

        await Assert.That(window.WindowPackets).IsEqualTo(4);
        await Assert.That(window.LastUpdated).IsEqualTo(timeProvider.GetUtcNow());
    }

    [Test]
    public async Task CongestionWindow_LossReport_BacksOffAndResetsAckGrowth()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        CongestionWindow window = new CongestionWindow(timeProvider, initialPackets: 8, minimumPackets: 2);

        window.OnAcknowledged(4);
        window.OnLoss(0.25);
        window.OnAcknowledged(1);

        await Assert.That(window.WindowPackets).IsEqualTo(4);
        await Assert.That(window.LossEvents).IsEqualTo(1L);
    }

    [Test]
    public async Task CongestionWindow_ReportWithoutLoss_CountsAsAckerAck()
    {
        CongestionWindow window = new CongestionWindow(new ManualTimeProvider(), initialPackets: 1, maximumPackets: 4);
        ReceiverCongestionReport report = new ReceiverCongestionReport(1, 0, 1024, TimeSpan.FromMilliseconds(20));

        window.ApplyReport(report);

        await Assert.That(window.WindowPackets).IsEqualTo(2);
    }

    [Test]
    public async Task CongestionWindow_ReportWithLoss_BacksOff()
    {
        CongestionWindow window = new CongestionWindow(
            new ManualTimeProvider(),
            initialPackets: 8,
            minimumPackets: 2);
        ReceiverCongestionReport report = new ReceiverCongestionReport(1, 0.5, 1024, TimeSpan.FromMilliseconds(20));

        window.ApplyReport(report);

        await Assert.That(window.WindowPackets).IsEqualTo(4);
        await Assert.That(window.LossEvents).IsEqualTo(1L);
    }

    [Test]
    public async Task CongestionWindow_LossWithZeroFraction_UpdatesTimestampOnly()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        CongestionWindow window = new CongestionWindow(timeProvider, initialPackets: 4);
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        window.OnLoss(0);

        await Assert.That(window.WindowPackets).IsEqualTo(4);
        await Assert.That(window.LossEvents).IsEqualTo(0L);
        await Assert.That(window.LastUpdated).IsEqualTo(timeProvider.GetUtcNow());
    }

    [Test]
    public async Task CongestionWindow_NullTimeProvider_Throws()
    {
        await Assert.That(() => new CongestionWindow(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task CongestionWindow_NullReport_Throws()
    {
        CongestionWindow window = new CongestionWindow(new ManualTimeProvider());

        await Assert.That(() => window.ApplyReport(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task CongestionWindow_NonPositiveAcknowledgement_Throws(int acknowledgedPackets)
    {
        CongestionWindow window = new CongestionWindow(new ManualTimeProvider());

        await Assert.That(() => window.OnAcknowledged(acknowledgedPackets))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CongestionWindow_InvalidMinimumPackets_Throws()
    {
        await Assert.That(() => new CongestionWindow(new ManualTimeProvider(), minimumPackets: 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CongestionWindow_MaximumBelowMinimum_Throws()
    {
        await Assert.That(() => new CongestionWindow(new ManualTimeProvider(), minimumPackets: 2, maximumPackets: 1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CongestionWindow_InitialOutOfRange_Throws()
    {
        await Assert.That(() => new CongestionWindow(new ManualTimeProvider(), initialPackets: 100, maximumPackets: 10))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CongestionWindow_InvalidLossBackoffFactor_Throws()
    {
        await Assert.That(() => new CongestionWindow(new ManualTimeProvider(), lossBackoffFactor: 1.5))
            .Throws<ArgumentOutOfRangeException>();
    }
}
