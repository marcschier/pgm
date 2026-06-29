// Copyright (c) marcschier. Licensed under the MIT License.

using System.Threading.Channels;

namespace Pgm.Net;

/// <summary>Provides an in-memory datagram channel backed by an <see cref="InMemoryMulticastBus" />.</summary>
public sealed class InMemoryDatagramChannel : IPgmDatagramChannel
{
    private readonly object _gate = new();
    private readonly InMemoryMulticastBus _bus;
    private readonly Channel<byte[]> _received;
    private byte[]? _postponed;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="InMemoryDatagramChannel" /> class.</summary>
    /// <param name="bus">The bus to join.</param>
    public InMemoryDatagramChannel(InMemoryMulticastBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _received = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _bus.Register(this);
    }

    /// <inheritdoc />
    public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        _bus.Publish(datagram);
        return default;
    }

    /// <inheritdoc />
    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        byte[] datagram = await _received.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        int byteCount = Math.Min(buffer.Length, datagram.Length);
        datagram.AsMemory(0, byteCount).CopyTo(buffer);
        return byteCount;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _bus.Unregister(this);

            lock (_gate)
            {
                FlushPostponed();
                _received.Writer.TryComplete();
            }
        }

        return default;
    }

    internal void Enqueue(ReadOnlyMemory<byte> datagram, bool reorder)
    {
        byte[] copy = datagram.ToArray();

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (reorder && _postponed is null)
            {
                _postponed = copy;
                return;
            }

            _received.Writer.TryWrite(copy);
            FlushPostponed();
        }
    }

    private void FlushPostponed()
    {
        if (_postponed is not null)
        {
            _received.Writer.TryWrite(_postponed);
            _postponed = null;
        }
    }

    private void EnsureNotDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InMemoryDatagramChannel));
        }
#endif
    }
}
