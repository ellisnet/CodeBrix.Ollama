using System;

namespace ModelQueryTool.ModelRunning;

/// <summary>
/// The one exception type this library raises: a member called in the wrong state, a value a command cannot
/// use, a load that failed, or a reload that had to fall back. Every exception the model runner raises is
/// translated into this one, with the original kept as the inner exception, so nothing above has to know the
/// runner's own hierarchy. A cancelled operation still surfaces as <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class ModelRunningException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message, written for the person using the application.</param>
    /// <param name="innerException">The exception being translated, when there is one.</param>
    public ModelRunningException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}
