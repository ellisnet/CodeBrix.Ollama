namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One message on its way into a track chunk: where it happens, what order it takes at that position, and the
/// bytes it is written as.
/// </summary>
internal sealed class MidiFileMessage
{
    /// <summary>Creates the message.</summary>
    /// <param name="tick">Ticks from the start of the piece.</param>
    /// <param name="phase">What order it takes among the messages at that same position.</param>
    /// <param name="sequence">Where it came in the score, which settles a tie inside one phase.</param>
    /// <param name="bytes">The message itself, status byte first.</param>
    internal MidiFileMessage(long tick, MidiFileMessagePhase phase, int sequence, byte[] bytes)
    {
        Tick = tick;
        Phase = phase;
        Sequence = sequence;
        Bytes = bytes;
    }

    /// <summary>Ticks from the start of the piece.</summary>
    internal long Tick { get; }

    /// <summary>What order it takes among the messages at that same position.</summary>
    internal MidiFileMessagePhase Phase { get; }

    /// <summary>Where it came in the score, which settles a tie inside one phase.</summary>
    internal int Sequence { get; }

    /// <summary>The message itself, status byte first.</summary>
    internal byte[] Bytes { get; }

    /// <summary>Orders two messages: by position, then by phase, then by the order they came in.</summary>
    /// <param name="left">One message.</param>
    /// <param name="right">The other.</param>
    /// <returns>Less than nought, nought, or more than nought.</returns>
    internal static int Compare(MidiFileMessage left, MidiFileMessage right)
    {
        int byTick = left.Tick.CompareTo(right.Tick);
        if (byTick != 0) return byTick;

        int byPhase = ((int)left.Phase).CompareTo((int)right.Phase);
        if (byPhase != 0) return byPhase;

        return left.Sequence.CompareTo(right.Sequence);
    }
}
