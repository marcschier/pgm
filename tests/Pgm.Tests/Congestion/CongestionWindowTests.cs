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
}