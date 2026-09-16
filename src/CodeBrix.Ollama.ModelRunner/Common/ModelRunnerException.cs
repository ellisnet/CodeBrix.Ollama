using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Base type for every exception thrown by CodeBrix.Ollama.ModelRunner.
/// </summary>
public class ModelRunnerException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelRunnerException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ModelRunnerException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelRunnerException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public ModelRunnerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
