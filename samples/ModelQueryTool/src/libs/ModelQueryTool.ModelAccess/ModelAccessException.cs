using System;

namespace ModelQueryTool.ModelAccess;

/// <summary>
/// The one exception this library raises for a failure of its own or of the store underneath it. Catch
/// this and nothing else.
/// </summary>
/// <remarks>
/// Cancellation and use after disposal are NOT wrapped: an
/// <see cref="OperationCanceledException"/> and an <see cref="ObjectDisposedException"/> travel out
/// exactly as they were raised, because an application handles those two by what they are rather than
/// by what they happened during.
/// </remarks>
public sealed class ModelAccessException : Exception
{
    /// <summary>
    /// Raises a failure with a message.
    /// </summary>
    /// <param name="message">What went wrong, in a sentence fit to show a person.</param>
    /// <param name="failure">Which kind of failure it is.</param>
    public ModelAccessException(string message, ModelAccessFailure failure)
        : this(message, failure, null, null)
    {
    }

    /// <summary>
    /// Raises a failure with a message and the exception that caused it.
    /// </summary>
    /// <param name="message">What went wrong, in a sentence fit to show a person.</param>
    /// <param name="failure">Which kind of failure it is.</param>
    /// <param name="innerException">The exception this one was translated from.</param>
    public ModelAccessException(string message, ModelAccessFailure failure, Exception innerException)
        : this(message, failure, null, innerException)
    {
    }

    /// <summary>
    /// Raises a failure with a message, an HTTP status code and the exception that caused it.
    /// </summary>
    /// <param name="message">What went wrong, in a sentence fit to show a person.</param>
    /// <param name="failure">Which kind of failure it is.</param>
    /// <param name="statusCode">
    /// The HTTP status code the registry answered with, or <see langword="null"/> when no answer arrived
    /// or the failure had nothing to do with a registry.
    /// </param>
    /// <param name="innerException">The exception this one was translated from.</param>
    public ModelAccessException(
        string message,
        ModelAccessFailure failure,
        int? statusCode,
        Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
        StatusCode = statusCode;
    }

    /// <summary>Gets which kind of failure this is.</summary>
    public ModelAccessFailure Failure { get; }

    /// <summary>
    /// Gets the HTTP status code the registry answered with, or <see langword="null"/> when no answer
    /// arrived or the failure had nothing to do with a registry.
    /// </summary>
    public int? StatusCode { get; }
}
