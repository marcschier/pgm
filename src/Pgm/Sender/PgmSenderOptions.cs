// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Sender;

/// <summary>Configures a PGM source session.</summary>
public sealed class PgmSenderOptions
{
    /// <summary>Initializes a new instance of the <see cref="PgmSenderOptions" /> class.</summary>
    public PgmSenderOptions()
    {
        SourceAddress = new PgmNetworkAddress(PgmAddressFamily.IPv4, new byte[] { 127, 0, 0, 1 });
        GroupAddress = new PgmNetworkAddress(PgmAddressFamily.IPv4, new byte[] { 239, 192, 0, 1 });
        GlobalSourceId = new PgmGlobalSourceId(1);
    }

    /// <summary>Gets or sets the source transport-session port.</summary>
    public ushort SourcePort { get; set; } = 7500;

    /// <summary>Gets or sets the destination transport-session port.</summary>
    public ushort DestinationPort { get; set; } = 7501;

    /// <summary>Gets or sets the source TSI global source identifier.</summary>
    public PgmGlobalSourceId GlobalSourceId { get; set; }

    /// <summary>Gets or sets the source path address advertised by SPM and NCF packets.</summary>
    public PgmNetworkAddress SourceAddress { get; set; }

    /// <summary>Gets or sets the multicast group address advertised by NCF packets.</summary>
    public PgmNetworkAddress GroupAddress { get; set; }

    /// <summary>Gets or sets the maximum APDU bytes carried by one ODATA packet before fragmentation.</summary>
    public int MaximumDataPayloadLength { get; set; } = 1200;

    /// <summary>Gets or sets the maximum number of data packets retained for repairs.</summary>
    public int TransmitWindowPacketCount { get; set; } = 1024;

    /// <summary>Gets or sets the interval between heartbeat SPM packets.</summary>
    public TimeSpan SourcePathMessageInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the maximum number of datagrams sent per second, or zero for unlimited.</summary>
    public int MaximumDatagramsPerSecond { get; set; }

    /// <summary>Gets or sets the number of source packets in a FEC transmission group.</summary>
    public int FecTransmissionGroupSize { get; set; } = 4;

    /// <summary>Gets or sets the number of proactive parity packets generated for each complete FEC group.</summary>
    public int ProactiveParityPacketCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the number of on-demand parity packets sent when a NAK cannot be repaired directly.
    /// </summary>
    public int OnDemandParityPacketCount { get; set; } = 1;

    /// <summary>Gets a copy of this options instance.</summary>
    /// <returns>The copied options.</returns>
    public PgmSenderOptions Clone()
    {
        return new PgmSenderOptions
        {
            SourcePort = SourcePort,
            DestinationPort = DestinationPort,
            GlobalSourceId = GlobalSourceId,
            SourceAddress = SourceAddress,
            GroupAddress = GroupAddress,
            MaximumDataPayloadLength = MaximumDataPayloadLength,
            TransmitWindowPacketCount = TransmitWindowPacketCount,
            SourcePathMessageInterval = SourcePathMessageInterval,
            MaximumDatagramsPerSecond = MaximumDatagramsPerSecond,
            FecTransmissionGroupSize = FecTransmissionGroupSize,
            ProactiveParityPacketCount = ProactiveParityPacketCount,
            OnDemandParityPacketCount = OnDemandParityPacketCount,
        };
    }
}
