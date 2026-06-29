// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Congestion;

namespace Pgm.Tests.Congestion;

public sealed class CongestionAckerElectionTests
{
    [Test]
    public async Task AckerElection_Reports_ElectWorstReceiverByLossThenThroughput()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        AckerElection election = new AckerElection(timeProvider, TimeSpan.FromSeconds(10));

        election.Report(new ReceiverCongestionReport(1, 0, 10_000, TimeSpan.FromMilliseconds(30)));
        election.Report(new ReceiverCongestionReport(2, 0.05, 20_000, TimeSpan.FromMilliseconds(20)));
        election.Report(new ReceiverCongestionReport(3, 0.05, 5_000, TimeSpan.FromMilliseconds(10)));

        bool found = election.TryGetAcker(out ReceiverCongestionReport? report);

        await Assert.That(found).IsTrue();
        await Assert.That(report!.ReceiverId).IsEqualTo(3UL);
        await Assert.That(election.CurrentAckerId).IsEqualTo(3UL);
    }

    [Test]
    public async Task AckerElection_ExpiredReports_AreIgnored()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        AckerElection election = new AckerElection(timeProvider, TimeSpan.FromSeconds(1));

        election.Report(new ReceiverCongestionReport(1, 0.25, 1_000, TimeSpan.FromMilliseconds(100)));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        election.Report(new ReceiverCongestionReport(2, 0, 2_000, TimeSpan.FromMilliseconds(10)));

        bool found = election.TryGetAcker(out ReceiverCongestionReport? report);

        await Assert.That(found).IsTrue();
        await Assert.That(report!.ReceiverId).IsEqualTo(2UL);
    }

    [Test]
    public async Task AckerElection_RemoveCurrentAcker_ReelectsNextWorstReceiver()
    {
        AckerElection election = new AckerElection(new ManualTimeProvider(), TimeSpan.FromSeconds(10));

        election.Report(new ReceiverCongestionReport(1, 0.10, 1_000, TimeSpan.FromMilliseconds(30)));
        election.Report(new ReceiverCongestionReport(2, 0.20, 1_000, TimeSpan.FromMilliseconds(30)));

        bool removed = election.RemoveReceiver(2);
        bool found = election.TryGetAcker(out ReceiverCongestionReport? report);

        await Assert.That(removed).IsTrue();
        await Assert.That(found).IsTrue();
        await Assert.That(report!.ReceiverId).IsEqualTo(1UL);
    }
}
