using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Turns a public Google Cloud Storage bucket prefix into a list of bundle files. A bucket speaks no
/// model protocol at all: it answers an XML listing of the objects under a prefix, and each object
/// answers a HEAD with the MD5 the bucket holds for it. That is enough to pull a model whose publisher
/// only ever put it in a bucket.
/// </summary>
/// <remarks>
/// The listing is paged, and this follows the paging to the end. The MD5 arrives in an
/// <c>x-goog-hash</c> header, base64 encoded, beside a CRC32C in a header of the same name; both are
/// read and only the MD5 is kept, because that is the one a download can verify while it streams.
/// </remarks>
public static class GoogleCloudStorageListing
{
    /// <summary>The host a public bucket is served from.</summary>
    public const string StorageHost = "storage.googleapis.com";

    /// <summary>
    /// Lists every object under a prefix.
    /// </summary>
    /// <param name="bucket">The bucket name, for example <c>magentadata</c>.</param>
    /// <param name="prefix">
    /// The prefix the objects sit under, for example <c>models/music_transformer/</c>. It is stripped
    /// from the front of each key to give the file its path inside the bundle, so a listing of
    /// <c>models/music_transformer/</c> yields <c>checkpoints/&lt;name&gt;</c> rather than the whole key.
    /// An empty prefix lists the whole bucket and keeps the keys as they are.
    /// </param>
    /// <param name="handler">
    /// The handler the requests go through, or <see langword="null"/> for a default one. Tests supply a
    /// fake handler here; nothing here reaches the network any other way.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the requests.</param>
    /// <returns>
    /// One file per object, in the order the bucket listed them, with the size from the listing and the
    /// MD5 from the object's own headers. Keys that end in a slash are placeholders for a folder and are
    /// left out.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="bucket"/> is missing.</exception>
    /// <exception cref="RegistryException">
    /// The bucket answered an error status, or something that is not a listing.
    /// </exception>
    public static async Task<IReadOnlyList<BundleFile>> ListAsync(
        string bucket, string prefix, HttpMessageHandler handler, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new ArgumentException("A bucket name is required.", nameof(bucket));
        }

        string bucketName = bucket.Trim().Trim('/');
        string keyPrefix = prefix == null ? string.Empty : prefix.TrimStart('/');

        var options = new ModelStoreOptions { HttpMessageHandler = handler };
        using var client = new RegistryClient(options);

        var files = new List<BundleFile>();
        string marker = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            XElement listing = await ReadListingAsync(client, bucketName, keyPrefix, marker, cancellationToken)
                .ConfigureAwait(false);

            string lastKey = null;
            foreach (XElement contents in Children(listing, "Contents"))
            {
                string key = ChildValue(contents, "Key");
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                lastKey = key;
                if (key.EndsWith("/", StringComparison.Ordinal))
                {
                    // A key that ends in a slash is the placeholder a console creates for a folder.
                    continue;
                }

                string path = StripPrefix(key, keyPrefix);
                if (path.Length == 0)
                {
                    continue;
                }

                long size = ParseSize(ChildValue(contents, "Size"));
                string url = BuildObjectUrl(bucketName, key);
                string md5 = await ReadObjectMd5Async(client, new Uri(url), cancellationToken).ConfigureAwait(false);
                files.Add(new BundleFile(path, url, size, null, md5, null));
            }

            if (!IsTruncated(listing))
            {
                return files;
            }

