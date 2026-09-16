using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One file of a bundle: the relative path the publisher gives it, the address it is fetched from, and
/// whatever the publisher states about its size and its hashes. A bundle is any set of files that is
/// not a GGUF model on an Ollama-protocol registry - a Hugging Face file repository, a list of HTTPS
/// addresses, the objects under a storage-bucket prefix - and this is the one shape all of them are
/// described in.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Path"/> is always relative and always spelled with forward slashes, because it is the
/// path the publisher uses and the path a later <c>Materialize</c> call lays out on disk. A backslash
/// in the supplied path is read as a separator and rewritten, a leading <c>./</c> is dropped, and a
/// path that is rooted or that walks up with <c>..</c> is refused: a bundle must never be able to
/// write outside the directory it is materialized into.
/// </para>
/// <para>
/// Sizes and hashes are what the SOURCE stated, not what was measured. <see cref="Size"/> is
/// <see cref="UnknownSize"/> when the source did not say, and each hash is <see langword="null"/> when
/// the source did not state that hash. The store computes the SHA-256 of every file it downloads in
/// any case, so a file that arrives with nothing stated is still named and recorded by its content.
/// </para>
/// </remarks>
public sealed class BundleFile
{
    /// <summary>
    /// The value <see cref="Size"/> carries when the source did not state a size. It is negative, not
    /// zero, because zero is a valid size for an empty file.
    /// </summary>
    public const long UnknownSize = -1L;

    /// <summary>
    /// Initializes a file whose size and hashes the source did not state.
    /// </summary>
    /// <param name="path">The publisher's relative path, with forward slashes.</param>
    /// <param name="url">The absolute http or https address the bytes are fetched from.</param>
    /// <exception cref="ArgumentException">The path or the address is missing or not usable.</exception>
    public BundleFile(string path, string url)
        : this(path, url, UnknownSize, null, null, null)
    {
    }

    /// <summary>
    /// Initializes a file whose size the source stated.
    /// </summary>
    /// <param name="path">The publisher's relative path, with forward slashes.</param>
    /// <param name="url">The absolute http or https address the bytes are fetched from.</param>
    /// <param name="size">The size in bytes, or <see cref="UnknownSize"/> when it is not known.</param>
    /// <exception cref="ArgumentException">The path or the address is missing or not usable.</exception>
    public BundleFile(string path, string url, long size)
        : this(path, url, size, null, null, null)
    {
    }

    /// <summary>
    /// Initializes a file with everything the source stated about it.
    /// </summary>
    /// <param name="path">The publisher's relative path, with forward slashes.</param>
    /// <param name="url">The absolute http or https address the bytes are fetched from.</param>
    /// <param name="size">The size in bytes, or <see cref="UnknownSize"/> when it is not known.</param>
    /// <param name="sha256">
    /// The SHA-256 of the content as 64 hexadecimal characters, or <see langword="null"/> when the
    /// source did not state one. The case is normalized to lower case.
    /// </param>
    /// <param name="md5">
    /// The MD5 of the content as 32 hexadecimal characters, or <see langword="null"/> when the source
    /// did not state one. The case is normalized to lower case.
    /// </param>
    /// <param name="gitSha1">
    /// The git blob identifier as 40 hexadecimal characters, or <see langword="null"/>. It identifies
    /// the object in the publisher's repository and is not a hash of the content as it arrives, so it
    /// is recorded and never verified against.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The path is missing, rooted or walks up with <c>..</c>; the address is missing, relative or
    /// neither http nor https; or a hash is not hexadecimal of the length its algorithm calls for.
    /// </exception>
    public BundleFile(string path, string url, long size, string sha256, string md5, string gitSha1)
    {
        Path = NormalizePath(path);
        Url = ParseUrl(url);
        Size = size < 0 ? UnknownSize : size;
        Sha256 = NormalizeHash(sha256, 64, "sha256", nameof(sha256));
        Md5 = NormalizeHash(md5, 32, "md5", nameof(md5));
        GitSha1 = NormalizeHash(gitSha1, 40, "git sha1", nameof(gitSha1));
    }

    /// <summary>
    /// The publisher's path, relative to the root of the bundle and spelled with forward slashes, for
    /// example <c>logs/version_0/events.out.tfevents.1727438892</c>.
    /// </summary>
    public string Path { get; }

    /// <summary>The absolute address the bytes are fetched from.</summary>
    public Uri Url { get; }

    /// <summary>
    /// The size in bytes as the source stated it, or <see cref="UnknownSize"/> when it did not.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// The SHA-256 of the content in lower-case hexadecimal, or <see langword="null"/> when the source
    /// stated none. A download verifies against this when it is there.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    /// The MD5 of the content in lower-case hexadecimal, or <see langword="null"/> when the source
    /// stated none. A download verifies against this when it is the only hash the source stated.
    /// </summary>
    public string Md5 { get; }

    /// <summary>
    /// The git blob identifier in lower-case hexadecimal, or <see langword="null"/>. It is recorded for
    /// provenance and never verified: it hashes the object as git stores it, not the bytes that arrive.
    /// </summary>
    public string GitSha1 { get; }

