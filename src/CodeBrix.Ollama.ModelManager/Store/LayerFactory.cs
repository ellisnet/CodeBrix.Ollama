using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama manifest/layer.go;

/// <summary>
/// Creates the blobs a manifest points at and reads them back. A blob is content addressed: its file
/// name is its own sha256, so writing the same bytes twice writes the file once and every manifest
/// that names that digest shares it. Each time a blob is used its last write time is set to now, which
/// is what keeps <see cref="LayerPruner"/> from collecting a blob that was just written.
/// </summary>
internal static class LayerFactory
{
    /// <summary>
    /// The buffer used while content is streamed into the blobs directory.
    /// </summary>
    private const int BufferSize = 128 * 1024;

    /// <summary>
    /// UTF-8 without a byte order mark, which is what Ollama writes for template, system and
    /// license layers.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    /// <summary>
    /// Streams content into the blobs directory and returns the layer that names it. The bytes are
    /// hashed as they are written to a temporary file; when no blob of that digest exists yet the
    /// temporary file becomes the blob, otherwise it is discarded and the existing blob is reused.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="content">The content to store. It is read to its end.</param>
    /// <param name="mediaType">One of the <see cref="MediaTypes"/> constants.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The layer, carrying the media type, the digest and the size.</returns>
    public static async Task<ModelLayer> CreateFromStreamAsync(
        ModelStorePaths paths,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        await paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);

        string tempPath = Path.Combine(paths.BlobsDirectory, "sha256-" + Path.GetRandomFileName());
        long size = 0;
        string digest;
        try
        {
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (FileStream temp = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous))
                {
                    byte[] buffer = new byte[BufferSize];
                    while (true)
                    {
                        int read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }
                        hash.AppendData(buffer, 0, read);
                        await temp.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        size += read;
                    }

                    await temp.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                digest = Sha256Digest.Prefix + Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            string blobPath = paths.GetBlobPath(digest);
            if (File.Exists(blobPath))
            {
                File.Delete(tempPath);
            }
            else
            {
                File.Move(tempPath, blobPath);
            }

            Touch(blobPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return new ModelLayer(mediaType, digest, size);
    }

    /// <summary>
    /// Stores a block of bytes as a blob.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="bytes">The content.</param>
    /// <param name="mediaType">One of the <see cref="MediaTypes"/> constants.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The layer that names the stored bytes.</returns>
    public static async Task<ModelLayer> CreateFromBytesAsync(
        ModelStorePaths paths,
        byte[] bytes,
        string mediaType,
        CancellationToken cancellationToken)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        await using MemoryStream stream = new MemoryStream(bytes, false);
        return await CreateFromStreamAsync(paths, stream, mediaType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores text as a blob, encoded as UTF-8 with no byte order mark.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="text">The content.</param>
    /// <param name="mediaType">One of the <see cref="MediaTypes"/> constants.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The layer that names the stored text.</returns>
    public static Task<ModelLayer> CreateFromTextAsync(
        ModelStorePaths paths,
        string text,
        string mediaType,
        CancellationToken cancellationToken)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        return CreateFromBytesAsync(paths, Utf8NoBom.GetBytes(text), mediaType, cancellationToken);
    }

    /// <summary>
    /// Imports a file into the store. The file is hashed and, when no blob of that digest exists yet,
    /// copied into the blobs directory. The caller's file is never moved or removed.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="filePath">The file to import.</param>
    /// <param name="mediaType">One of the <see cref="MediaTypes"/> constants.</param>
    /// <param name="from">The name recorded as the source of the layer, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The layer that names the imported file.</returns>
    public static async Task<ModelLayer> CreateFromFileAsync(
        ModelStorePaths paths,
        string filePath,
        string mediaType,
        string from,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }
        if (!File.Exists(filePath))
        {
            throw new ModelManagerException($"file {filePath} does not exist");
        }

        await paths.EnsureDirectoriesAsync(cancellationToken).ConfigureAwait(false);

        string digest = await Sha256Digest.ComputeFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        string blobPath = paths.GetBlobPath(digest);
        long size = new FileInfo(filePath).Length;

