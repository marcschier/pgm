// Copyright (c) marcschier. Licensed under the MIT License.

using System.Threading.Channels;
using Pgm.Net;
using Pgm.Packets;

namespace Pgm.Receiver;

/// <summary>Receives reliable PGM APDUs from one or more RFC 3208 transport-session sources.</summary>
public sealed class PgmReceiver : IAsyncDisposable
{
    private const int MaximumDatagramSize = 65535;

    private readonly object gate = new();
    private readonly IPgmDatagramChannel channel;
    private readonly PgmReceiverOptions options;
    private readonly TimeProvider timeProvider;
    private readonly Channel<byte[]> completedApdus;
    private readonly Dictionary<SourceKey, ReceiveWindow> windows = new();
    private readonly CancellationTokenSource stop = new();
    private Task? receiveTask;
    private Task? nakTask;
    private bool started;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="PgmReceiver"/> class.</summary>
    /// <param name="channel">The datagram channel joined to the PGM multicast group.</param>
    /// <param name="timeProvider">The time provider used for testable NAK timers.</param>
    /// <param name="options">The receiver options, or <see langword="null"/> to use defaults.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="channel"/> or <paramref name="timeProvider"/> is null.
    /// </exception>
    public PgmReceiver(
        IPgmDatagramChannel channel,
        TimeProvider timeProvider,
        PgmReceiverOptions? options = null)
    {
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.options = (options ?? new PgmReceiverOptions()).Clone();
        completedApdus = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>Starts receiving datagrams and sends an SPMR source-discovery request.</summary>
    /// <param name="cancellationToken">A token that can cancel the start operation.</param>
    /// <returns>A task that completes when receiver background processing has started.</returns>
    /// <exception cref="ObjectDisposedException">The receiver is disposed.</exception>
    /// <exception cref="InvalidOperationException">The receiver has already been started.</exception>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        lock (gate)
        {
            if (started)
            {
                throw new InvalidOperationException("The receiver has already been started.");
            }

            started = true;
            receiveTask = Task.Run(ReceiveLoopAsync, CancellationToken.None);
            nakTask = Task.Run(NakLoopAsync, CancellationToken.None);
        }

        await SendPacketAsync(CreateSpmr(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Receives the next completely reassembled APDU.</summary>
    /// <param name="cancellationToken">A token that can cancel the receive operation.</param>
    /// <returns>The next APDU bytes.</returns>
    /// <exception cref="ObjectDisposedException">The receiver is disposed before an APDU is available.</exception>
    public async ValueTask<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return await completedApdus.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
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
        Task? localReceiveTask;
        Task? localNakTask;

        lock (gate)
        {
            localReceiveTask = receiveTask;
            localNakTask = nakTask;
        }

        await AwaitTaskAsync(localReceiveTask).ConfigureAwait(false);
        await AwaitTaskAsync(localNakTask).ConfigureAwait(false);
        completedApdus.Writer.TryComplete();
        stop.Dispose();
    }

    private static async ValueTask AwaitTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReceiveLoopAsync()
    {
        byte[] buffer = new byte[MaximumDatagramSize];

        while (!stop.IsCancellationRequested)
        {
            int length;

            try
            {
                length = await channel.ReceiveAsync(buffer, stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (PgmPacket.TryParse(buffer.AsSpan(0, length), out var packet) && packet is not null)
            {
                ProcessPacket(packet);
            }
        }
    }

    private async Task NakLoopAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            List<PgmPacket> naks;

            lock (gate)
            {
                naks = CollectDueNaks();
            }

            for (int index = 0; index < naks.Count; index++)
            {
                try
                {
                    await SendPacketAsync(naks[index], stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            lock (gate)
            {
                DrainCompletedApdus();
            }

            try
            {
                await DelayAsync(TimeSpan.FromMilliseconds(5), stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private List<PgmPacket> CollectDueNaks()
    {
        long now = timeProvider.GetTimestamp();
        var packets = new List<PgmPacket>();

        foreach (ReceiveWindow window in windows.Values)
        {
            window.CollectDueNaks(now, packets);
        }

        return packets;
    }

    private void ProcessPacket(PgmPacket packet)
    {
        lock (gate)
        {
            var key = SourceKey.FromHeader(packet.Header);
            if (!windows.TryGetValue(key, out var window))
            {
                window = new ReceiveWindow(key, this, options, timeProvider);
                windows.Add(key, window);
            }

            if (window.ProcessPacket(packet))
            {
                DrainCompletedApdus();
            }
        }
    }

    private void DrainCompletedApdus()
    {
        foreach (ReceiveWindow window in windows.Values)
        {
            while (window.TryDequeueCompletedApdu(out var apdu) && apdu is not null)
            {
                completedApdus.Writer.TryWrite(apdu);
            }
        }
    }

    private PgmPacket CreateSpmr()
    {
        return PgmPacket.CreateSourcePathMessageRequest(
            new PgmHeader(
                options.SourcePort,
                options.DestinationPort,
                PgmPacketType.SourcePathMessageRequest,
                PgmHeaderOptions.None,
                0,
                options.ReceiverGlobalSourceId,
                0),
            Array.Empty<byte>());
    }

    private async ValueTask SendPacketAsync(PgmPacket packet, CancellationToken cancellationToken)
    {
        byte[] datagram = new byte[packet.EncodedLength];
        if (!packet.TryWrite(datagram))
        {
            throw new InvalidOperationException("The PGM packet could not be encoded.");
        }

        await channel.SendAsync(datagram, cancellationToken).ConfigureAwait(false);
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ITimer timer = timeProvider.CreateTimer(
            _ => completion.TrySetResult(true),
            null,
            delay,
            Timeout.InfiniteTimeSpan))
        using (cancellationToken.Register(() => completion.TrySetCanceled()))
        {
            await completion.Task.ConfigureAwait(false);
        }
    }

    private void EnsureNotDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(disposed, this);
#else
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(PgmReceiver));
        }
#endif
    }
}
