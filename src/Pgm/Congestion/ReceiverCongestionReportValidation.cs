// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CA1510, CA1512

namespace Pgm.Congestion;

internal static class ReceiverCongestionReportValidation
{
    public static void ThrowIfInvalidLossFraction(double lossFraction)
    {
        if (double.IsNaN(lossFraction) || lossFraction < 0 || lossFraction > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lossFraction));
        }
    }

    public static void ThrowIfInvalidThroughput(double throughputBytesPerSecond)
    {
        if (double.IsNaN(throughputBytesPerSecond) || throughputBytesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(throughputBytesPerSecond));
        }
    }

    public static void ThrowIfInvalidRoundTripTime(TimeSpan roundTripTime)
    {
        if (roundTripTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(roundTripTime));
        }
    }
}
