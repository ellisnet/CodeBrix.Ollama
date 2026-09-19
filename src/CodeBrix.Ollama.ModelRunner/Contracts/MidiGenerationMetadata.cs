namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a loaded MIDI-generating model says about itself: the size of a tick, how far back it can look, and
/// the shape of the vocabulary it generates in.
/// </summary>
public sealed class MidiGenerationMetadata
{
    /// <summary>Creates the description.</summary>
    /// <param name="architecture">The architecture the bundle names.</param>
    /// <param name="tokenizerVersion">Which of the publisher's tokenizers the bundle asks for.</param>
    /// <param name="ticksPerQuarterNote">How many ticks a quarter note lasts in everything it produces.</param>
    /// <param name="maximumContextEvents">How many events of history it reads.</param>
    /// <param name="vocabularySize">How many tokens there are altogether.</param>
    /// <param name="maximumTokensPerEvent">How many tokens one event is made of.</param>
    /// <param name="baseGraphFileName">The bundle's name for the graph that reads the events.</param>
    /// <param name="tokenGraphFileName">The bundle's name for the graph that writes an event's tokens.</param>
    public MidiGenerationMetadata(
        string architecture,
        string tokenizerVersion,
        int ticksPerQuarterNote,
        int maximumContextEvents,
        int vocabularySize,
        int maximumTokensPerEvent,
        string baseGraphFileName,
        string tokenGraphFileName)
    {
        Architecture = architecture;
        TokenizerVersion = tokenizerVersion;
        TicksPerQuarterNote = ticksPerQuarterNote;
        MaximumContextEvents = maximumContextEvents;
        VocabularySize = vocabularySize;
        MaximumTokensPerEvent = maximumTokensPerEvent;
        BaseGraphFileName = baseGraphFileName;
        TokenGraphFileName = tokenGraphFileName;
    }

    /// <summary>The architecture the bundle names.</summary>
    public string Architecture { get; }

    /// <summary>Which of the publisher's tokenizers the bundle asks for.</summary>
    public string TokenizerVersion { get; }

    /// <summary>
    /// How many ticks a quarter note lasts in every <see cref="MidiEvent"/> and every
    /// <see cref="MidiScore"/> this model produces. It is what turns a tick into a length of time, together
    /// with the piece's own tempo.
    /// </summary>
    public int TicksPerQuarterNote { get; }

    /// <summary>
    /// How many events of history the model reads. A piece longer than this keeps going: the model simply
    /// stops seeing the beginning of it.
    /// </summary>
    public int MaximumContextEvents { get; }

    /// <summary>How many tokens there are altogether in the vocabulary it generates in.</summary>
    public int VocabularySize { get; }

    /// <summary>
    /// How many tokens one event is made of - the kind, its parameters, and padding out to this length.
    /// </summary>
    public int MaximumTokensPerEvent { get; }

    /// <summary>The bundle's name for the graph that reads the events so far.</summary>
    public string BaseGraphFileName { get; }

    /// <summary>The bundle's name for the graph that writes one event's tokens.</summary>
    public string TokenGraphFileName { get; }
}
