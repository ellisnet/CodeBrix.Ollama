namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when Modelfile text cannot be parsed.
/// </summary>
public class ModelfileParseException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelfileParseException"/> class.
    /// </summary>
    /// <param name="lineNumber">The 1-based line the error was detected on, or 0 when it applies to the whole file.</param>
    /// <param name="message">The message that describes the error.</param>
    public ModelfileParseException(int lineNumber, string message)
        : base(lineNumber > 0 ? $"(line {lineNumber}): {message}" : message)
    {
        LineNumber = lineNumber;
    }

    /// <summary>
    /// The 1-based line the error was detected on, or 0 when it applies to the whole file.
    /// </summary>
    public int LineNumber { get; }
}
