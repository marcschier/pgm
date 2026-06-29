// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CA1510, CA1512

namespace Pgm.Congestion;

/// <summary>Represents a receiver's PGMCC congestion feedback to the source.</summary>
/// <remarks>
/// This model captures the PGMCC inputs needed by the pure congestion algorithms: receiver identity, loss,
/// measured throughput, and RTT. It intentionally omits packet encoding and PGM POLL/POLR wire details so the
/// sender and receiver integrations can map their packet state to this testable value object later.
/// </remarks>
public sealed class ReceiverCongestionReport
{
    /// <summary>Initializes a new instance of the <see cref="ReceiverCongestionReport"/> class.</summary>
    /// <param name="receiverId">The stable receiver identifier.</param>
    /// <param name="lossFraction">The observed loss fraction, from 0.0 through 1.0.</param>
    /// <param name="throughputBytesPerSecond">The receiver's measured goodput, in bytes per second.</param>
    /// <param name="roundTripTime">The receiver's measured round-trip time.</param>
    public ReceiverCongestionReport(
        ulong receiverId,
        double lossFraction,
        double throughputBytesPerSecond,
        TimeSpan roundTripTime)
    {
        ReceiverCongestionReportValidation.ThrowIfInvalidLossFraction(lossFraction);
        ReceiverCongestionReportValidation.ThrowIfInvalidThroughput(throughputBytesPerSecond);
        ReceiverCongestionReportValidation.ThrowIfInvalidRoundTripTime(roundTripTime);

        ReceiverId = receiverId;
        LossFraction = lossFraction;
        ThroughputBytesPerSecond = throughputBytesPerSecond;
        RoundTripTime = roundTripTime;
    }

    /// <summary>Gets the stable receiver identifier.</summary>
    public ulong ReceiverId { get; }

    /// <summary>Gets the observed loss fraction, from 0.0 through 1.0.</summary>
    public double LossFraction { get; }

    /// <summary>Gets the receiver's measured goodput, in bytes per second.</summary>
    public double ThroughputBytesPerSecond { get; }

    /// <summary>Gets the receiver's measured round-trip time.</summary>
    public TimeSpan RoundTripTime { get; }
}
