namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A note a file has begun and not yet ended, while it is being read: a score holds a note with a length, and
/// a file holds the two halves of it some distance apart.
/// </summary>
internal sealed class MidiSoundingNote
{
    /// <summary>Creates the note.</summary>
    /// <param name="tick">Where it begins.</param>
    /// <param name="channel">Which channel it is on, 0 to 15.</param>
    /// <param name="note">The pitch, 0 to 127.</param>
    /// <param name="velocity">How hard it was struck, 1 to 127.</param>
    internal MidiSoundingNote(long tick, int channel, int note, int velocity)
    {
        Tick = tick;
        Channel = channel;
        Note = note;
        Velocity = velocity;
        Ends = -1;
    }

    /// <summary>Where it begins.</summary>
    internal long Tick { get; }

    /// <summary>Which channel it is on.</summary>
    internal int Channel { get; }

    /// <summary>The pitch.</summary>
    internal int Note { get; }

    /// <summary>How hard it was struck.</summary>
    internal int Velocity { get; }

    /// <summary>Where it ends, or below nought while the file has not said yet.</summary>
    internal long Ends { get; set; }
}
