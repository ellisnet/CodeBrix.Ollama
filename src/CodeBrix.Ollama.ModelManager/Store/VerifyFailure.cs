using System.Globalization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One blob of a stored model that did not pass <see cref="IModelStore.VerifyAsync"/>: which layer it is,
/// what the manifest promised, and what the store actually holds.
/// </summary>
public sealed class VerifyFailure
{
    /// <summary>
    /// Initializes a failure.
    /// </summary>
    /// <param name="digest">The digest the manifest names, as the manifest spells it.</param>
    /// <param name="mediaType">The media type of the layer.</param>
    /// <param name="layerName">The layer's name - a bundle file's relative path - or <see langword="null"/>.</param>
    /// <param name="problem">What is wrong.</param>
    /// <param name="expectedSize">The size the manifest records.</param>
    /// <param name="actualSize">The size of the blob on disk, or -1 when there is none.</param>
    /// <param name="actualDigest">
    /// The digest the blob hashes to, in <c>sha256:&lt;hex&gt;</c> form, or <see langword="null"/> when
    /// there was nothing to hash.
    /// </param>
    public VerifyFailure(
        string digest,
        string mediaType,
        string layerName,
        VerifyProblem problem,
        long expectedSize,
        long actualSize,
        string actualDigest)
    {
        Digest = digest;
        MediaType = mediaType;
        LayerName = layerName;
        Problem = problem;
        ExpectedSize = expectedSize;
        ActualSize = actualSize;
        ActualDigest = actualDigest;
    }

    /// <summary>The digest the manifest names, as the manifest spells it.</summary>
    public string Digest { get; }

    /// <summary>The media type of the layer, for example <see cref="MediaTypes.Model"/>.</summary>
    public string MediaType { get; }

    /// <summary>
    /// The layer's name - the publisher's relative path for a bundle file - or <see langword="null"/> for a
    /// layer that has none.
    /// </summary>
    public string LayerName { get; }

    /// <summary>What is wrong.</summary>
    public VerifyProblem Problem { get; }

    /// <summary>The size the manifest records.</summary>
    public long ExpectedSize { get; }

    /// <summary>The size of the blob on disk, or -1 when there is none.</summary>
    public long ActualSize { get; }

    /// <summary>
    /// The digest the blob hashes to, in <c>sha256:&lt;hex&gt;</c> form, or <see langword="null"/> when the
    /// blob is missing or the manifest's digest is malformed.
    /// </summary>
    public string ActualDigest { get; }

    /// <summary>
    /// Returns the layer, the digest and the problem.
    /// </summary>
    /// <returns>A one-line description of the failure.</returns>
    public override string ToString()
    {
        string what = LayerName ?? MediaType ?? "layer";
        string text = what + " " + Digest + ": " + Problem.ToString();
        if (Problem == VerifyProblem.DigestMismatch && ActualDigest != null)
        {
            text += " (hashes to " + ActualDigest + ")";
        }
        else if (Problem == VerifyProblem.SizeMismatch)
        {
            text += " (" + ActualSize.ToString(CultureInfo.InvariantCulture) + " bytes, not "
                + ExpectedSize.ToString(CultureInfo.InvariantCulture) + ")";
        }
        return text;
    }
}
