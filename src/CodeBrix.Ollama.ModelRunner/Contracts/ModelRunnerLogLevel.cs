namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The severity of a message from the native engine.
/// </summary>
public enum ModelRunnerLogLevel
{
    /// <summary>Debug detail.</summary>
    Debug = 1,

    /// <summary>Information.</summary>
    Info = 2,

    /// <summary>A warning.</summary>
    Warning = 3,

    /// <summary>An error.</summary>
    Error = 4,
}
