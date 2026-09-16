using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when a model name does not resolve to a manifest, either in the local store or on the
/// registry it names.
/// </summary>
public class ModelNotFoundException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelNotFoundException"/> class.
    /// </summary>
    /// <param name="modelName">The model name that could not be found, as the caller wrote it.</param>
    /// <param name="message">The message that describes the error.</param>
    public ModelNotFoundException(string modelName, string message) : base(message)
    {
        ModelName = modelName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelNotFoundException"/> class.
    /// </summary>
    /// <param name="modelName">The model name that could not be found, as the caller wrote it.</param>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public ModelNotFoundException(string modelName, string message, Exception innerException) : base(message, innerException)
    {
        ModelName = modelName;
    }

    /// <summary>
    /// The model name that could not be found, as the caller wrote it.
    /// </summary>
    public string ModelName { get; }
}
