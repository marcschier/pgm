// Copyright (c) marcschier. Licensed under the MIT License.

using System.Threading.Channels;
using Pgm.Net;
using Pgm.Packets;
using Pgm.Receiver;

namespace Pgm;

/// <summary>Subscribes to APDUs from a reliable PGM multicast session.</summary>
public sealed class PgmSubscriber : IAsyncDisposable
{
    private readonly PgmReceiver receiver;
    private readonly Channel<byte[]> messages;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim startGate = new(1, 1);
    private Task? pumpTask;
    private bool started;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="PgmSubscriber" /> class over UDP multicast.</summary>
    /// <param name="options">The subscriber options.</param>
    public PgmSubscriber(PgmSubscriberOptions options)
        : this(CreateUdpChannel(options), options)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PgmSubscriber" /> class.</summary>
    /// <param name="channel">The datagram channel connected to the multicast group.</param>
    /// <param name="options">The subscriber options, or <see langword="null" /> to use defaults.</param>
    public PgmSubscriber(IPgmDatagramChannel channel, PgmSubscriberOptions? options = null)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(channel);
#else
        if (channel is null)
        {
            throw new ArgumentNullException(nameof(channel));
        }
#endif

        PgmSubscriberOptions subscriberOptions = (options ?? new PgmSubscriberOptions()).Clone();
        receiver = new PgmReceiver(
            new StartupSpmFilteringChannel(channel),
            TimeProvider.System,
            CreateReceiverOptions(subscriberOptions));
        messages = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>Gets a reader for received APDUs.</summary>
    public ChannelReader<byte[]> Messages => messages.Reader;

    /// <summary>Starts receiving APDUs from the multicast session.</summary>
    /// <param name="cancellationToken">A token that can cancel startup.</param>
    /// <returns>A value task that completes when receive processing has started.</returns>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (started)
        {
            return;
        }

        await startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!started)
            {
                await receiver.StartAsync(cancellationToken).ConfigureAwait(false);
                pumpTask = Task.Run(PumpAsync, CancellationToken.None);
                started = true;
            }
        }
        finally
        {
            startGate.Release();
        }
    }

    /// <summary>Receives the next APDU from the multicast session.</summary>
    /// <param name="cancellationToken">A token that can cancel the receive operation.</param>
    /// <returns>The next APDU bytes.</returns>
    public async ValueTask<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        return await messages.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        stop.Cancel();
        await AwaitPumpAsync().ConfigureAwait(false);
        await receiver.DisposeAsync().ConfigureAwait(false);
        messages.Writer.TryComplete();
        stop.Dispose();
        startGate.Dispose();
    }

    private static UdpMulticastChannel CreateUdpChannel(PgmSubscriberOptions options)
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

    private static PgmReceiverOptions CreateReceiverOptions(PgmSubscriberOptions options)
    {
        return new PgmReceiverOptions
        {
            GroupAddress = PgmAddressConversion.ToPgmAddress(options.MulticastGroup),
            DefaultSourceAddress = PgmAddressConversion.ToPgmAddress(
                options.InterfaceAddress ?? PgmNetworkDefaults.Any),
            InitialNakBackoff = options.InitialNakBackoff,
            MaximumNakBackoff = options.MaximumNakBackoff,
            NakConfirmationTimeout = options.NakConfirmationTimeout,
            MaximumNakAttempts = options.MaximumNakAttempts,
        };
    }

    private async Task PumpAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                byte[] message = await receiver.ReceiveAsync(stop.Token).ConfigureAwait(false);
                await messages.Writer.WriteAsync(message, stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            messages.Writer.TryComplete();
        }
    }

    private async ValueTask AwaitPumpAsync()
    {
        if (pumpTask is null)
        {
            return;
        }

        try
        {
            await pumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ThrowIfDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(disposed, this);
#else
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(PgmSubscriber));
        }
#endif
    }

    private sealed class StartupSpmFilteringChannel : IPgmDatagramChannel
    {
        private readonly IPgmDatagramChannel inner;

        internal StartupSpmFilteringChannel(IPgmDatagramChannel inner)
        {
            this.inner = inner;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken = default)
        {
            return inner.SendAsync(datagram, cancellationToken);
        }

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                int length = await inner.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (!IsEmptyStartupSpm(buffer.Span.Slice(0, length)))
                {
                    return length;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            return inner.DisposeAsync();
        }

        private static bool IsEmptyStartupSpm(ReadOnlySpan<byte> datagram)
        {
            return PgmUdpEncapsulation.TryParsePayload(datagram, out var packet)
                && packet?.Header.Type == PgmPacketType.SourcePathMessage
                && packet.Body is PgmSourcePathMessage spm
                && spm.TrailingEdgeSequenceNumber == 0
                && spm.LeadingEdgeSequenceNumber == 0;
        }
    }
}
