using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Downloads the files of a bundle into a store's blobs directory. It is the bundle counterpart of the
/// registry blob download: the same ranged parts, sidecar resume, stall watchdog and retries, driven
/// from a <see cref="BundleFile"/> instead of a model name and a digest.
/// </summary>
/// <remarks>
/// <para>
/// A file whose source stated a SHA-256 is written straight to the blob that digest names, so a file
/// already in the store costs no request at all. A file whose source stated no SHA-256 is downloaded to
/// a partial file named after its address, and moved to the blob its content turns out to name once the
/// bytes are all there and hashed. Either way the store ends up content-addressed and a second pull
/// verifies against the first.
/// </para>
/// <para>
/// Every hash the source stated is checked: the SHA-256 and the MD5 by the download itself, and the git
/// blob identifier - the only hash a Hugging Face repository states for a file outside large-file
/// storage - here, once the bytes are all there, whenever no SHA-256 was stated.
/// </para>
/// <para>
/// One downloader may be used for many files, one after another or several at a time; each file gets
/// its own <see cref="BlobDownload"/>.
/// </para>
/// </remarks>
internal sealed class FileDownloader : IDisposable
{
    /// <summary>The default base delay between retries of a failed byte range.</summary>
    private static readonly TimeSpan DefaultRetryBaseDelay = TimeSpan.FromSeconds(1);

    private readonly RegistryClient _client;
    private readonly ModelStoreOptions _options;
    private readonly ModelStorePaths _paths;
    private readonly TimeSpan _retryBaseDelay;
    private readonly bool _ownsClient;
    private bool _disposed;

    /// <summary>
    /// Initializes a downloader with a client of its own.
    /// </summary>
    /// <param name="options">The store options that set the part sizes, the retries and the timeouts.</param>
    /// <param name="paths">The store paths that say where blobs and partial files go.</param>
    /// <exception cref="ArgumentNullException">The options or the paths are <see langword="null"/>.</exception>
    public FileDownloader(ModelStoreOptions options, ModelStorePaths paths)
        : this(options, paths, DefaultRetryBaseDelay)
    {
    }

