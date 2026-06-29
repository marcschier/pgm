// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Congestion;

namespace Pgm.Tests.Congestion;

public sealed class CongestionRateLimiterTests
{
    [Test]
    public async Task RateLimiter_TryConsume_UsesBurstCapacityThenRefills()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        RateLimiter limiter = new RateLimiter(timeProvider, tokensPerSecond: 2, capacity: 2);

        bool first = limiter.TryConsume();
        bool second = limiter.TryConsume();
        bool third = limiter.TryConsume();
        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        bool afterRefill = limiter.TryConsume();

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsTrue();
        await Assert.That(third).IsFalse();
        await Assert.That(afterRefill).IsTrue();
    }

    [Test]
    public async Task RateLimiter_GetDelay_ReturnsTimeUntilTokensAreAvailable()
    {
        RateLimiter limiter = new RateLimiter(new ManualTimeProvider(), tokensPerSecond: 4, capacity: 1);

        _ = limiter.TryConsume();
        TimeSpan delay = limiter.GetDelay();

        await Assert.That(delay).IsEqualTo(TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public async Task RateLimiter_Reserve_DebitsFutureTokens()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        RateLimiter limiter = new RateLimiter(timeProvider, tokensPerSecond: 2, capacity: 1);

        TimeSpan immediate = limiter.Reserve();
        TimeSpan reserved = limiter.Reserve();
        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        bool canSendAtReservedTime = limiter.TryConsume();
        timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        bool canSendAfterReservation = limiter.TryConsume();

        await Assert.That(immediate).IsEqualTo(TimeSpan.Zero);
        await Assert.That(reserved).IsEqualTo(TimeSpan.FromMilliseconds(500));
        await Assert.That(canSendAtReservedTime).IsFalse();
        await Assert.That(canSendAfterReservation).IsTrue();
    }
}
