// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using Pgm.Fec;
using Pgm.Net;
using Pgm.Packets;

namespace Pgm.Sender;

/// <summary>Provides the RFC 3208 PGM source state machine and transmit window.</summary>
public sealed class PgmSender : IAsyncDisposable
{
    private readonly IPgmDatagramChannel _channel;
    private readonly PgmSenderOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly TransmitWindow _window;
    private readonly Dictionary<uint, FecGroup> _fecGroups = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private uint _nextDataSequence = 1;
    private uint _nextSpmSequence = 1;
    private Task? _receiveTask;
    private ITimer? _spmTimer;
    private DateTimeOffset _nextDatagramTime;
    private bool _started;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="PgmSender" /> class.</summary>
    /// <param name="channel">The connected PGM datagram channel.</param>
    /// <param name="options">The sender options.</param>
    /// <param name="timeProvider">The time provider used for heartbeat timers.</param>
    public PgmSender(IPgmDatagramChannel channel, PgmSenderOptions options, TimeProvider? timeProvider = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Clone();
        _timeProvider = timeProvider ?? TimeProvider.System;
        ValidateOptions(_options);
        _window = new TransmitWindow(_options.TransmitWindowPacketCount);
        _nextDatagramTime = _timeProvider.GetUtcNow();
    }

    /// <summary>Starts the source receive loop and emits the initial ambient SPM.</summary>
    /// <param name="cancellationToken">A token that can cancel startup.</param>
    /// <returns>A value task that completes when the sender is started.</returns>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_started)
        {
            return;
        }

