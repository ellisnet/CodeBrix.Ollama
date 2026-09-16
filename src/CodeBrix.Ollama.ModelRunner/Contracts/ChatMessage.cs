using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One message in a chat conversation.
/// </summary>
public sealed class ChatMessage
{
    /// <summary>Creates an empty message.</summary>
    public ChatMessage()
    {
    }

    /// <summary>Creates a message with a role and text content.</summary>
    /// <param name="role">Who the message is from.</param>
    /// <param name="content">The text.</param>
    public ChatMessage(ChatRole role, string content)
    {
        Role = role;
        Content = content;
    }

    /// <summary>Who the message is from.</summary>
    public ChatRole Role { get; set; }

    /// <summary>The text of the message. For a tool message, the tool's result.</summary>
    public string Content { get; set; }

    /// <summary>
    /// The model's reasoning, for an assistant message from a model that thinks before answering. Separated
    /// from <see cref="Content"/> by the library; <see langword="null"/> when there was none.
    /// </summary>
    public string Thinking { get; set; }

    /// <summary>The tool calls an assistant message made. Empty when it made none.</summary>
    public IList<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();

    /// <summary>For a tool message, the <see cref="ToolCall.Id"/> this is the result of. May be <see langword="null"/>.</summary>
    public string ToolCallId { get; set; }

    /// <summary>For a tool message, the name of the tool that produced the result. May be <see langword="null"/>.</summary>
    public string ToolName { get; set; }
}