    /// <summary>
    /// Initializes a downloader with a client of its own and an explicit retry base delay. Tests use
    /// this to keep their retries quick.
    /// </summary>
    /// <param name="options">The store options that set the part sizes, the retries and the timeouts.</param>
    /// <param name="paths">The store paths that say where blobs and partial files go.</param>
    /// <param name="retryBaseDelay">The base delay of the back-off between retries of a failed range.</param>
    /// <exception cref="ArgumentNullException">The options or the paths are <see langword="null"/>.</exception>
    public FileDownloader(ModelStoreOptions options, ModelStorePaths paths, TimeSpan retryBaseDelay)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        _options = options;
        _paths = paths;
        _retryBaseDelay = retryBaseDelay > TimeSpan.Zero ? retryBaseDelay : DefaultRetryBaseDelay;
        _client = new RegistryClient(options, _retryBaseDelay);
        _ownsClient = true;
    }

    /// <summary>
    /// Initializes a downloader over a client somebody else owns, which is how a store that already has
    /// one avoids opening a second set of connections.
    /// </summary>
    /// <param name="client">The client the requests go through. It is not disposed with this instance.</param>
    /// <param name="options">The store options that set the part sizes, the retries and the timeouts.</param>
    /// <param name="paths">The store paths that say where blobs and partial files go.</param>
    /// <exception cref="ArgumentNullException">The client, the options or the paths are <see langword="null"/>.</exception>
    public FileDownloader(RegistryClient client, ModelStoreOptions options, ModelStorePaths paths)
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        _client = client;
        _options = options;
        _paths = paths;
        _retryBaseDelay = DefaultRetryBaseDelay;
        _ownsClient = false;
    }

    /// <summary>
    /// Downloads one bundle file into the store's blobs directory.
    /// </summary>
    /// <param name="file">The file to fetch.</param>
    /// <param name="requireStatedHash">
    /// Whether a file whose source stated no SHA-256, no MD5 and no git blob identifier is refused
    /// instead of downloaded.
    /// This is what <see cref="PullOptions.RequireHashes"/> asks for.
    /// </param>
    /// <param name="progress">
    /// Called with the completed and total byte counts while the download runs, at most once per
    /// <see cref="ModelStoreOptions.ProgressInterval"/> and once more when it finishes.
    /// <see langword="null"/> asks for no reports.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels the download. A cancelled download leaves the partial file and its sidecar
    /// in place so that a later call picks it up.
    /// </param>
    /// <returns>The digest the bytes hash to, their size and the blob they were written to.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    /// <exception cref="DigestMismatchException">The bytes do not match a hash the source stated.</exception>
    /// <exception cref="ModelManagerException">
    /// <paramref name="requireStatedHash"/> is set and the source stated no hash for this file.
    /// </exception>
    /// <exception cref="RegistryException">
    /// The address is plain HTTP and <see cref="ModelStoreOptions.AllowInsecureHttp"/> is not set, or a
    /// redirect on the way is refused; a byte range failed every retry; the server answered an error
    /// status; or the server states a size other than the one the source stated.
    /// </exception>
    public async Task<BlobDownloadResult> DownloadFileAsync(
        BundleFile file,
        bool requireStatedHash,
        Action<long, long> progress,
        CancellationToken cancellationToken = default)
    {
        if (file == null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        if (requireStatedHash && !file.HasVerifiableHash)
        {
            throw new ModelManagerException(
                "The source states no sha256, no md5 and no git blob id for '" + file.Path
                    + "', and hashes were required. Either drop that requirement or use a source that states one.");
        }

        // A plain-HTTP address is refused here, before a partial file or sidecar is made for it.
        _client.EnsureAddressAllowed(file.Url);

        // A stated sha256 names the blob before a single byte arrives, which is what lets a file
        // already in the store cost nothing.
        string destinationPath = file.Sha256 == null ? null : _paths.GetBlobPath("sha256:" + file.Sha256);

        var download = new BlobDownload(
            _client,
            _options,
            file.Url,
            file.Size,
            file.Sha256,
            file.Md5,
            destinationPath,
            _paths.GetPartialDataPath(file.Url),
            _paths.GetPartialStatePath(file.Url),
            _retryBaseDelay);

        BlobDownloadResult result = await download.RunAsync(progress, cancellationToken).ConfigureAwait(false);
        if (destinationPath != null)
        {
            return result;
        }

        if (file.GitSha1 != null)
        {
            await VerifyGitBlobAsync(file, result.FilePath, cancellationToken).ConfigureAwait(false);
        }

        // No sha256 was stated, so the file is named by what it turned out to be.
        string blobPath = _paths.GetBlobPath(result.Digest);
        string blobDirectory = Path.GetDirectoryName(blobPath);
        if (!string.IsNullOrEmpty(blobDirectory))
        {
            Directory.CreateDirectory(blobDirectory);
        }

        File.Move(result.FilePath, blobPath, true);
        return new BlobDownloadResult(result.Digest, result.Md5, result.Size, blobPath, result.Existed);
    }

    /// <summary>
    /// Computes the git blob identifier of a file: the SHA-1 of <c>"blob " + length + "\0"</c> followed
    /// by the content.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The identifier in lower-case hexadecimal.</returns>
    internal static async Task<string> ComputeGitBlobSha1Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

        using IncrementalHash sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        sha1.AppendData(Encoding.ASCII.GetBytes(
            "blob " + stream.Length.ToString(CultureInfo.InvariantCulture) + "\0"));

        byte[] buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            sha1.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(sha1.GetCurrentHash());
    }

    /// <summary>
    /// Holds a finished download to the git blob identifier its source stated, and deletes it when the
    /// two differ, exactly as a SHA-256 mismatch is handled.
    /// </summary>
    /// <param name="file">The file as the source described it.</param>
    /// <param name="downloadedPath">Where the verified-so-far bytes are.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <exception cref="DigestMismatchException">The bytes do not hash to the stated identifier.</exception>
    private static async Task VerifyGitBlobAsync(BundleFile file, string downloadedPath, CancellationToken cancellationToken)
    {
        string actual = await ComputeGitBlobSha1Async(downloadedPath, cancellationToken).ConfigureAwait(false);
        if (string.Equals(actual, file.GitSha1, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            File.Delete(downloadedPath);
        }
        catch (IOException)
        {
            // The mismatch is what matters; a partial file left behind is started over next time,
            // because its sidecar is already gone.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }

        throw new DigestMismatchException("git-sha1:" + file.GitSha1, "git-sha1:" + actual);
    }

    /// <summary>
    /// Disposes the client when this instance created it, and leaves a borrowed one alone.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
