using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Timing and token counts for one completed request.
/// </summary>
public sealed class GenerationStatistics
{
    /// <summary>The number of tokens in the prompt.</summary>
    public int PromptTokens { get; init; }

    /// <summary>The number of prompt tokens that were already in the cache and did not need evaluating.</summary>
    public int CachedPromptTokens { get; init; }

    /// <summary>The number of tokens generated.</summary>
    public int GeneratedTokens { get; init; }

    /// <summary>How long prompt evaluation took.</summary>
    public TimeSpan PromptDuration { get; init; }

    /// <summary>How long generation took.</summary>
    public TimeSpan GenerationDuration { get; init; }

    /// <summary>
    /// How long the whole request took. For a chat request this includes rendering the template and
    /// tokenizing; for a raw completion it covers the decode only.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>Tokens generated per second, or 0 when nothing was generated.</summary>
    public double TokensPerSecond =>
        GenerationDuration.TotalSeconds > 0 ? GeneratedTokens / GenerationDuration.TotalSeconds : 0;
}
