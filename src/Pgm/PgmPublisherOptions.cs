// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using Pgm.Packets;

namespace Pgm;

/// <summary>Configures a <see cref="PgmPublisher" /> UDP multicast session.</summary>
public sealed class PgmPublisherOptions
{
    /// <summary>Gets or sets the multicast group address.</summary>
    public IPAddress MulticastGroup { get; set; } = IPAddress.Parse("239.192.0.1");

    /// <summary>Gets or sets the UDP port used for multicast datagrams.</summary>
    public int Port { get; set; } = PgmUdpEncapsulation.DefaultPort;

    /// <summary>Gets or sets the local interface address, or <see langword="null" /> to use any interface.</summary>
    public IPAddress? InterfaceAddress { get; set; }

    /// <summary>Gets or sets the IPv6 interface index, or zero for the default interface.</summary>
    public long InterfaceIndex { get; set; }

    /// <summary>Gets or sets the maximum APDU bytes carried by one PGM data packet before fragmentation.</summary>
    public int MaximumDataPayloadLength { get; set; } = 1200;

    /// <summary>Gets or sets the maximum number of data packets retained for repairs.</summary>
    public int TransmitWindowPacketCount { get; set; } = 1024;

    /// <summary>Gets or sets the interval between source path heartbeat packets.</summary>
    public TimeSpan SourcePathMessageInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the number of source packets in a FEC transmission group.</summary>
    public int FecTransmissionGroupSize { get; set; } = 4;

    /// <summary>Gets or sets the number of proactive parity packets generated for each complete FEC group.</summary>
    public int ProactiveParityPacketCount { get; set; }

    /// <summary>Gets or sets the number of on-demand parity packets sent when direct repair is unavailable.</summary>
    public int OnDemandParityPacketCount { get; set; } = 1;

    internal PgmPublisherOptions Clone()
    {
        return new PgmPublisherOptions
        {
            MulticastGroup = MulticastGroup,
            Port = Port,
            InterfaceAddress = InterfaceAddress,
            InterfaceIndex = InterfaceIndex,
            MaximumDataPayloadLength = MaximumDataPayloadLength,
            TransmitWindowPacketCount = TransmitWindowPacketCount,
            SourcePathMessageInterval = SourcePathMessageInterval,
            FecTransmissionGroupSize = FecTransmissionGroupSize,
            ProactiveParityPacketCount = ProactiveParityPacketCount,
            OnDemandParityPacketCount = OnDemandParityPacketCount,
        };
    }
}
