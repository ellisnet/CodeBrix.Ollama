namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Which side of the process owns the embedded CPython interpreter. There is exactly one interpreter per
/// process and it cannot be restarted, so ownership decides who configures it and who ends it.
/// </summary>
public enum PythonEngineOwner
{
    /// <summary>
    /// No interpreter has been started yet, so nobody owns one.
    /// </summary>
    None = 0,

    /// <summary>
    /// This library started the interpreter. It applied <see cref="PythonOptions"/>, it set the
    /// process-exit safety net, and <see cref="PythonSupport.Shutdown"/> is what ends it.
    /// </summary>
    ModelManager = 1,

    /// <summary>
    /// The host application had already started an interpreter when this library first looked. Nothing
    /// here configures it and nothing here shuts it down; the host's own lifetime rules apply.
    /// </summary>
    Host = 2
}
