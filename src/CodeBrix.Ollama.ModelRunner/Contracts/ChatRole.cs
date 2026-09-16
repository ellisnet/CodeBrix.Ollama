namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Who a chat message is from.
/// </summary>
public enum ChatRole
{
    /// <summary>Instructions to the model.</summary>
    System = 0,

    /// <summary>The person or application talking to the model.</summary>
    User = 1,

    /// <summary>The model.</summary>
    Assistant = 2,

    /// <summary>The result of a tool the model asked to call.</summary>
    Tool = 3,
}
