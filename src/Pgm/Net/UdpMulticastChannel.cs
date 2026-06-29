// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
#if !NET8_0_OR_GREATER
using System.Buffers;
using System.Runtime.InteropServices;
#endif

namespace Pgm.Net;

/// <summary>Provides a UDP multicast datagram channel.</summary>
public sealed class UdpMulticastChannel : IPgmDatagramChannel
{
    private readonly Socket _socket;
    private readonly EndPoint _remoteEndPoint;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="UdpMulticastChannel" /> class.</summary>
    /// <param name="multicastAddress">The multicast group address to join.</param>
    /// <param name="port">The UDP port to bind and send to.</param>
    public UdpMulticastChannel(IPAddress multicastAddress, int port)
        : this(multicastAddress, port, null, 0)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UdpMulticastChannel" /> class.</summary>
    /// <param name="multicastAddress">The multicast group address to join.</param>
    /// <param name="port">The UDP port to bind and send to.</param>
    /// <param name="localAddress">The local interface address to bind to, or <see langword="null" /> for any.</param>
    /// <param name="interfaceIndex">The IPv6 interface index, or zero for the default interface.</param>
    public UdpMulticastChannel(
        IPAddress multicastAddress,
        int port,
        IPAddress? localAddress,
        long interfaceIndex = 0)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(multicastAddress);
#else
        if (multicastAddress is null)
        {
            throw new ArgumentNullException(nameof(multicastAddress));
        }
#endif

        if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        _socket = CreateSocket(multicastAddress);
        _remoteEndPoint = new IPEndPoint(multicastAddress, port);

        try
        {
            BindAndJoin(multicastAddress, port, localAddress, interfaceIndex);
        }
        catch
        {
            _socket.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();

#if NET8_0_OR_GREATER
        return SendCoreAsync(datagram, cancellationToken);
#else
        return SendLegacyAsync(datagram, cancellationToken);
#endif
    }

    /// <inheritdoc />
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();

#if NET8_0_OR_GREATER
        return ReceiveCoreAsync(buffer, cancellationToken);
#else
        return ReceiveLegacyAsync(buffer, cancellationToken);
#endif
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _socket.Dispose();
        }

        return default;
    }

    private static Socket CreateSocket(IPAddress multicastAddress)
    {
        if (multicastAddress.AddressFamily != AddressFamily.InterNetwork
            && multicastAddress.AddressFamily != AddressFamily.InterNetworkV6)
        {
            throw new ArgumentException("The multicast address must be IPv4 or IPv6.", nameof(multicastAddress));
        }

        if (!IsMulticast(multicastAddress))
        {
            throw new ArgumentException("The address must be a multicast address.", nameof(multicastAddress));
        }

        Socket socket = new(multicastAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        if (multicastAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
        }

        return socket;
    }

    private static bool IsMulticast(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] >= 224 && bytes[0] <= 239;
        }

        return address.IsIPv6Multicast;
    }

    private void EnsureNotDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(UdpMulticastChannel));
        }
#endif
    }

    private void BindAndJoin(IPAddress multicastAddress, int port, IPAddress? localAddress, long interfaceIndex)
    {
        if (multicastAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            IPAddress bindAddress = localAddress ?? IPAddress.Any;
            _socket.Bind(new IPEndPoint(bindAddress, port));
            _socket.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.AddMembership,
                new MulticastOption(multicastAddress, bindAddress));
            return;
        }

        _socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        _socket.SetSocketOption(
            SocketOptionLevel.IPv6,
            SocketOptionName.AddMembership,
            new IPv6MulticastOption(multicastAddress, interfaceIndex));
    }

#if NET8_0_OR_GREATER
    private async ValueTask SendCoreAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken)
    {
        await _socket.SendToAsync(datagram, SocketFlags.None, _remoteEndPoint, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<int> ReceiveCoreAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        EndPoint sender = _socket.AddressFamily == AddressFamily.InterNetwork
            ? new IPEndPoint(IPAddress.Any, 0)
            : new IPEndPoint(IPAddress.IPv6Any, 0);
        SocketReceiveFromResult result = await _socket.ReceiveFromAsync(
            buffer,
            SocketFlags.None,
            sender,
            cancellationToken).ConfigureAwait(false);
        return result.ReceivedBytes;
    }
#else
    private async ValueTask SendLegacyAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(datagram.Length);

        try
        {
            datagram.CopyTo(rented);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((Socket)state!).Dispose(),
                _socket);
            await _socket.SendToAsync(
                new ArraySegment<byte>(rented, 0, datagram.Length),
                SocketFlags.None,
                _remoteEndPoint).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private async ValueTask<int> ReceiveLegacyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        byte[]? rented = null;
        ArraySegment<byte> segment;

        if (!MemoryMarshal.TryGetArray(buffer, out segment))
        {
            rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
            segment = new ArraySegment<byte>(rented, 0, buffer.Length);
        }

        try
        {
            EndPoint sender = _socket.AddressFamily == AddressFamily.InterNetwork
                ? new IPEndPoint(IPAddress.Any, 0)
                : new IPEndPoint(IPAddress.IPv6Any, 0);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((Socket)state!).Dispose(),
                _socket);
            SocketReceiveFromResult result = await _socket.ReceiveFromAsync(
                segment,
                SocketFlags.None,
                sender).ConfigureAwait(false);

            if (rented is not null)
            {
                rented.AsMemory(0, result.ReceivedBytes).CopyTo(buffer);
            }

            return result.ReceivedBytes;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
#endif
}
