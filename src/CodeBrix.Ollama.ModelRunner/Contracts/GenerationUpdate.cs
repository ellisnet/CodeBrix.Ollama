using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One step of a streaming completion: the text produced since the previous update, and on the last update
/// the reason generation ended and its statistics.
/// </summary>
public sealed class GenerationUpdate
{
    /// <summary>The text decoded since the previous update. May be empty on the final update.</summary>
    public string Text { get; init; } = "";

    /// <summary>The token ids decoded since the previous update, in order.</summary>
    public int[] Tokens { get; init; } = Array.Empty<int>();

    /// <summary>True on the last update of the stream.</summary>
    public bool IsFinal { get; init; }

    /// <summary>Why generation ended; <see cref="FinishReason.None"/> until the final update.</summary>
    public FinishReason FinishReason { get; init; }

    /// <summary>The statistics of the completed request; <see langword="null"/> until the final update.</summary>
    public GenerationStatistics Statistics { get; init; }
}
