using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One step of a streaming chat reply. Reasoning and answer text arrive as separate deltas; tool calls arrive
/// complete, on the final update.
/// </summary>
public sealed class ChatUpdate
{
    /// <summary>Answer text produced since the previous update. Empty when this update carries none.</summary>
    public string ContentDelta { get; init; } = "";

    /// <summary>Reasoning text produced since the previous update. Empty when this update carries none.</summary>
    public string ThinkingDelta { get; init; } = "";

    /// <summary>The tool calls the reply made. Filled on the final update only; empty otherwise.</summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = Array.Empty<ToolCall>();

    /// <summary>True on the last update of the stream.</summary>
    public bool IsFinal { get; init; }

    /// <summary>Why generation ended; <see cref="FinishReason.None"/> until the final update.</summary>
    public FinishReason FinishReason { get; init; }

    /// <summary>The statistics of the completed request; <see langword="null"/> until the final update.</summary>
    public GenerationStatistics Statistics { get; init; }
}
