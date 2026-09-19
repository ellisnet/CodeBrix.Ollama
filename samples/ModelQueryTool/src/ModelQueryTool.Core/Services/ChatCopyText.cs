namespace ModelQueryTool.Services;

/// <summary>
/// A piece of the conversation ready to be put on the clipboard, and the little the chat has to
/// say about it afterwards: how much of it there is, and whether an answer in it was cut short.
/// </summary>
internal sealed class ChatCopyText
{
    /// <summary>What a copy of an empty conversation comes to.</summary>
    internal static readonly ChatCopyText Nothing = new(string.Empty, 0, true);

    /// <summary>Records what is to be copied.</summary>
    /// <param name="text">The text itself, exactly as it is to reach the clipboard.</param>
    /// <param name="turnCount">How many turns it covers.</param>
    /// <param name="isComplete">Whether every answer in it ran to its end.</param>
    internal ChatCopyText(string text, int turnCount, bool isComplete)
    {
        Text = text ?? string.Empty;
        TurnCount = turnCount;
        IsComplete = isComplete;
    }

    /// <summary>Gets the text itself, exactly as it is to reach the clipboard.</summary>
    internal string Text { get; }

    /// <summary>Gets how many turns the text covers.</summary>
    internal int TurnCount { get; }

    /// <summary>Gets whether every answer in the text ran to its end.</summary>
    internal bool IsComplete { get; }

    /// <summary>Gets whether there is nothing here to copy.</summary>
    internal bool IsEmpty
    {
        get { return Text.Length == 0; }
    }
}
