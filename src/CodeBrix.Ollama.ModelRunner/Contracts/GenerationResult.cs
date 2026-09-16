namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A completed, non-streaming completion.
/// </summary>
public sealed class GenerationResult
{
    /// <summary>The generated text.</summary>
    public string Text { get; init; } = "";

    /// <summary>Why generation ended.</summary>
    public FinishReason FinishReason { get; init; }

    /// <summary>The statistics of the request.</summary>
    public GenerationStatistics Statistics { get; init; }
}
