using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One generation, settled: the rows of tokens it starts from, how many events to add, how the choice is made
/// at each token, and which tokens are refused throughout.
/// </summary>
/// <remarks>
/// A plan is built once from the caller's options, validated there, and read from then on - so the options
/// object the caller keeps can be changed afterwards without reaching into a generation that is running.
/// </remarks>
internal sealed class SkyTntGenerationPlan
{
    /// <summary>Creates the plan.</summary>
    /// <param name="promptRows">The rows of tokens the piece starts from, the beginning token included.</param>
    /// <param name="maximumEvents">How many events to add.</param>
    /// <param name="temperature">How much to flatten the model's answer.</param>
    /// <param name="topP">Keep the most likely tokens up to this much probability.</param>
    /// <param name="topK">Keep at most this many of them.</param>
    /// <param name="seed">What the stream of random numbers starts from.</param>
    /// <param name="disableProgramChange">Whether the model may change an instrument.</param>
    /// <param name="disableControlChange">Whether the model may move a controller.</param>
    /// <param name="allowedChannels">Which of the sixteen channels the model may write to.</param>
    /// <param name="includePromptEvents">Whether the events of the prompt are handed to the caller as well.</param>
    internal SkyTntGenerationPlan(
        List<int[]> promptRows,
        int maximumEvents,
        double temperature,
        double topP,
        int topK,
        long seed,
        bool disableProgramChange,
        bool disableControlChange,
        bool[] allowedChannels,
        bool includePromptEvents)
    {
        PromptRows = promptRows;
        MaximumEvents = maximumEvents;
        Temperature = temperature;
        TopP = topP;
        TopK = topK;
        Seed = seed;
        DisableProgramChange = disableProgramChange;
        DisableControlChange = disableControlChange;
        AllowedChannels = allowedChannels;
        IncludePromptEvents = includePromptEvents;
    }

    /// <summary>The rows of tokens the piece starts from.</summary>
    internal List<int[]> PromptRows { get; }

    /// <summary>How many events to add.</summary>
    internal int MaximumEvents { get; }

    /// <summary>How much to flatten the model's answer.</summary>
    internal double Temperature { get; }

    /// <summary>Keep the most likely tokens up to this much probability.</summary>
    internal double TopP { get; }

    /// <summary>Keep at most this many of them.</summary>
    internal int TopK { get; }

    /// <summary>What the stream of random numbers starts from.</summary>
    internal long Seed { get; }

    /// <summary>Whether the model is refused the token that begins an instrument change.</summary>
    internal bool DisableProgramChange { get; }

    /// <summary>Whether the model is refused the token that begins a controller change.</summary>
    internal bool DisableControlChange { get; }

    /// <summary>Which of the sixteen channels the model may write to.</summary>
    internal bool[] AllowedChannels { get; }

    /// <summary>Whether the events of the prompt are handed to the caller as well.</summary>
    internal bool IncludePromptEvents { get; }
}
