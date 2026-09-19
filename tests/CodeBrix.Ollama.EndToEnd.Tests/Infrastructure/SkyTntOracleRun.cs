using System.Collections.Generic;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// What one run of the publisher's own generation loop produced.
/// </summary>
public sealed class SkyTntOracleRun
{
    /// <summary>Creates the record.</summary>
    /// <param name="promptBeats">How far into the piece the prompt already reaches, in whole beats.</param>
    /// <param name="promptRows">The rows of tokens it started from.</param>
    /// <param name="events">The events it wrote.</param>
    /// <param name="seconds">How long it took.</param>
    /// <param name="midiPath">The Standard MIDI File it wrote.</param>
    public SkyTntOracleRun(
        int promptBeats,
        IReadOnlyList<IReadOnlyList<int>> promptRows,
        IReadOnlyList<SkyTntOracleEvent> events,
        double seconds,
        string midiPath)
    {
        PromptBeats = promptBeats;
        PromptRows = promptRows;
        Events = events;
        Seconds = seconds;
        MidiPath = midiPath;
    }

    /// <summary>How far into the piece the prompt already reaches, in whole beats.</summary>
    public int PromptBeats { get; }

    /// <summary>The rows of tokens it started from.</summary>
    public IReadOnlyList<IReadOnlyList<int>> PromptRows { get; }

    /// <summary>The events it wrote.</summary>
    public IReadOnlyList<SkyTntOracleEvent> Events { get; }

    /// <summary>How long it took.</summary>
    public double Seconds { get; }

    /// <summary>The Standard MIDI File it wrote, written by the publisher's own writer.</summary>
    public string MidiPath { get; }
}
