using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama manifest/paths.go;

/// <summary>
/// Every path inside a model store directory, computed the way Ollama computes it, so a directory this
/// library writes is one a local Ollama install can read and the other way round. The layout is
/// <c>&lt;store&gt;/manifests/&lt;host&gt;/&lt;namespace&gt;/&lt;model&gt;/&lt;tag&gt;</c> for manifests and
/// <c>&lt;store&gt;/blobs/sha256-&lt;hex&gt;</c> for content.
/// </summary>
internal sealed class ModelStorePaths
{
    /// <summary>
    /// The suffix of the file that holds the bytes of a download still in progress.
    /// </summary>
    private const string PartialDataSuffix = ".codebrix-partial";

    /// <summary>
    /// The suffix of the file that holds the byte ranges already written for a download in progress.
    /// </summary>
    private const string PartialStateSuffix = ".codebrix-parts.json";

    /// <summary>
    /// Initializes the paths of one store directory.
    /// </summary>
    /// <param name="storeDirectory">The store directory. It need not exist yet.</param>
    public ModelStorePaths(string storeDirectory)
    {
        if (string.IsNullOrWhiteSpace(storeDirectory))
        {
            throw new ArgumentException("A store directory is required.", nameof(storeDirectory));
        }

        StoreDirectory = Path.GetFullPath(storeDirectory);
        ManifestsDirectory = Path.Combine(StoreDirectory, "manifests");
        BlobsDirectory = Path.Combine(StoreDirectory, "blobs");
    }

    /// <summary>
    /// The absolute store directory.
    /// </summary>
    public string StoreDirectory { get; }

    /// <summary>
    /// The absolute manifests directory, <c>&lt;store&gt;/manifests</c>.
    /// </summary>
    public string ManifestsDirectory { get; }

    /// <summary>
    /// The absolute blobs directory, <c>&lt;store&gt;/blobs</c>.
    /// </summary>
    public string BlobsDirectory { get; }

    /// <summary>
    /// The path of the blob a digest names. No directory is created and the file need not exist.
    /// </summary>
    /// <param name="digest">The digest, spelled with either a colon or a hyphen.</param>
    /// <returns>The absolute path of the blob file.</returns>
    /// <exception cref="ArgumentException">The digest is not well formed.</exception>
    public string GetBlobPath(string digest)
    {
        if (!Sha256Digest.IsValid(digest))
        {
            throw new ArgumentException("invalid digest format", nameof(digest));
        }

        return Path.Combine(BlobsDirectory, Sha256Digest.ToFileName(digest));
    }

    /// <summary>
    /// The path of the manifest file of a model name. No directory is created and the file need not exist.
    /// </summary>
    /// <param name="name">The model name, which must be fully qualified.</param>
    /// <returns>The absolute path of the manifest file.</returns>
    /// <exception cref="ArgumentException">The name is not fully qualified.</exception>
    public string GetManifestPath(ModelName name)
    {
        if (!name.IsFullyQualified)
        {
            throw new ArgumentException(
                "The manifest path of a model name that is not fully qualified cannot be built.",
                nameof(name));
        }

        return Path.Combine(ManifestsDirectory, name.ToRelativePath());
    }

    /// <summary>
    /// The path of the file that holds the bytes of a blob still being downloaded. The
    /// <c>.codebrix-</c> segment keeps this name clear of Ollama's own <c>-partial</c> and
    /// <c>-partial-N</c> sidecars, so the two downloaders never write to the same file.
    /// </summary>
    /// <param name="digest">The digest of the blob being downloaded.</param>
    /// <returns>The absolute path of the partial data file.</returns>
    public string GetPartialDataPath(string digest)
    {
        return GetBlobPath(digest) + PartialDataSuffix;
    }

    /// <summary>
    /// The path of the file that records which byte ranges of a partial download have been written,
    /// so an interrupted download can resume. The <c>.codebrix-</c> segment keeps this name clear of
    /// Ollama's own sidecars.
    /// </summary>
    /// <param name="digest">The digest of the blob being downloaded.</param>
    /// <returns>The absolute path of the partial state file.</returns>
    public string GetPartialStatePath(string digest)
    {
        return GetBlobPath(digest) + PartialStateSuffix;
    }

    /// <summary>
    /// The key a download of an address is tracked under while it runs. A file fetched from an address
    /// has no digest until its bytes are all there, so its partial file and sidecar are named after the
    /// address instead: <c>url-</c> and the first 32 characters of the SHA-256 of the address.
    /// </summary>
    /// <param name="url">The address the bytes are fetched from.</param>
    /// <returns>The key, which is a usable file name on every platform.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="url"/> is <see langword="null"/>.</exception>
    public static string GetDownloadKey(Uri url)
    {
        if (url == null)
        {
            throw new ArgumentNullException(nameof(url));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri));
        return "url-" + Convert.ToHexStringLower(hash).Substring(0, 32);
    }

    /// <summary>
    /// The path of the file that holds the bytes of a download from an address still in progress. It
    /// sits in the blobs directory beside the blob it will become, and it carries the same
    /// <c>.codebrix-</c> segment as every other sidecar this library writes, so a prune cleans it up
    /// and a real Ollama install never mistakes it for one of its own.
    /// </summary>
    /// <param name="url">The address the bytes are fetched from.</param>
    /// <returns>The absolute path of the partial data file.</returns>
    public string GetPartialDataPath(Uri url)
    {
        return Path.Combine(BlobsDirectory, GetDownloadKey(url) + PartialDataSuffix);
    }

    /// <summary>
    /// The path of the file that records which byte ranges of a download from an address have been
    /// written, so an interrupted download resumes.
    /// </summary>
    /// <param name="url">The address the bytes are fetched from.</param>
    /// <returns>The absolute path of the partial state file.</returns>
    public string GetPartialStatePath(Uri url)
    {
        return Path.Combine(BlobsDirectory, GetDownloadKey(url) + PartialStateSuffix);
    }

    /// <summary>
    /// Reports whether a blob directory file name is a leftover this library wrote, which is the only
    /// kind of unrecognized file pruning is allowed to delete.
    /// </summary>
    /// <param name="fileName">The file name, without a directory.</param>
    /// <returns><see langword="true"/> when the name carries one of this library's sidecar suffixes.</returns>
    public static bool IsPartialSidecarFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        return fileName.EndsWith(PartialDataSuffix, StringComparison.Ordinal)
            || fileName.EndsWith(PartialStateSuffix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates the manifests and blobs directories when they are missing.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>A task that completes when both directories exist.</returns>
    public Task EnsureDirectoriesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(ManifestsDirectory);
        Directory.CreateDirectory(BlobsDirectory);
        return Task.CompletedTask;
    }
}
