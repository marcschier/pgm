// Copyright (c) marcschier. Licensed under the MIT License.

namespace Pgm.Net;

/// <summary>Provides a deterministic, process-local multicast datagram bus.</summary>
public sealed class InMemoryMulticastBus
{
    private readonly object gate = new();
    private readonly HashSet<InMemoryDatagramChannel> channels = new();
    private readonly Random random;

    /// <summary>Initializes a new instance of the <see cref="InMemoryMulticastBus" /> class.</summary>
    /// <param name="datagramLossRate">The datagram drop probability per receiver, from zero to one.</param>
    /// <param name="datagramReorderRate">The datagram reorder probability per receiver, from zero to one.</param>
    /// <param name="datagramDuplicateRate">The datagram duplicate probability per receiver, from zero to one.</param>
    /// <param name="seed">The deterministic random seed, or <see langword="null" /> to use a time-based seed.</param>
    public InMemoryMulticastBus(
        double datagramLossRate = 0,
        double datagramReorderRate = 0,
        double datagramDuplicateRate = 0,
        int? seed = null)
    {
        EnsureProbability(datagramLossRate, nameof(datagramLossRate));
        EnsureProbability(datagramReorderRate, nameof(datagramReorderRate));
        EnsureProbability(datagramDuplicateRate, nameof(datagramDuplicateRate));

        DatagramLossRate = datagramLossRate;
        DatagramReorderRate = datagramReorderRate;
        DatagramDuplicateRate = datagramDuplicateRate;
        random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>Gets the probability, from zero to one, that a datagram is dropped per receiver.</summary>
    public double DatagramLossRate { get; }

    /// <summary>Gets the probability, from zero to one, that a datagram is reordered per receiver.</summary>
    public double DatagramReorderRate { get; }

    /// <summary>Gets the probability, from zero to one, that a datagram is duplicated per receiver.</summary>
    public double DatagramDuplicateRate { get; }

    /// <summary>Creates and joins a new in-memory datagram channel.</summary>
    /// <returns>The created channel.</returns>
    public InMemoryDatagramChannel CreateChannel()
    {
        return new InMemoryDatagramChannel(this);
    }

    internal void Register(InMemoryDatagramChannel channel)
    {
        lock (gate)
        {
            channels.Add(channel);
        }
    }

    internal void Unregister(InMemoryDatagramChannel channel)
    {
        lock (gate)
        {
            channels.Remove(channel);
        }
    }

    internal void Publish(ReadOnlyMemory<byte> datagram)
    {
        lock (gate)
        {
            foreach (InMemoryDatagramChannel channel in channels)
            {
                if (ShouldDrop())
                {
                    continue;
                }

                Enqueue(channel, datagram);

                if (ShouldDuplicate())
                {
                    Enqueue(channel, datagram);
                }
            }
        }
    }

    private static void EnsureProbability(double value, string parameterName)
    {
        if (value < 0 || value > 1 || double.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private void Enqueue(InMemoryDatagramChannel channel, ReadOnlyMemory<byte> datagram)
    {
        channel.Enqueue(datagram, ShouldReorder());
    }

    private bool ShouldDrop()
    {
        return DatagramLossRate > 0 && random.NextDouble() < DatagramLossRate;
    }

    private bool ShouldReorder()
    {
        return DatagramReorderRate > 0 && random.NextDouble() < DatagramReorderRate;
    }

    private bool ShouldDuplicate()
    {
        return DatagramDuplicateRate > 0 && random.NextDouble() < DatagramDuplicateRate;
    }
}
