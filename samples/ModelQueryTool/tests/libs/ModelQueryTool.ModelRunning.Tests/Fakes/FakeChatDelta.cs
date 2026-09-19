namespace ModelQueryTool.ModelRunning.Tests.Fakes;

/// <summary>One piece of a scripted reply: reasoning text, answer text, or both at once.</summary>
public sealed class FakeChatDelta
{
    /// <summary>Gets the reasoning this piece carries.</summary>
    public string ThinkingDelta { get; init; } = "";

    /// <summary>Gets the answer text this piece carries.</summary>
    public string ContentDelta { get; init; } = "";

    /// <summary>Makes a piece that carries only reasoning.</summary>
    /// <param name="text">The reasoning.</param>
    /// <returns>The piece.</returns>
    public static FakeChatDelta Thinking(string text)
    {
        return new FakeChatDelta { ThinkingDelta = text };
    }

    /// <summary>Makes a piece that carries only answer text.</summary>
    /// <param name="text">The answer text.</param>
    /// <returns>The piece.</returns>
    public static FakeChatDelta Content(string text)
    {
        return new FakeChatDelta { ContentDelta = text };
    }
}
