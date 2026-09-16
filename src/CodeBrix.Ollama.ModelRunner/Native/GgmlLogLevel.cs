namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/ggml.h;


/// <summary>
/// The severity the engine attaches to a log line. Mirrors <c>enum ggml_log_level</c>.
/// </summary>
internal enum GgmlLogLevel : int
{
    /// <summary>No severity.</summary>
    None = 0,

    /// <summary>Debug detail.</summary>
    Debug = 1,

    /// <summary>Information.</summary>
    Info = 2,

    /// <summary>A warning.</summary>
    Warn = 3,

    /// <summary>An error.</summary>
    Error = 4,

    /// <summary>A continuation of the previous line, carrying its severity.</summary>
    Cont = 5,
}
