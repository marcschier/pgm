// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Packets;

namespace Pgm.Receiver;

internal readonly struct SourceKey : IEquatable<SourceKey>
{
    private readonly ulong _globalSourceId;
    private readonly ushort _sourcePort;

    private SourceKey(ulong globalSourceId, ushort sourcePort)
    {
        _globalSourceId = globalSourceId;
        _sourcePort = sourcePort;
    }

    public static SourceKey FromHeader(PgmHeader header)
    {
        return new SourceKey(header.GlobalSourceId.Value, header.SourcePort);
    }

    public bool Equals(SourceKey other)
    {
        return _globalSourceId == other._globalSourceId && _sourcePort == other._sourcePort;
    }

    public override bool Equals(object? obj)
    {
        return obj is SourceKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)_globalSourceId * 397) ^ _sourcePort;
        }
    }
}