    /// <summary>The last segment of <see cref="Path"/>, which is the file's own name.</summary>
    public string FileName
    {
        get
        {
            int slash = Path.LastIndexOf('/');
            return slash < 0 ? Path : Path.Substring(slash + 1);
        }
    }

    /// <summary>Whether the source stated a size.</summary>
    public bool HasSize
    {
        get { return Size >= 0; }
    }

    /// <summary>Whether the source stated at least one hash a download can verify against.</summary>
    public bool HasVerifiableHash
    {
        get { return Sha256 != null || Md5 != null; }
    }

    /// <summary>
    /// Returns a copy of this file with the size replaced, which is how a source fills in a size it had
    /// to ask the server for.
    /// </summary>
    /// <param name="size">The size in bytes, or <see cref="UnknownSize"/>.</param>
    /// <returns>The copy.</returns>
    public BundleFile WithSize(long size)
    {
        return new BundleFile(Path, Url.AbsoluteUri, size, Sha256, Md5, GitSha1);
    }

    /// <summary>
    /// Returns a copy of this file with the MD5 replaced, which is how a source fills in the hash a
    /// storage bucket reports in a header rather than in its listing.
    /// </summary>
    /// <param name="md5">The MD5 as 32 hexadecimal characters, or <see langword="null"/>.</param>
    /// <returns>The copy.</returns>
    /// <exception cref="ArgumentException">The hash is not 32 hexadecimal characters.</exception>
    public BundleFile WithMd5(string md5)
    {
        return new BundleFile(Path, Url.AbsoluteUri, Size, Sha256, md5, GitSha1);
    }

    /// <summary>
    /// Returns the path and, when it is known, the size.
    /// </summary>
    /// <returns>A one-line description of the file.</returns>
    public override string ToString()
    {
        return HasSize
            ? Path + " (" + Size.ToString(CultureInfo.InvariantCulture) + " bytes)"
            : Path;
    }

    /// <summary>
    /// Checks and normalizes a publisher's relative path.
    /// </summary>
    /// <param name="path">The path as it was supplied.</param>
    /// <returns>The path with forward slashes and no leading <c>./</c>.</returns>
    /// <exception cref="ArgumentException">The path is missing, rooted or walks up with <c>..</c>.</exception>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A bundle file needs a relative path.", nameof(path));
        }

        string normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(2);
        }

        if (normalized.Length == 0)
        {
            throw new ArgumentException("A bundle file needs a relative path.", nameof(path));
        }
        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The bundle file path '" + path + "' is absolute; a bundle file path is relative to the bundle root.",
                nameof(path));
        }
        if (normalized.Length > 1 && normalized[1] == ':')
        {
            throw new ArgumentException(
                "The bundle file path '" + path + "' names a drive; a bundle file path is relative to the bundle root.",
                nameof(path));
        }

        foreach (string segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                throw new ArgumentException(
                    "The bundle file path '" + path + "' walks outside the bundle with '..'.",
                    nameof(path));
            }
        }

        return normalized;
    }

    /// <summary>
    /// Checks and parses the address the bytes are fetched from.
    /// </summary>
    /// <param name="url">The address as it was supplied.</param>
    /// <returns>The parsed address.</returns>
    /// <exception cref="ArgumentException">The address is missing, relative or neither http nor https.</exception>
    private static Uri ParseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("A bundle file needs an address to fetch it from.", nameof(url));
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri parsed))
        {
            throw new ArgumentException("'" + url + "' is not an absolute address.", nameof(url));
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The address '" + url + "' is " + parsed.Scheme + "; a bundle file is fetched over http or https.",
                nameof(url));
        }

        return parsed;
    }

    /// <summary>
    /// Checks and lower-cases a stated hash.
    /// </summary>
    /// <param name="value">The hash as it was supplied, which may be <see langword="null"/> or empty.</param>
    /// <param name="length">How many hexadecimal characters the algorithm produces.</param>
    /// <param name="algorithm">The algorithm name, for the error message.</param>
    /// <param name="parameterName">The parameter the hash arrived in.</param>
    /// <returns>The lower-case hash, or <see langword="null"/> when none was stated.</returns>
    /// <exception cref="ArgumentException">The hash is not hexadecimal of the expected length.</exception>
    private static string NormalizeHash(string value, int length, string algorithm, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length != length)
        {
            throw new ArgumentException(
                "A " + algorithm + " hash is " + length.ToString(CultureInfo.InvariantCulture)
                    + " hexadecimal characters; '" + value + "' is "
                    + trimmed.Length.ToString(CultureInfo.InvariantCulture) + ".",
                parameterName);
        }

        foreach (char character in trimmed)
        {
            bool hexadecimal = (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F');
            if (!hexadecimal)
            {
                throw new ArgumentException(
                    "A " + algorithm + " hash is hexadecimal; '" + value + "' is not.", parameterName);
            }
        }

        return trimmed.ToLowerInvariant();
    }
}
