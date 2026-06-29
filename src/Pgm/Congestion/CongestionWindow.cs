// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CA1510, CA1512

namespace Pgm.Congestion;

/// <summary>Maintains a TCP-friendly congestion window for a PGM source.</summary>
/// <remarks>
/// The window follows the PGMCC source behavior at algorithm level: ACKs from the elected receiver increase the
/// window additively and loss reports reduce it multiplicatively. It deliberately does not implement the full
/// equation-based TCP throughput model from RFC 3208 section 13; integrations can translate POLR feedback into
/// ACK and loss events while preserving deterministic unit tests.
/// </remarks>
public sealed class CongestionWindow
{
    private readonly TimeProvider _timeProvider;
    private readonly double _minimumPackets;
    private readonly double _maximumPackets;
    private readonly double _lossBackoffFactor;
    private double _ackAccumulator;

    /// <summary>Initializes a new instance of the <see cref="CongestionWindow"/> class.</summary>
    /// <param name="timeProvider">The time provider used to timestamp updates.</param>
    /// <param name="initialPackets">The initial window size, in packets.</param>
    /// <param name="minimumPackets">The minimum window size, in packets.</param>
    /// <param name="maximumPackets">The maximum window size, in packets.</param>
    /// <param name="lossBackoffFactor">The multiplicative factor applied after loss.</param>
    public CongestionWindow(
        TimeProvider timeProvider,
        double initialPackets = 2,
        double minimumPackets = 1,
        double maximumPackets = 1024,
        double lossBackoffFactor = 0.5)
    {
        if (timeProvider is null)
        {
            throw new ArgumentNullException(nameof(timeProvider));
        }
        ThrowIfInvalidLimits(initialPackets, minimumPackets, maximumPackets, lossBackoffFactor);

        _timeProvider = timeProvider;
        _minimumPackets = minimumPackets;
        _maximumPackets = maximumPackets;
        _lossBackoffFactor = lossBackoffFactor;
        WindowPackets = initialPackets;
        LastUpdated = _timeProvider.GetUtcNow();
    }

    /// <summary>Gets the current congestion window size, in packets.</summary>
    public double WindowPackets { get; private set; }

    /// <summary>Gets the time of the last window update.</summary>
    public DateTimeOffset LastUpdated { get; private set; }

    /// <summary>Gets the number of loss events applied to this window.</summary>
    public long LossEvents { get; private set; }

    /// <summary>Applies one or more ACKs from the elected ACKer.</summary>
    /// <param name="acknowledgedPackets">The number of packets acknowledged.</param>
    public void OnAcknowledged(int acknowledgedPackets = 1)
    {
        ThrowIfInvalidAcknowledgementCount(acknowledgedPackets);

        for (int index = 0; index < acknowledgedPackets; index++)
        {
            _ackAccumulator += 1 / WindowPackets;
            while (_ackAccumulator >= 1)
            {
                WindowPackets = Math.Min(_maximumPackets, WindowPackets + 1);
                _ackAccumulator -= 1;
            }
        }

        LastUpdated = _timeProvider.GetUtcNow();
    }

    /// <summary>Applies receiver loss feedback to the congestion window.</summary>
    /// <param name="lossFraction">The receiver loss fraction, from 0.0 through 1.0.</param>
    public void OnLoss(double lossFraction)
    {
        ReceiverCongestionReportValidation.ThrowIfInvalidLossFraction(lossFraction);

        if (lossFraction == 0)
        {
            LastUpdated = _timeProvider.GetUtcNow();
            return;
        }

        double backoff = Math.Max(_minimumPackets, WindowPackets * _lossBackoffFactor);
        WindowPackets = Math.Min(_maximumPackets, backoff);
        _ackAccumulator = 0;
        LossEvents++;
        LastUpdated = _timeProvider.GetUtcNow();
    }

    /// <summary>Applies one receiver congestion report as either ACK or loss feedback.</summary>
    /// <param name="report">The receiver congestion report.</param>
    public void ApplyReport(ReceiverCongestionReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (report.LossFraction > 0)
        {
            OnLoss(report.LossFraction);
        }
        else
        {
            OnAcknowledged();
        }
    }

    private static void ThrowIfInvalidLimits(
        double initialPackets,
        double minimumPackets,
        double maximumPackets,
        double lossBackoffFactor)
    {
        if (double.IsNaN(minimumPackets) || minimumPackets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumPackets));
        }

        if (double.IsNaN(maximumPackets) || maximumPackets < minimumPackets)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPackets));
        }

        if (double.IsNaN(initialPackets) || initialPackets < minimumPackets || initialPackets > maximumPackets)
        {
            throw new ArgumentOutOfRangeException(nameof(initialPackets));
        }

        if (double.IsNaN(lossBackoffFactor) || lossBackoffFactor <= 0 || lossBackoffFactor >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lossBackoffFactor));
        }
    }

    private static void ThrowIfInvalidAcknowledgementCount(int acknowledgedPackets)
    {
        if (acknowledgedPackets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(acknowledgedPackets));
        }
    }
}
