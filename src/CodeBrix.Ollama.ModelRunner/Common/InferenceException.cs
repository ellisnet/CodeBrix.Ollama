using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Thrown when the engine fails while running a loaded model: a prompt that does not fit the context, a
/// decode call the native library rejected, or an embeddings request to a model that cannot produce them.
/// </summary>
public class InferenceException : ModelRunnerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InferenceException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public InferenceException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InferenceException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public InferenceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
