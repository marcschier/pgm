// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Congestion;

namespace Pgm.Tests.Congestion;

public sealed class ReceiverCongestionReportTests
{
    [Test]
    public async Task Constructor_ValidInputs_StoresValues()
    {
        var report = new ReceiverCongestionReport(7, 0.25, 1_000_000, TimeSpan.FromMilliseconds(40));

        await Assert.That(report.ReceiverId).IsEqualTo(7UL);
        await Assert.That(report.LossFraction).IsEqualTo(0.25);
        await Assert.That(report.ThroughputBytesPerSecond).IsEqualTo(1_000_000d);
        await Assert.That(report.RoundTripTime).IsEqualTo(TimeSpan.FromMilliseconds(40));
    }

    [Test]
    [Arguments(double.NaN)]
    [Arguments(-0.1)]
    [Arguments(1.1)]
    public async Task Constructor_InvalidLoss_Throws(double lossFraction)
    {
        await Assert.That(() => new ReceiverCongestionReport(1, lossFraction, 1, TimeSpan.Zero))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(double.NaN)]
    [Arguments(-1)]
    public async Task Constructor_InvalidThroughput_Throws(double throughput)
    {
        await Assert.That(() => new ReceiverCongestionReport(1, 0, throughput, TimeSpan.Zero))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Constructor_NegativeRoundTrip_Throws()
    {
        await Assert.That(() => new ReceiverCongestionReport(1, 0, 1, TimeSpan.FromMilliseconds(-1)))
            .Throws<ArgumentOutOfRangeException>();
    }
}
