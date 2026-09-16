using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Thrown when a chat template cannot be parsed or rendered, in either dialect, or when no template is
/// available for a chat request at all.
/// </summary>
public class ChatTemplateException : ModelRunnerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatTemplateException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ChatTemplateException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatTemplateException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public ChatTemplateException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
