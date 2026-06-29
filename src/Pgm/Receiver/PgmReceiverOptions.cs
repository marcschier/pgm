// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Receiver;

/// <summary>Configures a <see cref="PgmReceiver"/> instance.</summary>
public sealed class PgmReceiverOptions
{
    /// <summary>Gets or sets the source port used by receiver-originated control packets.</summary>
    public ushort SourcePort { get; set; } = 7501;

    /// <summary>Gets or sets the destination port used by receiver-originated control packets.</summary>
    public ushort DestinationPort { get; set; } = 7500;

    /// <summary>Gets or sets the receiver global source identifier used for SPMR and NAK headers.</summary>
    public PgmGlobalSourceId ReceiverGlobalSourceId { get; set; } = new PgmGlobalSourceId(1);

    /// <summary>Gets or sets the multicast group network-layer address advertised in NAK packets.</summary>
    public PgmNetworkAddress GroupAddress { get; set; } = new(PgmAddressFamily.IPv4, new byte[] { 239, 192, 0, 1 });

    /// <summary>Gets or sets the fallback source network-layer address used before an SPM path is known.</summary>
    public PgmNetworkAddress DefaultSourceAddress { get; set; } = new(PgmAddressFamily.IPv4, new byte[] { 0, 0, 0, 0 });

    /// <summary>Gets or sets the number of data sequence numbers retained per source.</summary>
    public int ReceiveWindowSize { get; set; } = 4096;

    /// <summary>Gets or sets the initial time between NAK transmissions.</summary>
    public TimeSpan InitialNakBackoff { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>Gets or sets the maximum time between repeated NAK transmissions.</summary>
    public TimeSpan MaximumNakBackoff { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or sets the time to wait for NCF before retransmitting a NAK.</summary>
    public TimeSpan NakConfirmationTimeout { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>Gets or sets the maximum number of NAK transmissions before declaring data loss.</summary>
    public int MaximumNakAttempts { get; set; } = 5;

    internal PgmReceiverOptions Clone()
    {
        return new PgmReceiverOptions
        {
            SourcePort = SourcePort,
            DestinationPort = DestinationPort,
            ReceiverGlobalSourceId = ReceiverGlobalSourceId,
            GroupAddress = GroupAddress,
            DefaultSourceAddress = DefaultSourceAddress,
            ReceiveWindowSize = ReceiveWindowSize,
            InitialNakBackoff = InitialNakBackoff,
            MaximumNakBackoff = MaximumNakBackoff,
            NakConfirmationTimeout = NakConfirmationTimeout,
            MaximumNakAttempts = MaximumNakAttempts,
        };
    }
}
