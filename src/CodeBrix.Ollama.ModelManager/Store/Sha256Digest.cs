using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The sha256 digest helpers the store is built on. A digest is written <c>sha256:&lt;64 lowercase hex&gt;</c>
/// inside a manifest and <c>sha256-&lt;64 hex&gt;</c> as a file name in the blobs directory, because a colon
/// is not a usable file name character on every platform. Both spellings are accepted everywhere a digest
/// is validated, which is what Ollama's <c>ValidateDigest</c> does.
/// </summary>
internal static class Sha256Digest
{
    /// <summary>
    /// The prefix a digest carries inside a manifest, including the colon.
    /// </summary>
    public const string Prefix = "sha256:";

    /// <summary>
    /// The buffer size used when a digest is computed by streaming.
    /// </summary>
    private const int BufferSize = 128 * 1024;

    /// <summary>
    /// Matches both digest spellings, exactly as Ollama's <c>digestPattern</c> in manifest/paths.go does.
    /// </summary>
    private static readonly Regex DigestPattern = new Regex(
        "^sha256[:-][0-9a-fA-F]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Computes the digest of everything that can still be read from <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream to read to its end.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The digest in <c>sha256:&lt;64 lowercase hex&gt;</c> form.</returns>
    public static async Task<string> ComputeAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Prefix + Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Computes the digest of the contents of a file.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The digest in <c>sha256:&lt;64 lowercase hex&gt;</c> form.</returns>
    public static async Task<string> ComputeFileAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        await using FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ComputeAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the digest of a block of bytes already in memory.
    /// </summary>
    /// <param name="bytes">The bytes to hash.</param>
    /// <returns>The digest in <c>sha256:&lt;64 lowercase hex&gt;</c> form.</returns>
    public static string Compute(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, hash);
        return Prefix + Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Reports whether a string names a blob: <c>sha256</c>, then a colon or a hyphen, then exactly
    /// 64 hexadecimal characters of either case.
    /// </summary>
    /// <param name="digest">The candidate digest, in either spelling.</param>
    /// <returns><see langword="true"/> when the string is a well formed digest.</returns>
    public static bool IsValid(string digest)
    {
        return !string.IsNullOrEmpty(digest) && DigestPattern.IsMatch(digest);
    }

    /// <summary>
    /// Converts a digest to the file name a blob carries on disk, turning the colon into a hyphen.
    /// A digest already spelled with a hyphen is returned unchanged.
    /// </summary>
    /// <param name="digest">The digest, in either spelling.</param>
    /// <returns>The blob file name.</returns>
    public static string ToFileName(string digest)
    {
        if (digest == null)
        {
            throw new ArgumentNullException(nameof(digest));
        }
        return digest.Replace(':', '-');
    }

    /// <summary>
    /// Converts a blob file name to the digest a manifest carries, turning the hyphen into a colon.
    /// A digest already spelled with a colon is returned unchanged.
    /// </summary>
    /// <param name="fileName">The blob file name, in either spelling.</param>
    /// <returns>The digest.</returns>
    public static string ToDigest(string fileName)
    {
        if (fileName == null)
        {
            throw new ArgumentNullException(nameof(fileName));
        }
        return fileName.Replace('-', ':');
    }

    /// <summary>
    /// The first 12 hexadecimal characters of a digest, which is the short form Ollama prints on its
    /// progress lines. A string too short to hold a digest is returned unchanged.
    /// </summary>
    /// <param name="digest">The digest, in either spelling.</param>
    /// <returns>The 12 character short form.</returns>
    public static string Short(string digest)
    {
        if (string.IsNullOrEmpty(digest))
        {
            return digest;
        }
        if (digest.Length < Prefix.Length + 12)
        {
            return digest;
        }
        return digest.Substring(Prefix.Length, 12);
    }
}