        if (!File.Exists(blobPath))
        {
            string tempPath = Path.Combine(paths.BlobsDirectory, "sha256-" + Path.GetRandomFileName());
            try
            {
                File.Copy(filePath, tempPath, false);
                if (File.Exists(blobPath))
                {
                    File.Delete(tempPath);
                }
                else
                {
                    File.Move(tempPath, blobPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        Touch(blobPath);

        return new ModelLayer(mediaType, digest, size)
        {
            From = from
        };
    }

    /// <summary>
    /// Builds a layer that names a blob already in the store, which is how a model created from
    /// another model reuses its weights without copying them.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="digest">The digest of the existing blob.</param>
    /// <param name="mediaType">One of the <see cref="MediaTypes"/> constants.</param>
    /// <param name="from">The name recorded as the source of the layer, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The layer, with the size read from the blob on disk.</returns>
    /// <exception cref="ModelManagerException">The blob is not in the store.</exception>
    public static Task<ModelLayer> CreateFromExistingBlobAsync(
        ModelStorePaths paths,
        string digest,
        string mediaType,
        string from,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }
        if (string.IsNullOrEmpty(digest))
        {
            throw new ArgumentException("A digest is required.", nameof(digest));
        }

        cancellationToken.ThrowIfCancellationRequested();

        string blobPath = paths.GetBlobPath(digest);
        var info = new FileInfo(blobPath);
        if (!info.Exists)
        {
            throw new ModelManagerException(
                $"blob {digest} does not exist",
                new FileNotFoundException("The blob was not found in the store.", blobPath));
        }

        long size = info.Length;
        Touch(blobPath);

        ModelLayer layer = new ModelLayer(mediaType, digest, size)
        {
            From = from
        };
        return Task.FromResult(layer);
    }

    /// <summary>
    /// Reads the bytes of a blob.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="digest">The digest of the blob.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The bytes the blob holds.</returns>
    /// <exception cref="ModelManagerException">The blob is not in the store.</exception>
    public static async Task<byte[]> ReadBlobBytesAsync(
        ModelStorePaths paths,
        string digest,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        string blobPath = paths.GetBlobPath(digest);
        if (!File.Exists(blobPath))
        {
            throw new ModelManagerException($"blob {digest} does not exist");
        }

        return await File.ReadAllBytesAsync(blobPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a blob as UTF-8 text, which is what the template, system, parameters, messages and
    /// license layers hold.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="digest">The digest of the blob.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The text the blob holds.</returns>
    /// <exception cref="ModelManagerException">The blob is not in the store.</exception>
    public static async Task<string> ReadBlobTextAsync(
        ModelStorePaths paths,
        string digest,
        CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadBlobBytesAsync(paths, digest, cancellationToken).ConfigureAwait(false);
        return Utf8NoBom.GetString(bytes);
    }

    /// <summary>
    /// Rehashes a blob and compares the result with the digest it is filed under. A blob that does not
    /// hash to its own name is corrupt, so it is deleted before the failure is reported; the next pull
    /// then downloads it again instead of trusting it.
    /// </summary>
    /// <param name="paths">The store paths.</param>
    /// <param name="digest">The digest of the blob.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns><see langword="true"/> when the blob hashes to its digest.</returns>
    /// <exception cref="ModelManagerException">The blob is not in the store.</exception>
    /// <exception cref="DigestMismatchException">The blob does not hash to its digest; it has been deleted.</exception>
    public static async Task<bool> VerifyBlobAsync(
        ModelStorePaths paths,
        string digest,
        CancellationToken cancellationToken)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        string blobPath = paths.GetBlobPath(digest);
        if (!File.Exists(blobPath))
        {
            throw new ModelManagerException($"blob {digest} does not exist");
        }

        string actual = await Sha256Digest.ComputeFileAsync(blobPath, cancellationToken).ConfigureAwait(false);
        string expected = Sha256Digest.ToDigest(digest).ToLowerInvariant();
        if (string.Equals(actual, expected, StringComparison.Ordinal))
        {
            return true;
        }

        File.Delete(blobPath);
        throw new DigestMismatchException(expected, actual);
    }

    /// <summary>
    /// Sets the last write time of a blob to now, marking it as in use so that pruning leaves it alone.
    /// </summary>
    /// <param name="blobPath">The blob file.</param>
    private static void Touch(string blobPath)
    {
        try
        {
            File.SetLastWriteTimeUtc(blobPath, DateTime.UtcNow);
        }
        catch (IOException)
        {
            // A blob whose time cannot be updated is still usable; the only cost is that pruning may
            // consider it older than it is.
        }
        catch (UnauthorizedAccessException)
        {
            // As above: a read-only blob directory does not make the layer unusable.
        }
    }
}
