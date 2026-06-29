// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CA1510, CA1512

namespace Pgm.Congestion;

/// <summary>Elects the PGMCC ACKer from receiver congestion reports.</summary>
/// <remarks>
/// RFC 3208 section 13 has the source select the receiver that represents the bottleneck path for congestion
/// feedback. This implementation ranks reports by loss first, then by lower throughput, then by larger RTT. It
/// does not model PGM POLL round timing or random receiver suppression; integrations supply reports explicitly.
/// </remarks>
public sealed class AckerElection
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _reportLifetime;
    private readonly Dictionary<ulong, TimedReport> _reports = new Dictionary<ulong, TimedReport>();

    /// <summary>Initializes a new instance of the <see cref="AckerElection"/> class.</summary>
    /// <param name="timeProvider">The time provider used to age reports.</param>
    /// <param name="reportLifetime">The maximum report age that can participate in election.</param>
    public AckerElection(TimeProvider timeProvider, TimeSpan reportLifetime)
    {
        if (timeProvider is null)
        {
            throw new ArgumentNullException(nameof(timeProvider));
        }
        ThrowIfInvalidLifetime(reportLifetime);

        _timeProvider = timeProvider;
        _reportLifetime = reportLifetime;
    }

    /// <summary>
    /// Gets the currently elected ACKer receiver identifier, or <see langword="null"/> when none exists.
    /// </summary>
    public ulong? CurrentAckerId { get; private set; }

    /// <summary>Records or replaces congestion feedback from one receiver.</summary>
    /// <param name="report">The receiver report.</param>
    public void Report(ReceiverCongestionReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        _reports[report.ReceiverId] = new TimedReport(report, _timeProvider.GetUtcNow());
        ElectAcker();
    }

    /// <summary>Attempts to get the current elected ACKer report.</summary>
    /// <param name="report">The elected ACKer report.</param>
    /// <returns><see langword="true"/> when an ACKer is available.</returns>
    public bool TryGetAcker(out ReceiverCongestionReport? report)
    {
        ElectAcker();
        if (CurrentAckerId.HasValue && _reports.TryGetValue(CurrentAckerId.Value, out TimedReport timed))
        {
            report = timed.Report;
            return true;
        }

        report = null;
        return false;
    }

    /// <summary>Removes a receiver from future elections.</summary>
    /// <param name="receiverId">The receiver identifier.</param>
    /// <returns><see langword="true"/> when a report was removed.</returns>
    public bool RemoveReceiver(ulong receiverId)
    {
        bool removed = _reports.Remove(receiverId);
        if (removed && CurrentAckerId == receiverId)
        {
            CurrentAckerId = null;
            ElectAcker();
        }

        return removed;
    }

    private static void ThrowIfInvalidLifetime(TimeSpan reportLifetime)
    {
        if (reportLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reportLifetime));
        }
    }

    private void ElectAcker()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        RemoveExpiredReports(now);

        ReceiverCongestionReport? best = null;
        foreach (TimedReport timed in _reports.Values)
        {
            if (best is null || IsWorse(timed.Report, best))
            {
                best = timed.Report;
            }
        }

        CurrentAckerId = best?.ReceiverId;
    }

    private void RemoveExpiredReports(DateTimeOffset now)
    {
        List<ulong>? expired = null;
        foreach (KeyValuePair<ulong, TimedReport> pair in _reports)
        {
            if (now - pair.Value.Timestamp > _reportLifetime)
            {
                expired ??= new List<ulong>();
                expired.Add(pair.Key);
            }
        }

        if (expired is null)
        {
            return;
        }

        for (int index = 0; index < expired.Count; index++)
        {
            _reports.Remove(expired[index]);
        }
    }

    private static bool IsWorse(ReceiverCongestionReport candidate, ReceiverCongestionReport current)
    {
        int lossComparison = candidate.LossFraction.CompareTo(current.LossFraction);
        if (lossComparison != 0)
        {
            return lossComparison > 0;
        }

        int throughputComparison = candidate.ThroughputBytesPerSecond.CompareTo(current.ThroughputBytesPerSecond);
        if (throughputComparison != 0)
        {
            return throughputComparison < 0;
        }

        int rttComparison = candidate.RoundTripTime.CompareTo(current.RoundTripTime);
        if (rttComparison != 0)
        {
            return rttComparison > 0;
        }

        return candidate.ReceiverId < current.ReceiverId;
    }

    private readonly struct TimedReport
    {
        public TimedReport(ReceiverCongestionReport report, DateTimeOffset timestamp)
        {
            Report = report;
            Timestamp = timestamp;
        }

        public ReceiverCongestionReport Report { get; }

        public DateTimeOffset Timestamp { get; }
    }
}
