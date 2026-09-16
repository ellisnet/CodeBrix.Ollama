namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when a downloaded or imported blob does not hash to the digest the manifest promised.
/// The offending file has already been removed from the store when this is raised.
/// </summary>
public class DigestMismatchException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DigestMismatchException"/> class.
    /// </summary>
    /// <param name="expectedDigest">The digest the manifest promised, in <c>sha256:&lt;hex&gt;</c> form.</param>
    /// <param name="actualDigest">The digest the bytes on disk hash to, in <c>sha256:&lt;hex&gt;</c> form.</param>
    public DigestMismatchException(string expectedDigest, string actualDigest)
        : base($"Digest mismatch: expected {expectedDigest} but the data hashes to {actualDigest}. The file must be downloaded again.")
    {
        ExpectedDigest = expectedDigest;
        ActualDigest = actualDigest;
    }

    /// <summary>
    /// The digest the manifest promised, in <c>sha256:&lt;hex&gt;</c> form.
    /// </summary>
    public string ExpectedDigest { get; }

    /// <summary>
    /// The digest the bytes on disk hash to, in <c>sha256:&lt;hex&gt;</c> form.
    /// </summary>
    public string ActualDigest { get; }
}
