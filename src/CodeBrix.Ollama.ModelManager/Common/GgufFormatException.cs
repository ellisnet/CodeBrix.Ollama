using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when a file is not a GGUF file, or is a GGUF file whose header this reader cannot interpret.
/// </summary>
public class GgufFormatException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GgufFormatException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public GgufFormatException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GgufFormatException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public GgufFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
