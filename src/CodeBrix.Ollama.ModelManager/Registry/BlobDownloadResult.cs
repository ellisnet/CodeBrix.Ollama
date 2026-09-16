namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a finished download hands back: what the bytes hash to, how many of them there are and where
/// they now live. A bundle pull needs all three, because a file whose source stated no hash is named in
/// the store by the digest computed here.
/// </summary>
internal sealed class BlobDownloadResult
{
    /// <summary>
    /// Initializes a result.
    /// </summary>
    /// <param name="digest">The SHA-256 of the bytes in <c>sha256:&lt;hex&gt;</c> form.</param>
    /// <param name="md5">
    /// The MD5 of the bytes as lower-case hexadecimal, or <see langword="null"/> when none was computed.
    /// </param>
    /// <param name="size">The size of the file in bytes.</param>
    /// <param name="filePath">Where the finished file is.</param>
    /// <param name="existed">
    /// Whether the file was already in the store, in which case nothing was fetched.
    /// </param>
    public BlobDownloadResult(string digest, string md5, long size, string filePath, bool existed)
    {
        Digest = digest;
        Md5 = md5;
        Size = size;
        FilePath = filePath;
        Existed = existed;
    }

    /// <summary>
    /// The SHA-256 of the bytes in <c>sha256:&lt;hex&gt;</c> form. It is computed for every download,
    /// whatever the source stated, except when the file was already in the store, where it is the
    /// digest the caller named the file by.
    /// </summary>
    public string Digest { get; }

    /// <summary>
    /// The MD5 of the bytes as lower-case hexadecimal when the source stated one to check against,
    /// otherwise <see langword="null"/>.
    /// </summary>
    public string Md5 { get; }

    /// <summary>The size of the file in bytes.</summary>
    public long Size { get; }

    /// <summary>Where the finished file is.</summary>
    public string FilePath { get; }

    /// <summary>Whether the file was already in the store and nothing was fetched.</summary>
    public bool Existed { get; }
}
