// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
using Pgm.Packets;

namespace Pgm;

internal static class PgmAddressConversion
{
    internal static PgmNetworkAddress ToPgmAddress(IPAddress address)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(address);
#else
        if (address is null)
        {
            throw new ArgumentNullException(nameof(address));
        }
#endif

        PgmAddressFamily family = address.AddressFamily == AddressFamily.InterNetwork
            ? PgmAddressFamily.IPv4
            : PgmAddressFamily.IPv6;

        if (address.AddressFamily != AddressFamily.InterNetwork
            && address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            throw new ArgumentException("The address must be IPv4 or IPv6.", nameof(address));
        }

        return new PgmNetworkAddress(family, address.GetAddressBytes());
    }
}
