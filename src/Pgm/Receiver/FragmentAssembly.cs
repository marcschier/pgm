// Copyright (c) marcschier. Licensed under the MIT License.

namespace Pgm.Receiver;

internal sealed class FragmentAssembly
{
    private readonly byte[] data;
    private readonly List<Range> ranges = new();
    private int receivedLength;

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

        data = new byte[(int)length];
    }

    public bool IsComplete => receivedLength == data.Length;

    public bool TryAdd(uint offset, byte[] fragment)
    {
        if (offset > int.MaxValue || offset + (uint)fragment.Length > (uint)data.Length)
        {
            return false;
        }

        int start = (int)offset;
        int end = start + fragment.Length;

        for (int index = 0; index < ranges.Count; index++)
        {
            if (start < ranges[index].End && end > ranges[index].Start)
            {
                return false;
            }
        }

        fragment.AsSpan().CopyTo(data.AsSpan(start));
        ranges.Add(new Range(start, end));
        receivedLength += fragment.Length;
        return true;
    }

    public byte[] ToArray()
    {
        var copy = new byte[data.Length];
        data.AsSpan().CopyTo(copy);
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
