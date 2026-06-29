// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using Pgm.Packets;

namespace Pgm;

/// <summary>Configures a <see cref="PgmSubscriber" /> UDP multicast session.</summary>
public sealed class PgmSubscriberOptions
{
    /// <summary>Gets or sets the multicast group address.</summary>
    public IPAddress MulticastGroup { get; set; } = IPAddress.Parse("239.192.0.1");

    /// <summary>Gets or sets the UDP port used for multicast datagrams.</summary>
    public int Port { get; set; } = PgmUdpEncapsulation.DefaultPort;

    /// <summary>Gets or sets the local interface address, or <see langword="null" /> to use any interface.</summary>
    public IPAddress? InterfaceAddress { get; set; }

    /// <summary>Gets or sets the IPv6 interface index, or zero for the default interface.</summary>
    public long InterfaceIndex { get; set; }

    /// <summary>Gets or sets the initial time between NAK transmissions.</summary>
    public TimeSpan InitialNakBackoff { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>Gets or sets the maximum time between repeated NAK transmissions.</summary>
    public TimeSpan MaximumNakBackoff { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or sets the time to wait for NCF before retransmitting a NAK.</summary>
    public TimeSpan NakConfirmationTimeout { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>Gets or sets the maximum number of NAK transmissions before declaring data loss.</summary>
    public int MaximumNakAttempts { get; set; } = 5;

    internal PgmSubscriberOptions Clone()
    {
        return new PgmSubscriberOptions
        {
            MulticastGroup = MulticastGroup,
            Port = Port,
            InterfaceAddress = InterfaceAddress,
            InterfaceIndex = InterfaceIndex,
            InitialNakBackoff = InitialNakBackoff,
            MaximumNakBackoff = MaximumNakBackoff,
            NakConfirmationTimeout = NakConfirmationTimeout,
            MaximumNakAttempts = MaximumNakAttempts,
        };
    }
}
