namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a <see cref="MidiEvent"/> is. The six kinds are everything a MIDI-generating model of this family
/// produces and everything needed to play the result back.
/// </summary>
public enum MidiEventKind
{
    /// <summary>
    /// A sounded note, with a pitch, a loudness and a DURATION - there is no separate note-off event, so a
    /// player never has to pair one event with another or worry about a note left hanging.
    /// </summary>
    Note = 0,

    /// <summary>A change of instrument (a patch, or program, change) on one channel.</summary>
    ProgramChange = 1,

    /// <summary>A controller change on one channel - a volume, a pan, a sustain pedal and so on.</summary>
    ControlChange = 2,

    /// <summary>A change of speed, in beats per minute, from this point on.</summary>
    Tempo = 3,

    /// <summary>A change of time signature from this point on.</summary>
    TimeSignature = 4,

    /// <summary>A change of key signature from this point on.</summary>
    KeySignature = 5,
}
