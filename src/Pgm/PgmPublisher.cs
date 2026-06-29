// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Net;
using Pgm.Sender;

namespace Pgm;

/// <summary>Publishes APDUs to a reliable PGM multicast session.</summary>
public sealed class PgmPublisher : IAsyncDisposable
{
    private readonly PgmSender _sender;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private bool _started;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="PgmPublisher" /> class over UDP multicast.</summary>
    /// <param name="options">The publisher options.</param>
    public PgmPublisher(PgmPublisherOptions options)
        : this(CreateUdpChannel(options), options)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PgmPublisher" /> class.</summary>
    /// <param name="channel">The datagram channel connected to the multicast group.</param>
    /// <param name="options">The publisher options, or <see langword="null" /> to use defaults.</param>
    public PgmPublisher(IPgmDatagramChannel channel, PgmPublisherOptions? options = null)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(channel);
#else
        if (channel is null)
        {
            throw new ArgumentNullException(nameof(channel));
        }
#endif

        PgmPublisherOptions publisherOptions = (options ?? new PgmPublisherOptions()).Clone();
        _sender = new PgmSender(channel, CreateSenderOptions(publisherOptions));
    }

    /// <summary>Starts the publisher source state machine.</summary>
    /// <param name="cancellationToken">A token that can cancel startup.</param>
    /// <returns>A value task that completes when the publisher has started.</returns>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_started)
        {
            return;
        }

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                await _sender.StartAsync(cancellationToken).ConfigureAwait(false);
                _started = true;
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <summary>Publishes one APDU to the multicast session.</summary>
    /// <param name="payload">The APDU bytes to publish.</param>
    /// <param name="cancellationToken">A token that can cancel publication.</param>
    /// <returns>A value task that completes when the APDU has been accepted for sending.</returns>
    public async ValueTask PublishAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        await _sender.SendAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _startGate.Dispose();
        await _sender.DisposeAsync().ConfigureAwait(false);
    }

    private static UdpMulticastChannel CreateUdpChannel(PgmPublisherOptions options)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(options);
#else
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }
#endif

        return new UdpMulticastChannel(
            options.MulticastGroup,
            options.Port,
            options.InterfaceAddress,
            options.InterfaceIndex);
    }

    private static PgmSenderOptions CreateSenderOptions(PgmPublisherOptions options)
    {
        return new PgmSenderOptions
        {
            SourceAddress = PgmAddressConversion.ToPgmAddress(options.InterfaceAddress ?? PgmNetworkDefaults.Loopback),
            GroupAddress = PgmAddressConversion.ToPgmAddress(options.MulticastGroup),
            MaximumDataPayloadLength = options.MaximumDataPayloadLength,
            TransmitWindowPacketCount = options.TransmitWindowPacketCount,
            SourcePathMessageInterval = options.SourcePathMessageInterval,
            FecTransmissionGroupSize = options.FecTransmissionGroupSize,
            ProactiveParityPacketCount = options.ProactiveParityPacketCount,
            OnDemandParityPacketCount = options.OnDemandParityPacketCount,
        };
    }

    private void ThrowIfDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PgmPublisher));
        }
#endif
    }
}
