namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A completed, non-streaming chat reply.
/// </summary>
public sealed class ChatResponse
{
    /// <summary>The assistant message: content, separated thinking and any tool calls.</summary>
    public ChatMessage Message { get; init; }

    /// <summary>Why generation ended.</summary>
    public FinishReason FinishReason { get; init; }

    /// <summary>The statistics of the request.</summary>
    public GenerationStatistics Statistics { get; init; }
}
