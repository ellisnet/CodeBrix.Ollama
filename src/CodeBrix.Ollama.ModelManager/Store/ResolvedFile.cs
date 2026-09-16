using System.Globalization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One file of a bundle as the store holds it: the path its publisher gives it, the blob its content
/// lives in, and what that content is. A resolved bundle is a list of these, in manifest order, and
/// materializing one writes exactly this list out as a directory tree.
/// </summary>
public sealed class ResolvedFile
{
    /// <summary>
    /// Initializes a resolved file.
    /// </summary>
    /// <param name="name">The publisher's relative path, with forward slashes.</param>
    /// <param name="blobPath">The absolute path of the blob the content lives in.</param>
    /// <param name="size">The size of the content in bytes.</param>
    /// <param name="digest">The SHA-256 of the content in <c>sha256:&lt;hex&gt;</c> form.</param>
    public ResolvedFile(string name, string blobPath, long size, string digest)
    {
        Name = name;
        BlobPath = blobPath;
        Size = size;
        Digest = digest;
    }

    /// <summary>
    /// The publisher's relative path, with forward slashes, for example <c>checkpoints/model.ckpt.index</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>The absolute path of the blob the content lives in.</summary>
    public string BlobPath { get; }

    /// <summary>The size of the content in bytes.</summary>
    public long Size { get; }

    /// <summary>The SHA-256 of the content in <c>sha256:&lt;hex&gt;</c> form.</summary>
    public string Digest { get; }

    /// <summary>
    /// Returns the publisher's path and the size.
    /// </summary>
    /// <returns>A one-line description of the file.</returns>
    public override string ToString()
    {
        return Name + " (" + Size.ToString(CultureInfo.InvariantCulture) + " bytes)";
    }
}
