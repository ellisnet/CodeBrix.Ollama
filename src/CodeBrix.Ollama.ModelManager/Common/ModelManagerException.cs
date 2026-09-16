using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Base type for every exception thrown by CodeBrix.Ollama.ModelManager.
/// </summary>
public class ModelManagerException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelManagerException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ModelManagerException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelManagerException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public ModelManagerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
