// Copyright (c) marcschier. Licensed under the MIT License.

namespace Pgm.Receiver;

internal sealed class NakState
{
    public NakState(uint sequenceNumber, long now)
    {
        SequenceNumber = sequenceNumber;
        DueTimestamp = now;
    }

    public uint SequenceNumber { get; }

    public int Attempts { get; private set; }

    public bool AwaitingConfirmation { get; private set; }

    public TimeSpan CurrentBackoff { get; private set; } = TimeSpan.Zero;

    public long DueTimestamp { get; private set; }

    public void MarkSent(long now, TimeSpan initialBackoff, TimeSpan maximumBackoff, long confirmationTicks)
    {
        Attempts++;
        AwaitingConfirmation = true;
        DueTimestamp = now + confirmationTicks;
        CurrentBackoff = CurrentBackoff == TimeSpan.Zero
            ? initialBackoff
            : TimeSpan.FromTicks(Math.Min(CurrentBackoff.Ticks * 2, maximumBackoff.Ticks));
    }

    public void MarkConfirmed(long now, long backoffTicks)
    {
        AwaitingConfirmation = false;
        DueTimestamp = now + backoffTicks;
    }
}
