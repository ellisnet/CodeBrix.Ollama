using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One manifest as it exists on disk: the parsed document together with the name it is filed under,
/// the exact bytes it was read from and the digest of those bytes. Ollama keeps the same four pieces
/// on its <c>Manifest</c> value, and the raw bytes matter because a manifest is pushed and compared
/// byte for byte, never re-serialized.
/// </summary>
internal sealed class StoredManifest
{
    /// <summary>
    /// Initializes a stored manifest.
    /// </summary>
    /// <param name="name">The fully qualified name the manifest is filed under.</param>
    /// <param name="path">The absolute path of the manifest file.</param>
    /// <param name="manifest">The parsed manifest document.</param>
    /// <param name="digest">The sha256 of the file bytes, lowercase hexadecimal with no prefix.</param>
    /// <param name="modifiedAt">The last write time of the file, in UTC.</param>
    /// <param name="rawBytes">The exact bytes the file holds.</param>
    public StoredManifest(
        ModelName name,
        string path,
        ModelManifest manifest,
        string digest,
        DateTimeOffset modifiedAt,
        byte[] rawBytes)
    {
        Name = name;
        Path = path;
        Manifest = manifest;
        Digest = digest;
        ModifiedAt = modifiedAt;
        RawBytes = rawBytes;
    }

    /// <summary>
    /// The fully qualified name the manifest is filed under.
    /// </summary>
    public ModelName Name { get; }

    /// <summary>
    /// The absolute path of the manifest file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The parsed manifest document.
    /// </summary>
    public ModelManifest Manifest { get; }

    /// <summary>
    /// The sha256 of the file bytes, lowercase hexadecimal with no <c>sha256:</c> prefix. This is what
    /// Ollama's <c>Manifest.Digest()</c> returns and what the registry compares a push against.
    /// </summary>
    public string Digest { get; }

    /// <summary>
    /// The last write time of the manifest file, in UTC. Ollama reports this as a model's modified time.
    /// </summary>
    public DateTimeOffset ModifiedAt { get; }

    /// <summary>
    /// The exact bytes the manifest file holds.
    /// </summary>
    public byte[] RawBytes { get; }
}
