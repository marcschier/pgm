// Copyright (c) marcschier. Licensed under the MIT License.

namespace Pgm.Receiver;

internal sealed class FragmentAssembly
{
    private readonly byte[] _data;
    private readonly List<Range> _ranges = new();
    private int _receivedLength;

    public FragmentAssembly(uint length)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, (uint)int.MaxValue);
#else
        if (length > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
#endif

        _data = new byte[(int)length];
    }

    public bool IsComplete => _receivedLength == _data.Length;

    public bool TryAdd(uint offset, byte[] fragment)
    {
        if (offset > int.MaxValue || offset + (uint)fragment.Length > (uint)_data.Length)
        {
            return false;
        }

        int start = (int)offset;
        int end = start + fragment.Length;

        for (int index = 0; index < _ranges.Count; index++)
        {
            if (start < _ranges[index].End && end > _ranges[index].Start)
            {
                return false;
            }
        }

        fragment.AsSpan().CopyTo(_data.AsSpan(start));
        _ranges.Add(new Range(start, end));
        _receivedLength += fragment.Length;
        return true;
    }

    public byte[] ToArray()
    {
        var copy = new byte[_data.Length];
        _data.AsSpan().CopyTo(copy);
        return copy;
    }

    private readonly struct Range
    {
        public Range(int start, int end)
        {
            Start = start;
            End = end;
        }

        public int Start { get; }

        public int End { get; }
    }
}
