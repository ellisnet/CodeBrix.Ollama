using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when a model name string cannot be parsed into a fully qualified <see cref="ModelName"/>.
/// </summary>
public class InvalidModelNameException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidModelNameException"/> class.
    /// </summary>
    /// <param name="modelName">The model name string that was rejected.</param>
    /// <param name="message">The message that describes the error.</param>
    public InvalidModelNameException(string modelName, string message) : base(message)
    {
        ModelName = modelName;
    }

    /// <summary>
    /// The model name string that was rejected.
    /// </summary>
    public string ModelName { get; }
}
