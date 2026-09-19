namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The order two messages at the same position are written in, which decides what a player hears when a note
/// ends exactly where another begins.
/// </summary>
internal enum MidiFileMessagePhase
{
    /// <summary>A note stops. It goes first, so a note trimmed to end where the next one starts is heard.</summary>
    NoteOff = 0,

    /// <summary>An instrument, a controller, a tempo or a signature is set, before anything sounds under it.</summary>
    Setup = 1,

    /// <summary>A note starts, after everything that decides how it will sound.</summary>
    NoteOn = 2,
}
