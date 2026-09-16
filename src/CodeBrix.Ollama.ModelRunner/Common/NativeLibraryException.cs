using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Thrown when the native inference library cannot be found or loaded, or when the one that was loaded is not
/// the build this binding was written against. The message lists every path that was tried.
/// </summary>
public class NativeLibraryException : ModelRunnerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeLibraryException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public NativeLibraryException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeLibraryException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public NativeLibraryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
