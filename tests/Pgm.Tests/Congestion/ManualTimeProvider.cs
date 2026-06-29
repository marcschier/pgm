// Copyright (c) marcschier. Licensed under the MIT License.

namespace Pgm.Tests.Congestion;

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }

    public override long GetTimestamp()
    {
        return _timestamp;
    }

    public void Advance(TimeSpan duration)
    {
        _utcNow += duration;
        _timestamp += duration.Ticks;
    }
}