        _started = true;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_disposeCancellation.Token), CancellationToken.None);
        await SendSourcePathMessageAsync(cancellationToken).ConfigureAwait(false);
        _spmTimer = _timeProvider.CreateTimer(
            OnSourcePathMessageTimer,
            this,
            _options.SourcePathMessageInterval,
            _options.SourcePathMessageInterval);
    }

    /// <summary>Sends one APDU as one or more ODATA packets and records them in the transmit window.</summary>
    /// <param name="apdu">The application protocol data unit to transmit.</param>
    /// <param name="cancellationToken">A token that can cancel the send operation.</param>
    /// <returns>A value task that completes when all ODATA packets have been accepted for sending.</returns>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureStarted();

        int payloadLength = _options.MaximumDataPayloadLength;
        int fragmentCount = Math.Max(1, (apdu.Length + payloadLength - 1) / payloadLength);
        uint firstSequenceNumber = 0;
        int offset = 0;

        for (int fragment = 0; fragment < fragmentCount; fragment++)
        {
            int count = Math.Min(payloadLength, apdu.Length - offset);
            byte[] data = apdu.Slice(offset, count).ToArray();
            TxEntry entry;
            List<TxEntry> parityEntries;

            lock (_stateGate)
            {
                uint sequenceNumber = _nextDataSequence++;
                if (fragment == 0)
                {
                    firstSequenceNumber = sequenceNumber;
                }

                byte[] options = BuildFragmentOptions(fragmentCount, firstSequenceNumber, offset, apdu.Length);
                entry = new TxEntry(sequenceNumber, data, options, false);
                AddToWindow(entry);
                parityEntries = RecordFecSource(entry);
            }

            await SendEntryAsync(entry, PgmPacketType.OriginalData, cancellationToken).ConfigureAwait(false);

            for (int parity = 0; parity < parityEntries.Count; parity++)
            {
                await SendEntryAsync(
                    parityEntries[parity],
                    PgmPacketType.OriginalData,
                    cancellationToken).ConfigureAwait(false);
            }

            offset += count;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCancellation.Cancel();

        if (_spmTimer is not null)
        {
            await _spmTimer.DisposeAsync().ConfigureAwait(false);
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _disposeCancellation.Dispose();
        _sendGate.Dispose();
        await _channel.DisposeAsync().ConfigureAwait(false);
    }

    private static void ValidateOptions(PgmSenderOptions options)
    {
        if (options.GlobalSourceId is null)
        {
            throw new ArgumentException("A global source identifier is required.", nameof(options));
        }

        if (options.SourceAddress is null)
        {
            throw new ArgumentException("A source address is required.", nameof(options));
        }

        if (options.GroupAddress is null)
        {
            throw new ArgumentException("A group address is required.", nameof(options));
        }

        if (options.MaximumDataPayloadLength <= 0 || options.MaximumDataPayloadLength > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.TransmitWindowPacketCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.SourcePathMessageInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.MaximumDatagramsPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.FecTransmissionGroupSize <= 0 || options.FecTransmissionGroupSize > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.ProactiveParityPacketCount < 0 || options.OnDemandParityPacketCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static void OnSourcePathMessageTimer(object? state)
    {
        var sender = (PgmSender?)state;
        if (sender is not null)
        {
            _ = sender.SendSourcePathMessageFromTimerAsync();
        }
    }

    private async Task SendSourcePathMessageFromTimerAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await SendSourcePathMessageAsync(_disposeCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ushort.MaxValue];

        while (!cancellationToken.IsCancellationRequested)
        {
            int byteCount = await _channel.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (!PgmUdpEncapsulation.TryParsePayload(buffer.AsSpan(0, byteCount), out var packet) || packet is null)
            {
                continue;
            }

            if (packet.Header.Type == PgmPacketType.NegativeAcknowledgment)
            {
                await HandleNakAsync(packet, cancellationToken).ConfigureAwait(false);
            }
            else if (packet.Header.Type == PgmPacketType.SourcePathMessageRequest)
            {
                await SendSourcePathMessageAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleNakAsync(PgmPacket packet, CancellationToken cancellationToken)
    {
        if (!IsSessionPacket(packet) || !(packet.Body is PgmNakPacket nak))
        {
            return;
        }

        await RepairSequenceAsync(nak, nak.SequenceNumber, cancellationToken).ConfigureAwait(false);
        uint[] additionalSequences = ReadNakList(packet.GetOptionBytes());

        for (int i = 0; i < additionalSequences.Length; i++)
        {
            await RepairSequenceAsync(nak, additionalSequences[i], cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RepairSequenceAsync(PgmNakPacket nak, uint sequenceNumber, CancellationToken cancellationToken)
    {
        TxEntry? entry;
        List<TxEntry> onDemandParity = new List<TxEntry>();

        lock (_stateGate)
        {
            entry = _window.TryGet(sequenceNumber, out var found) ? found : null;
            if (entry is null && _options.OnDemandParityPacketCount > 0)
            {
                onDemandParity = BuildOnDemandParity(sequenceNumber);
            }
        }

        if (entry is null && onDemandParity.Count == 0)
        {
            return;
        }

        await SendNakConfirmationAsync(nak, sequenceNumber, cancellationToken).ConfigureAwait(false);

        if (entry is not null)
        {
            await SendEntryAsync(entry, PgmPacketType.RepairData, cancellationToken).ConfigureAwait(false);
            return;
        }

        for (int i = 0; i < onDemandParity.Count; i++)
        {
            await SendEntryAsync(onDemandParity[i], PgmPacketType.RepairData, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool IsSessionPacket(PgmPacket packet)
    {
        return packet.Header.SourcePort == _options.SourcePort
            && packet.Header.DestinationPort == _options.DestinationPort
            && packet.Header.GlobalSourceId.Value == _options.GlobalSourceId.Value;
    }

    private void AddToWindow(TxEntry entry)
    {
        _window.Add(entry);
        entry.TrailingEdgeSequenceNumber = _window.TrailingEdgeSequenceNumber;
    }

    private List<TxEntry> RecordFecSource(TxEntry entry)
    {
        List<TxEntry> parityEntries = new List<TxEntry>();
        int groupSize = _options.FecTransmissionGroupSize;
        uint groupNumber = GetFecGroupNumber(entry.SequenceNumber);

        if (!_fecGroups.TryGetValue(groupNumber, out var group))
        {
            group = new FecGroup(groupNumber, groupSize);
            _fecGroups.Add(groupNumber, group);
        }

        group.Add(entry);

        if (group.Count == groupSize && !group.ProactiveParityGenerated && _options.ProactiveParityPacketCount > 0)
        {
            group.ProactiveParityGenerated = true;
            parityEntries.AddRange(BuildParityEntries(group, _options.ProactiveParityPacketCount));
        }

        return parityEntries;
    }

    private List<TxEntry> BuildOnDemandParity(uint sequenceNumber)
    {
        uint groupNumber = GetFecGroupNumber(sequenceNumber);
        if (!_fecGroups.TryGetValue(groupNumber, out var group) || group.Count == 0)
        {
            return new List<TxEntry>();
        }

        return BuildParityEntries(group, _options.OnDemandParityPacketCount);
    }

    private List<TxEntry> BuildParityEntries(FecGroup group, int parityCount)
    {
        List<TxEntry> entries = new List<TxEntry>();
        int sourceCount = group.Count;

        if (sourceCount == 0 || parityCount == 0 || sourceCount + parityCount > 256)
        {
            return entries;
        }

        byte[][] sourceBlocks = group.GetPaddedBlocks();
        ReedSolomon reedSolomon = new ReedSolomon(sourceCount, parityCount);
        byte[][] parityBlocks = reedSolomon.Encode(sourceBlocks);

        for (int i = 0; i < parityBlocks.Length; i++)
        {
            TxEntry entry = new TxEntry(
                _nextDataSequence++,
                parityBlocks[i],
                BuildParityOptions(group.GroupNumber),
                true);
            AddToWindow(entry);
            entries.Add(entry);
        }

        return entries;
    }

    private uint GetFecGroupNumber(uint sequenceNumber)
    {
        return (sequenceNumber - 1) / (uint)_options.FecTransmissionGroupSize;
    }

    private async Task SendSourcePathMessageAsync(CancellationToken cancellationToken)
    {
        uint spmSequenceNumber;
        uint trailingEdgeSequenceNumber;
        uint leadingEdgeSequenceNumber;

        lock (_stateGate)
        {
            spmSequenceNumber = _nextSpmSequence++;
            trailingEdgeSequenceNumber = _window.TrailingEdgeSequenceNumber;
            leadingEdgeSequenceNumber = _nextDataSequence - 1;
        }

        PgmSourcePathMessage body = new PgmSourcePathMessage(
            spmSequenceNumber,
            trailingEdgeSequenceNumber,
            leadingEdgeSequenceNumber,
            _options.SourceAddress);
        PgmHeader header = CreateHeader(PgmPacketType.SourcePathMessage, PgmHeaderOptions.None, 0);
        PgmPacket packet = PgmPacket.CreateSourcePathMessage(header, body, Array.Empty<byte>());

        await SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendNakConfirmationAsync(
        PgmNakPacket nak,
        uint sequenceNumber,
        CancellationToken cancellationToken)
    {
        PgmNakPacket body = new PgmNakPacket(sequenceNumber, nak.Source, nak.Group);
        PgmHeader header = CreateHeader(PgmPacketType.NakConfirmation, PgmHeaderOptions.None, 0);
        PgmPacket packet = PgmPacket.CreateNakLike(header, body, Array.Empty<byte>());

        await SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendEntryAsync(
        TxEntry entry,
        PgmPacketType packetType,
        CancellationToken cancellationToken)
    {
        PgmHeaderOptions headerOptions = PgmOptionCodec.GetHeaderOptions(entry.Options);
        if (entry.IsParity)
        {
            headerOptions |= PgmHeaderOptions.Parity | PgmHeaderOptions.VariablePacketLength;
        }

        PgmHeader header = CreateHeader(packetType, headerOptions, (ushort)entry.Data.Length);
        PgmDataPacket body = new PgmDataPacket(
            entry.SequenceNumber,
            entry.TrailingEdgeSequenceNumber,
            entry.Data);
        PgmPacket packet = PgmPacket.CreateData(header, body, entry.Options);

        await SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    private PgmHeader CreateHeader(PgmPacketType type, PgmHeaderOptions options, ushort tsduLength)
    {
        return new PgmHeader(
            _options.SourcePort,
            _options.DestinationPort,
            type,
            options,
            0,
            _options.GlobalSourceId,
            tsduLength);
    }

    private async Task SendPacketAsync(PgmPacket packet, CancellationToken cancellationToken)
    {
        byte[] datagram = new byte[packet.EncodedLength];
        if (!PgmUdpEncapsulation.TryWritePayload(packet, datagram))
        {
            throw new InvalidOperationException("The PGM packet could not be encoded.");
        }

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ApplyRateLimitAsync(cancellationToken).ConfigureAwait(false);
            await _channel.SendAsync(datagram, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task ApplyRateLimitAsync(CancellationToken cancellationToken)
    {
        if (_options.MaximumDatagramsPerSecond <= 0)
        {
            return;
        }

        TimeSpan interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / _options.MaximumDatagramsPerSecond);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_nextDatagramTime > now)
        {
            TimeSpan delay = _nextDatagramTime - now;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            now = _timeProvider.GetUtcNow();
        }

        _nextDatagramTime = now + interval;
    }

    private static byte[] BuildFragmentOptions(int fragmentCount, uint firstSequenceNumber, int offset, int apduLength)
    {
        if (fragmentCount <= 1)
        {
            return Array.Empty<byte>();
        }

        byte[] options = new byte[20];
        _ = PgmOptionCodec.TryWriteLength(options, (ushort)options.Length, false);
        _ = PgmOptionCodec.TryWriteFragment(
            options.AsSpan(4),
            firstSequenceNumber,
            (uint)offset,
            (uint)apduLength,
            true);
        return options;
    }

    private byte[] BuildParityOptions(uint groupNumber)
    {
        byte[] options = new byte[20];
        _ = PgmOptionCodec.TryWriteLength(options, (ushort)options.Length, false);
        _ = PgmOptionCodec.TryWriteFec(
            options.AsSpan(4),
            (uint)_options.FecTransmissionGroupSize,
            _options.ProactiveParityPacketCount > 0,
            _options.OnDemandParityPacketCount > 0,
            false);
        _ = PgmOptionCodec.TryWriteParityGroup(options.AsSpan(12), groupNumber, true);
        return options;
    }

    private static uint[] ReadNakList(ReadOnlySpan<byte> options)
    {
        List<uint> sequenceNumbers = new List<uint>();
        int offset = 0;

        while (offset < options.Length)
        {
            if (!PgmOptionHeader.TryParse(options.Slice(offset), out var header) || header is null)
            {
                break;
            }

            if (header.Type == PgmOptionType.NakList)
            {
                int count = (header.Length - PgmOptionHeader.EncodedLength) / 4;
                ReadOnlySpan<byte> nakList = options.Slice(offset + PgmOptionHeader.EncodedLength);
                for (int i = 0; i < count; i++)
                {
                    sequenceNumbers.Add(BinaryPrimitives.ReadUInt32BigEndian(nakList.Slice(i * 4)));
                }
            }

            offset += header.Length;
            if (header.IsLast)
            {
                break;
            }
        }

        return sequenceNumbers.ToArray();
    }

    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException("The sender has not been started.");
        }
    }

    private void ThrowIfDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PgmSender));
        }
#endif
    }

    private sealed class TxEntry
    {
        internal TxEntry(uint sequenceNumber, byte[] data, byte[] options, bool isParity)
        {
            SequenceNumber = sequenceNumber;
            Data = data;
            Options = options;
            IsParity = isParity;
        }

        internal uint SequenceNumber { get; }

        internal uint TrailingEdgeSequenceNumber { get; set; }

        internal byte[] Data { get; }

        internal byte[] Options { get; }

        internal bool IsParity { get; }
    }

    private sealed class TransmitWindow
    {
        private readonly int _capacity;
        private readonly Dictionary<uint, TxEntry> _entries = new Dictionary<uint, TxEntry>();
        private readonly Queue<uint> _order = new Queue<uint>();

        internal TransmitWindow(int capacity)
        {
            _capacity = capacity;
        }

        internal uint TrailingEdgeSequenceNumber => _order.Count == 0 ? 0 : _order.Peek();

        internal void Add(TxEntry entry)
        {
            _entries[entry.SequenceNumber] = entry;
            _order.Enqueue(entry.SequenceNumber);

            while (_order.Count > _capacity)
            {
                uint removed = _order.Dequeue();
                _entries.Remove(removed);
            }
        }

        internal bool TryGet(uint sequenceNumber, out TxEntry? entry)
        {
            return _entries.TryGetValue(sequenceNumber, out entry);
        }
    }

    private sealed class FecGroup
    {
        private readonly TxEntry?[] _entries;

        internal FecGroup(uint groupNumber, int groupSize)
        {
            GroupNumber = groupNumber;
            _entries = new TxEntry[groupSize];
        }

        internal uint GroupNumber { get; }

        internal int Count { get; private set; }

        internal bool ProactiveParityGenerated { get; set; }

        internal void Add(TxEntry entry)
        {
            int index = (int)((entry.SequenceNumber - 1) % (uint)_entries.Length);
            if (_entries[index] is null)
            {
                Count++;
            }

            _entries[index] = entry;
        }

        internal byte[][] GetPaddedBlocks()
        {
            int blockLength = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                TxEntry? entry = _entries[i];
                if (entry is not null && entry.Data.Length > blockLength)
                {
                    blockLength = entry.Data.Length;
                }
            }

            byte[][] blocks = new byte[Count][];
            int block = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                TxEntry? entry = _entries[i];
                if (entry is null)
                {
                    continue;
                }

                blocks[block] = new byte[blockLength];
                entry.Data.AsSpan().CopyTo(blocks[block]);
                block++;
            }

            return blocks;
        }
    }
}
