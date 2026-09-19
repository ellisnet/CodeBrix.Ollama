using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Thrown when a model file, a projector or an adapter cannot be loaded: the file is missing, is not a GGUF
/// file, uses an architecture the engine does not know, or does not fit in memory. A quantization that the
/// engine refuses fails the same way and for the same kinds of reason, so it carries this exception too.
/// </summary>
public class ModelLoadException : ModelRunnerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelLoadException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ModelLoadException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelLoadException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public ModelLoadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
