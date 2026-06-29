// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CA1510, CA1512

namespace Pgm.Congestion;

/// <summary>Limits sends with a token-bucket rate shaper.</summary>
/// <remarks>
/// PGMCC computes an allowed source rate from the selected ACKer's feedback. This class implements the reusable
/// token bucket that enforces that computed rate; it does not decide the rate from wire protocol fields.
/// </remarks>
public sealed class RateLimiter
{
    private readonly TimeProvider _timeProvider;
    private readonly double _tokensPerSecond;
    private readonly double _capacity;
    private double _tokens;
    private long _lastTimestamp;

    /// <summary>Initializes a new instance of the <see cref="RateLimiter"/> class.</summary>
    /// <param name="timeProvider">The time provider used to refill tokens.</param>
    /// <param name="tokensPerSecond">The token refill rate.</param>
    /// <param name="capacity">The maximum token capacity.</param>
    public RateLimiter(TimeProvider timeProvider, double tokensPerSecond, double capacity)
    {
        if (timeProvider is null)
        {
            throw new ArgumentNullException(nameof(timeProvider));
        }
        ThrowIfInvalidRate(tokensPerSecond, capacity);

        _timeProvider = timeProvider;
        _tokensPerSecond = tokensPerSecond;
        _capacity = capacity;
        _tokens = capacity;
        _lastTimestamp = _timeProvider.GetTimestamp();
    }

    /// <summary>Gets the current token count after refilling from the time provider.</summary>
    public double AvailableTokens
    {
        get
        {
            Refill();
            return _tokens;
        }
    }

    /// <summary>Attempts to consume tokens immediately.</summary>
    /// <param name="tokens">The number of tokens to consume.</param>
    /// <returns><see langword="true"/> when the tokens were consumed.</returns>
    public bool TryConsume(double tokens = 1)
    {
        ValidateTokens(tokens);
        Refill();
        if (_tokens < tokens)
        {
            return false;
        }

        _tokens -= tokens;
        return true;
    }

    /// <summary>Gets the delay until the requested tokens can be consumed.</summary>
    /// <param name="tokens">The number of tokens required.</param>
    /// <returns>The required delay, or <see cref="TimeSpan.Zero"/> when tokens are available.</returns>
    public TimeSpan GetDelay(double tokens = 1)
    {
        ValidateTokens(tokens);
        Refill();
        if (_tokens >= tokens)
        {
            return TimeSpan.Zero;
        }

        double missing = tokens - _tokens;
        double seconds = missing / _tokensPerSecond;
        return TimeSpan.FromTicks((long)Math.Ceiling(seconds * TimeSpan.TicksPerSecond));
    }

    /// <summary>Reserves tokens and returns the time the caller should wait before sending.</summary>
    /// <param name="tokens">The number of tokens to reserve.</param>
    /// <returns>The required delay, or <see cref="TimeSpan.Zero"/> when tokens were available immediately.</returns>
    public TimeSpan Reserve(double tokens = 1)
    {
        ValidateTokens(tokens);
        Refill();
        if (_tokens >= tokens)
        {
            _tokens -= tokens;
            return TimeSpan.Zero;
        }

        double missing = tokens - _tokens;
        double seconds = missing / _tokensPerSecond;
        _tokens = 0;
        _lastTimestamp += (long)Math.Ceiling(seconds * _timeProvider.TimestampFrequency);
        return TimeSpan.FromTicks((long)Math.Ceiling(seconds * TimeSpan.TicksPerSecond));
    }

    private static void ThrowIfInvalidRate(double tokensPerSecond, double capacity)
    {
        if (double.IsNaN(tokensPerSecond) || tokensPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tokensPerSecond));
        }

        if (double.IsNaN(capacity) || capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
    }

    private static void ValidateTokens(double tokens)
    {
        if (double.IsNaN(tokens) || tokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tokens));
        }
    }

    private void Refill()
    {
        long now = _timeProvider.GetTimestamp();
        if (now <= _lastTimestamp)
        {
            return;
        }

        double elapsedSeconds = (double)(now - _lastTimestamp) / _timeProvider.TimestampFrequency;
        _tokens = Math.Min(_capacity, _tokens + (elapsedSeconds * _tokensPerSecond));
        _lastTimestamp = now;
    }
}