            // The bucket names the key to continue from; when it does not, the last key of this page is
            // where the next one starts.
            marker = ChildValue(listing, "NextMarker");
            if (string.IsNullOrEmpty(marker))
            {
                marker = lastKey;
            }
            if (string.IsNullOrEmpty(marker))
            {
                return files;
            }
        }
    }

    /// <summary>
    /// The address one object of a bucket is fetched from.
    /// </summary>
    /// <param name="bucket">The bucket name.</param>
    /// <param name="key">The object key.</param>
    /// <returns>The address, with every key segment escaped.</returns>
    public static string BuildObjectUrl(string bucket, string key)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new ArgumentException("A bucket name is required.", nameof(bucket));
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("An object key is required.", nameof(key));
        }

        string[] segments = key.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = Uri.EscapeDataString(segments[index]);
        }

        return "https://" + StorageHost + "/" + bucket.Trim().Trim('/') + "/" + string.Join("/", segments);
    }

    /// <summary>
    /// Reads the MD5 out of the <c>x-goog-hash</c> headers of an answer. An object usually carries two
    /// of them, <c>crc32c=</c> and <c>md5=</c>, and the header may arrive either as two values or as one
    /// value holding both.
    /// </summary>
    /// <param name="response">The answer to read.</param>
    /// <returns>The MD5 as lower-case hexadecimal, or <see langword="null"/> when no MD5 was sent.</returns>
    internal static string ReadMd5Header(HttpResponseMessage response)
    {
        var values = new List<string>();
        if (response.Headers.TryGetValues("x-goog-hash", out IEnumerable<string> fromHeaders))
        {
            values.AddRange(fromHeaders);
        }
        if (response.Content != null && response.Content.Headers.TryGetValues("x-goog-hash", out IEnumerable<string> fromContent))
        {
            values.AddRange(fromContent);
        }

        foreach (string value in values)
        {
            foreach (string part in value.Split(','))
            {
                string trimmed = part.Trim();
                if (!trimmed.StartsWith("md5=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string encoded = trimmed.Substring("md5=".Length).Trim();
                try
                {
                    byte[] hash = Convert.FromBase64String(encoded);
                    if (hash.Length == 16)
                    {
                        return Convert.ToHexStringLower(hash);
                    }
                }
                catch (FormatException)
                {
                    // A hash that is not base64 states nothing; the download falls back to the SHA-256
                    // it computes for itself.
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Fetches one page of the listing.
    /// </summary>
    /// <param name="client">The client the request goes through.</param>
    /// <param name="bucket">The bucket name.</param>
    /// <param name="prefix">The key prefix.</param>
    /// <param name="marker">The key to continue from, or <see langword="null"/> for the first page.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The root element of the listing.</returns>
    /// <exception cref="RegistryException">The bucket answered an error status or something that is not a listing.</exception>
    private static async Task<XElement> ReadListingAsync(
        RegistryClient client, string bucket, string prefix, string marker, CancellationToken cancellationToken)
    {
        string address = "https://" + StorageHost + "/" + bucket + "/?prefix=" + Uri.EscapeDataString(prefix);
        if (!string.IsNullOrEmpty(marker))
        {
            address += "&marker=" + Uri.EscapeDataString(marker);
        }

        var uri = new Uri(address);
        using HttpResponseMessage response = await client.SendWithChallengeAsync(
            HttpMethod.Get, uri, null, null, false, false, cancellationToken).ConfigureAwait(false);

        string body = await RegistryClient.ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        if ((int)response.StatusCode >= 400)
        {
            throw new RegistryException(
                "the bucket " + bucket + " answered "
                    + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " for " + uri,
                response.StatusCode,
                body);
        }

        try
        {
            return XDocument.Parse(body).Root;
        }
        catch (System.Xml.XmlException exception)
        {
            throw new RegistryException("the bucket " + bucket + " did not answer with a listing", exception);
        }
    }

    /// <summary>
    /// Asks one object for the hashes the bucket holds for it.
    /// </summary>
    /// <param name="client">The client the request goes through.</param>
    /// <param name="uri">The object address.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The MD5 as lower-case hexadecimal, or <see langword="null"/>.</returns>
    private static async Task<string> ReadObjectMd5Async(
        RegistryClient client, Uri uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.SendWithChallengeAsync(
            HttpMethod.Head, uri, null, null, false, false, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return ReadMd5Header(response);
        }

        // An object that will not answer a HEAD still downloads; it is verified by the SHA-256 the
        // store computes for it instead.
        return null;
    }

    /// <summary>
    /// Whether a listing says there is another page.
    /// </summary>
    /// <param name="listing">The root element of the listing.</param>
    /// <returns><see langword="true"/> when the listing is truncated.</returns>
    private static bool IsTruncated(XElement listing)
    {
        string value = ChildValue(listing, "IsTruncated");
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The child elements of one name, whatever XML namespace the bucket wrote them in.
    /// </summary>
    /// <param name="parent">The element to look inside, which may be <see langword="null"/>.</param>
    /// <param name="name">The local name to match.</param>
    /// <returns>The matching children.</returns>
    private static IEnumerable<XElement> Children(XElement parent, string name)
    {
        if (parent == null)
        {
            yield break;
        }

        foreach (XElement child in parent.Elements())
        {
            if (string.Equals(child.Name.LocalName, name, StringComparison.Ordinal))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// The text of the first child element of one name.
    /// </summary>
    /// <param name="parent">The element to look inside, which may be <see langword="null"/>.</param>
    /// <param name="name">The local name to match.</param>
    /// <returns>The text, or <see langword="null"/> when there is no such child.</returns>
    private static string ChildValue(XElement parent, string name)
    {
        foreach (XElement child in Children(parent, name))
        {
            return child.Value;
        }
        return null;
    }

    /// <summary>
    /// Reads the size a listing states for an object.
    /// </summary>
    /// <param name="value">The text of the size element.</param>
    /// <returns>The size in bytes, or <see cref="BundleFile.UnknownSize"/> when it cannot be read.</returns>
    private static long ParseSize(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            && parsed >= 0)
        {
            return parsed;
        }
        return BundleFile.UnknownSize;
    }

    /// <summary>
    /// Takes the prefix off the front of a key, so that a file keeps the path the publisher's own tree
    /// gives it rather than the whole key.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="prefix">The prefix that was listed.</param>
    /// <returns>The path inside the bundle.</returns>
    private static string StripPrefix(string key, string prefix)
    {
        string path = key;
        if (prefix.Length > 0 && path.StartsWith(prefix, StringComparison.Ordinal))
        {
            path = path.Substring(prefix.Length);
        }
        return path.TrimStart('/');
    }
}
