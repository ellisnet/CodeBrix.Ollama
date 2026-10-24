namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What is wrong with one blob of a stored model, as <see cref="IModelStore.VerifyAsync"/> found it.
/// </summary>
public enum VerifyProblem
{
    /// <summary>The blob the manifest names is not in the store.</summary>
    Missing,

    /// <summary>The blob does not hash to the digest it is filed under: its content has changed.</summary>
    DigestMismatch,

    /// <summary>
    /// The blob hashes to its digest but is not the size the manifest records, so the manifest itself
    /// does not describe its own content.
    /// </summary>
    SizeMismatch,

    /// <summary>The manifest names a digest that is not <c>sha256:</c> followed by 64 hexadecimal characters.</summary>
    MalformedDigest
}
