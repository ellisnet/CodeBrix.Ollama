using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What one step of the chat pipeline separated out of the model's raw output: the reasoning text, the
/// answer text, and any tool calls that became complete at that step.
/// </summary>
internal readonly struct ChatPipelineOutput
{
    /// <summary>Creates one step's result.</summary>
    /// <param name="thinking">The reasoning text, or <see langword="null"/> for none.</param>
    /// <param name="content">The answer text, or <see langword="null"/> for none.</param>
    /// <param name="toolCalls">The calls completed at this step, or <see langword="null"/> for none.</param>
    public ChatPipelineOutput(string thinking, string content, IReadOnlyList<ToolCall> toolCalls)
    {
        Thinking = thinking ?? string.Empty;
        Content = content ?? string.Empty;
        ToolCalls = toolCalls ?? Array.Empty<ToolCall>();
    }

    /// <summary>The reasoning text produced at this step. Empty when there was none.</summary>
    public string Thinking { get; }

    /// <summary>The answer text produced at this step. Empty when there was none.</summary>
    public string Content { get; }

    /// <summary>The tool calls that became complete at this step. Empty when there were none.</summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; }

    /// <summary>Whether this step produced any text at all, and so is worth sending to the caller.</summary>
    public bool HasText => Thinking.Length > 0 || Content.Length > 0;
}
