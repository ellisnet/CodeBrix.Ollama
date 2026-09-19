using System.Collections.Generic;
using CodeBrix.Ollama.ModelRunner;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// What one generation produced: the events as they were handed over, the piece they gather into, and how
/// long the whole thing took.
/// </summary>
/// <remarks>
/// All three come from ONE loaded model. Loading the full-precision pair costs a second and most of a
/// gigabyte, so a measurement that reloaded it to ask a second question would be measuring the load.
/// </remarks>
public sealed class SkyTntGenerated
{
    /// <summary>Creates the record.</summary>
    /// <param name="events">The events as they were handed over.</param>
    /// <param name="score">The piece they gather into, tidied as the model's own tokenizer tidies one.</param>
    /// <param name="seconds">How long the generation took.</param>
    public SkyTntGenerated(IReadOnlyList<MidiEvent> events, MidiScore score, double seconds)
    {
        Events = events;
        Score = score;
        Seconds = seconds;
    }

    /// <summary>The events as they were handed over.</summary>
    public IReadOnlyList<MidiEvent> Events { get; }

    /// <summary>The piece they gather into.</summary>
    public MidiScore Score { get; }

    /// <summary>How long the generation took.</summary>
    public double Seconds { get; }
}
