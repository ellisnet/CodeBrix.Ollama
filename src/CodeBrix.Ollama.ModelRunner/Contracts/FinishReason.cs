namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Why a generation ended.
/// </summary>
public enum FinishReason
{
    /// <summary>It has not ended; this is an intermediate update.</summary>
    None = 0,

    /// <summary>The model produced an end-of-generation token.</summary>
    Stop = 1,

    /// <summary>The output reached one of the stop sequences.</summary>
    StopSequence = 2,

    /// <summary>The request's token limit was reached.</summary>
    Length = 3,

    /// <summary>The context window filled up.</summary>
    ContextFull = 4,

    /// <summary>The model produced a complete tool call and stopped.</summary>
    ToolCall = 5,
}
