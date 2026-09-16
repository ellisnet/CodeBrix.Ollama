namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One progress report from <see cref="IModelStore.PullAsync"/>. The shape mirrors Ollama's own
/// progress stream: a status line, and for layer downloads the digest with byte counts.
/// </summary>
public sealed class PullProgress
{
    /// <summary>
    /// Initializes a status-only report.
    /// </summary>
    /// <param name="status">A short human-readable status, for example "pulling manifest".</param>
    public PullProgress(string status)
    {
        Status = status;
    }

    /// <summary>
    /// Initializes a layer-download report.
    /// </summary>
    /// <param name="status">A short human-readable status, for example "pulling 3f2a1b9c0d4e".</param>
    /// <param name="digest">The digest of the layer being downloaded, in <c>sha256:&lt;hex&gt;</c> form.</param>
    /// <param name="totalBytes">The layer size in bytes.</param>
    /// <param name="completedBytes">The bytes downloaded so far, including bytes resumed from an earlier attempt.</param>
    public PullProgress(string status, string digest, long totalBytes, long completedBytes)
    {
        Status = status;
        Digest = digest;
        TotalBytes = totalBytes;
        CompletedBytes = completedBytes;
    }

    /// <summary>A short human-readable status.</summary>
    public string Status { get; }

    /// <summary>The digest of the layer this report is about, or <see langword="null"/> for a status-only report.</summary>
    public string Digest { get; }

    /// <summary>The layer size in bytes, or 0 for a status-only report.</summary>
    public long TotalBytes { get; }

    /// <summary>The bytes downloaded so far, or 0 for a status-only report.</summary>
    public long CompletedBytes { get; }

    /// <summary>
    /// The completed fraction of this layer in the range 0 to 100, or 0 when the total is unknown.
    /// </summary>
    public double Percent
    {
        get
        {
            return TotalBytes > 0 ? 100.0 * CompletedBytes / TotalBytes : 0;
        }
    }

    /// <summary>
    /// Returns the status, and for a layer report the byte counts.
    /// </summary>
    /// <returns>A one-line description of the report.</returns>
    public override string ToString()
    {
        return Digest == null ? Status : $"{Status} {CompletedBytes}/{TotalBytes}";
    }
}
