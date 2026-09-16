using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Thrown when a GBNF grammar cannot be parsed, or when a JSON schema cannot be converted into one.
/// </summary>
public class GrammarException : ModelRunnerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GrammarException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public GrammarException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GrammarException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public GrammarException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
