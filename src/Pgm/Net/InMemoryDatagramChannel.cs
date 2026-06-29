// Copyright (c) marcschier. Licensed under the MIT License.

using System.Threading.Channels;

namespace Pgm.Net;

/// <summary>Provides an in-memory datagram channel backed by an <see cref="InMemoryMulticastBus" />.</summary>
public sealed class InMemoryDatagramChannel : IPgmDatagramChannel
{
    private readonly object gate = new();
    private readonly InMemoryMulticastBus bus;
    private readonly Channel<byte[]> received;
    private byte[]? postponed;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="InMemoryDatagramChannel" /> class.</summary>
    /// <param name="bus">The bus to join.</param>
    public InMemoryDatagramChannel(InMemoryMulticastBus bus)
    {
        this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
        received = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        this.bus.Register(this);
    }

    /// <inheritdoc />
    public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        bus.Publish(datagram);
        return default;
    }

    /// <inheritdoc />
    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        byte[] datagram = await received.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        int byteCount = Math.Min(buffer.Length, datagram.Length);
        datagram.AsMemory(0, byteCount).CopyTo(buffer);
        return byteCount;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            bus.Unregister(this);

            lock (gate)
            {
                FlushPostponed();
                received.Writer.TryComplete();
            }
        }

        return default;
    }

    internal void Enqueue(ReadOnlyMemory<byte> datagram, bool reorder)
    {
        byte[] copy = datagram.ToArray();

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (reorder && postponed is null)
            {
                postponed = copy;
                return;
            }

            received.Writer.TryWrite(copy);
            FlushPostponed();
        }
    }

    private void FlushPostponed()
    {
        if (postponed is not null)
        {
            received.Writer.TryWrite(postponed);
            postponed = null;
        }
    }

    private void EnsureNotDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(disposed, this);
#else
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(InMemoryDatagramChannel));
        }
#endif
    }
}
