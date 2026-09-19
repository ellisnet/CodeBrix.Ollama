namespace ModelQueryTool.ModelRunning;

/// <summary>What one piece of a streamed turn is, so the application can decide how to show it.</summary>
public enum ChatTurnUpdateKind
{
    /// <summary>Something the host did that the user should know about, such as history dropped to make room.</summary>
    Notice = 0,

    /// <summary>A piece of the model's reasoning, as the model wrote it.</summary>
    Thinking = 1,

    /// <summary>A piece of the model's answer, as the model wrote it.</summary>
    Content = 2,

    /// <summary>The last piece of the turn, carrying why it ended and what it cost.</summary>
    Completed = 3,
}
