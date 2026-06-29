// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Receiver;

internal readonly struct SourceKey : IEquatable<SourceKey>
{
    private readonly ulong globalSourceId;
    private readonly ushort sourcePort;

    private SourceKey(ulong globalSourceId, ushort sourcePort)
    {
        this.globalSourceId = globalSourceId;
        this.sourcePort = sourcePort;
    }

    public static SourceKey FromHeader(PgmHeader header)
    {
        return new SourceKey(header.GlobalSourceId.Value, header.SourcePort);
    }

    public bool Equals(SourceKey other)
    {
        return globalSourceId == other.globalSourceId && sourcePort == other.sourcePort;
    }

    public override bool Equals(object? obj)
    {
        return obj is SourceKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)globalSourceId * 397) ^ sourcePort;
        }
    }
}
