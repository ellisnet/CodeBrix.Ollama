using CodeBrix.Ollama.ModelRunner;

namespace ModelQueryTool.ModelRunning;

/// <summary>
/// One piece of a streamed turn. The pieces arrive in the order the model wrote them, and the last piece of
/// every turn is a <see cref="ChatTurnUpdateKind.Completed"/> one.
/// </summary>
public sealed class ChatTurnUpdate
{
    /// <summary>Gets what this piece is: a notice, reasoning, answer text, or the end of the turn.</summary>
    public ChatTurnUpdateKind Kind { get; init; }

    /// <summary>Gets the text of this piece, which is empty on a <see cref="ChatTurnUpdateKind.Completed"/> piece.</summary>
    public string Text { get; init; } = "";

    /// <summary>Gets why the turn ended. Only meaningful on a <see cref="ChatTurnUpdateKind.Completed"/> piece.</summary>
    public FinishReason FinishReason { get; init; }

    /// <summary>
    /// Gets what the turn cost - prompt tokens, generated tokens and durations. Only present on a
    /// <see cref="ChatTurnUpdateKind.Completed"/> piece, and null when the model reported nothing.
    /// </summary>
    public GenerationStatistics Statistics { get; init; }
}
