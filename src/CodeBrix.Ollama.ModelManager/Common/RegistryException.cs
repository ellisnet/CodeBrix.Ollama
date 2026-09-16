using System;
using System.Net;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when a registry request fails: an HTTP error status, a transport failure that survived every
/// retry, or a response the client cannot interpret.
/// </summary>
public class RegistryException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="statusCode">The HTTP status code the registry answered with, or <see langword="null"/> when no response arrived.</param>
    /// <param name="responseBody">The response body, when one was read, otherwise <see langword="null"/>.</param>
    public RegistryException(string message, HttpStatusCode? statusCode, string responseBody) : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public RegistryException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// The HTTP status code the registry answered with, or <see langword="null"/> when no response arrived.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// The response body, when one was read, otherwise <see langword="null"/>.
    /// </summary>
    public string ResponseBody { get; }
}
