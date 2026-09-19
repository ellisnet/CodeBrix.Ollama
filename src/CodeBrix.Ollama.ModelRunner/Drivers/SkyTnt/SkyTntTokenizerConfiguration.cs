using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a bundle's <c>config.json</c> says about its tokenizer, read as it stands so that the port can be
/// held to it.
/// </summary>
/// <remarks>
/// THE FILE DOES NOT DECIDE THE VOCABULARY, and it cannot: the order the tokens are allotted in is the
/// tokenizer's own and a JSON object does not carry an order. What the file gives is the shape of the answer -
/// which version, how many tokens, how long a row is, which kinds of event there are and what each one's
/// parameters are - and the port is CHECKED against every one of those before a model is run, so a bundle
/// that means something else is turned away at load rather than generating nonsense.
/// </remarks>
internal sealed class SkyTntTokenizerConfiguration
{
    /// <summary>Creates the description.</summary>
    /// <param name="version">Which tokenizer the bundle wants, for example <c>v2</c>.</param>
    /// <param name="optimiseMidi">Whether the tokenizer tidies a piece up as it reads it.</param>
    /// <param name="vocabularySize">How many tokens there are altogether.</param>
    /// <param name="maximumTokensPerEvent">How long an event's row of tokens is.</param>
    /// <param name="padId">The token that fills the unused end of a row.</param>
    /// <param name="beginningId">The token a piece starts with.</param>
    /// <param name="endId">The token that ends a piece.</param>
    /// <param name="events">Each kind of event against the parameters it is made of, in order.</param>
    /// <param name="eventParameters">Each parameter family against how many values it has.</param>
    internal SkyTntTokenizerConfiguration(
        string version,
        bool optimiseMidi,
        int vocabularySize,
        int maximumTokensPerEvent,
        int padId,
        int beginningId,
        int endId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> events,
        IReadOnlyDictionary<string, int> eventParameters)
    {
        Version = version;
        OptimiseMidi = optimiseMidi;
        VocabularySize = vocabularySize;
        MaximumTokensPerEvent = maximumTokensPerEvent;
        PadId = padId;
        BeginningId = beginningId;
        EndId = endId;
        Events = events;
        EventParameters = eventParameters;
    }

    /// <summary>Which tokenizer the bundle wants.</summary>
    internal string Version { get; }

    /// <summary>Whether the tokenizer tidies a piece up as it reads it.</summary>
    internal bool OptimiseMidi { get; }

    /// <summary>How many tokens there are altogether.</summary>
    internal int VocabularySize { get; }

    /// <summary>How long an event's row of tokens is.</summary>
    internal int MaximumTokensPerEvent { get; }

    /// <summary>The token that fills the unused end of a row.</summary>
    internal int PadId { get; }

    /// <summary>The token a piece starts with.</summary>
    internal int BeginningId { get; }

    /// <summary>The token that ends a piece.</summary>
    internal int EndId { get; }

    /// <summary>Each kind of event against the parameters it is made of, in order.</summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> Events { get; }

    /// <summary>Each parameter family against how many values it has.</summary>
    internal IReadOnlyDictionary<string, int> EventParameters { get; }
}
