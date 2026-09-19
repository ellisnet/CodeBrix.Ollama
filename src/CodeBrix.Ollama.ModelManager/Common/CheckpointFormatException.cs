using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when a checkpoint container is not the format it claims to be, or declares something the reader
/// refuses to believe - an unknown element type, an offset outside the file, or a header larger than the cap.
/// </summary>
public class CheckpointFormatException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CheckpointFormatException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CheckpointFormatException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckpointFormatException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public CheckpointFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
