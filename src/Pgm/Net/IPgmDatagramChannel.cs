// Copyright (c) marcschier. Licensed under the MIT License.

namespace Pgm.Net;

/// <summary>Represents a datagram channel connected to a PGM multicast group.</summary>
public interface IPgmDatagramChannel : IAsyncDisposable
{
    /// <summary>Sends one datagram to the multicast group.</summary>
    /// <param name="datagram">The datagram payload to send.</param>
    /// <param name="cancellationToken">A token that can cancel the send operation.</param>
    /// <returns>A task that completes when the datagram has been accepted for sending.</returns>
    ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken = default);

    /// <summary>Receives one datagram from the multicast group.</summary>
    /// <param name="buffer">The buffer that receives the datagram payload.</param>
    /// <param name="cancellationToken">A token that can cancel the receive operation.</param>
    /// <returns>The number of bytes copied into <paramref name="buffer" />.</returns>
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
}
