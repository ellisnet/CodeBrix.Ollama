namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A request from the model to call one of the tools offered in the chat request.
/// </summary>
public sealed class ToolCall
{
    /// <summary>
    /// An identifier for this call, when the model or its template produced one. Echo it back in the
    /// <see cref="ChatMessage.ToolCallId"/> of the tool result. May be <see langword="null"/>.
    /// </summary>
    public string Id { get; init; }

    /// <summary>The name of the tool to call.</summary>
    public string Name { get; init; }

    /// <summary>The arguments as a JSON object text, exactly as the model wrote them.</summary>
    public string ArgumentsJson { get; init; }
}
