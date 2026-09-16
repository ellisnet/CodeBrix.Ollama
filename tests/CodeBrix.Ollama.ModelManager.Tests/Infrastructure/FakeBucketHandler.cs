using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// An in-memory public storage bucket behind an <see cref="HttpMessageHandler"/>: the XML listing of
/// the objects under a prefix, with paging when a test asks for it, and the objects themselves, served
/// with byte ranges and with the two <c>x-goog-hash</c> headers a real bucket sends - a CRC32C and an
/// MD5, both base64.
/// </summary>
public sealed class FakeBucketHandler : FakeHttpHandlerBase
{
    private readonly List<string> _keys = new List<string>();
    private readonly Dictionary<string, byte[]> _objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    private readonly object _sync = new object();

    /// <summary>The host the bucket is served from.</summary>
    public string Host { get; set; } = "storage.googleapis.com";

    /// <summary>The bucket name the listing answers for.</summary>
    public string BucketName { get; set; } = "magentadata";

    /// <summary>
    /// How many objects one page of the listing carries, or 0 for all of them in one page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Whether a truncated listing names the key to continue after. A bucket that does not leaves the
    /// caller to continue from the last key it saw, which is the same key.
    /// </summary>
    public bool SendNextMarker { get; set; } = true;

    /// <summary>Whether objects carry the <c>x-goog-hash</c> headers.</summary>
    public bool SendHashHeaders { get; set; } = true;

    /// <summary>Whether a HEAD on an object is refused, which makes a caller fall back to a ranged read.</summary>
    public bool RefuseHeadRequests { get; set; }

    /// <summary>
    /// Adds an object.
    /// </summary>
    /// <param name="key">The object key, which is its whole path inside the bucket.</param>
    /// <param name="data">The bytes the object holds.</param>
    public void AddObject(string key, byte[] data)
    {
        lock (_sync)
        {
            if (!_objects.ContainsKey(key))
            {
                _keys.Add(key);
            }
            _objects[key] = data;
        }
    }

    /// <summary>
    /// Adds the placeholder a console creates for a folder: a key that ends in a slash and holds
    /// nothing. A listing names it and a caller is expected to leave it out.
    /// </summary>
    /// <param name="key">The key, which must end in a slash.</param>
    public void AddFolderPlaceholder(string key)
    {
        AddObject(key, Array.Empty<byte>());
    }

    /// <summary>
    /// The MD5 of an object as a bucket states it: base64 rather than hexadecimal.
    /// </summary>
    /// <param name="data">The bytes to hash.</param>
    /// <returns>The base64 MD5.</returns>
    public static string ComputeBase64Md5(byte[] data)
    {
        return Convert.ToBase64String(MD5.HashData(data));
    }

    /// <summary>
    /// Answers one request from the in-memory bucket.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response.</returns>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Record(request);

        Uri uri = request.RequestUri;
        if (!string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(CreateTextResponse(HttpStatusCode.NotFound, "no route for " + uri));
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = Uri.UnescapeDataString(segments[index]);
        }

        if (segments.Length == 0 || !string.Equals(segments[0], BucketName, StringComparison.Ordinal))
        {
            return Task.FromResult(CreateTextResponse(HttpStatusCode.NotFound, "no such bucket"));
        }

        if (segments.Length == 1)
        {
            return Task.FromResult(ServeListing(uri));
        }

        string key = string.Join("/", segments, 1, segments.Length - 1);
        return Task.FromResult(ServeObject(request, key));
    }

    /// <summary>
    /// Answers the XML listing of one page of the objects under a prefix.
    /// </summary>
    /// <param name="uri">The address, whose query carries the prefix and the marker.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage ServeListing(Uri uri)
    {
        string prefix = ReadQuery(uri, "prefix") ?? string.Empty;
        string marker = ReadQuery(uri, "marker");

        List<string> keys;
        lock (_sync)
        {
            keys = new List<string>(_keys);
        }

        var matching = new List<string>();
        foreach (string key in keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal)
                && (marker == null || string.CompareOrdinal(key, marker) > 0))
            {
                matching.Add(key);
            }
        }

        int pageSize = PageSize > 0 ? PageSize : matching.Count;
        bool truncated = matching.Count > pageSize;
        var page = matching.GetRange(0, Math.Min(pageSize, matching.Count));

        var body = new StringBuilder();
        body.Append("<?xml version='1.0' encoding='UTF-8'?>");
        body.Append("<ListBucketResult xmlns='http://doc.s3.amazonaws.com/2006-03-01'>");
        body.Append("<Name>").Append(BucketName).Append("</Name>");
        body.Append("<Prefix>").Append(prefix).Append("</Prefix>");
        body.Append("<IsTruncated>").Append(truncated ? "true" : "false").Append("</IsTruncated>");

        if (truncated && SendNextMarker)
        {
            // A bucket names the LAST key of the page it just sent; the next request continues after it.
            body.Append("<NextMarker>").Append(page[page.Count - 1]).Append("</NextMarker>");
        }

        foreach (string key in page)
        {
            byte[] data;
            lock (_sync)
            {
                data = _objects[key];
            }

            body.Append("<Contents>");
            body.Append("<Key>").Append(key).Append("</Key>");
            body.Append("<Generation>1596811527903346</Generation>");
            body.Append("<LastModified>2020-08-07T14:45:27.902Z</LastModified>");
            body.Append("<ETag>\"").Append(Convert.ToHexStringLower(MD5.HashData(data))).Append("\"</ETag>");
            body.Append("<Size>").Append(data.Length.ToString(CultureInfo.InvariantCulture)).Append("</Size>");
            body.Append("</Contents>");
        }

        body.Append("</ListBucketResult>");
        return CreateContentResponse(HttpStatusCode.OK, body.ToString(), "application/xml");
    }

    /// <summary>
    /// Answers a request for one object.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="key">The object key taken from the address.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage ServeObject(HttpRequestMessage request, string key)
    {
        byte[] data;
        lock (_sync)
        {
            if (!_objects.TryGetValue(key, out data))
            {
                return CreateTextResponse(HttpStatusCode.NotFound, "no such object");
            }
        }

        if (RefuseHeadRequests && request.Method == HttpMethod.Head)
        {
            return CreateTextResponse(HttpStatusCode.MethodNotAllowed, "HEAD is not allowed here");
        }

        HttpResponseMessage response = ServeBytes(request, data, false);
        if (SendHashHeaders)
        {
            response.Headers.TryAddWithoutValidation("x-goog-hash", "crc32c=" + FakeCrc32c(data));
            response.Headers.TryAddWithoutValidation("x-goog-hash", "md5=" + ComputeBase64Md5(data));
        }
        return response;
    }

    /// <summary>
    /// A stand-in for the CRC32C a real bucket sends, which nothing in this library reads: four bytes
    /// of the MD5, base64, so that the header looks right beside the one that matters.
    /// </summary>
    /// <param name="data">The bytes the object holds.</param>
    /// <returns>The base64 value.</returns>
    private static string FakeCrc32c(byte[] data)
    {
        byte[] hash = MD5.HashData(data);
        return Convert.ToBase64String(new[] { hash[0], hash[1], hash[2], hash[3] });
    }

    /// <summary>
    /// Reads one query parameter of an address.
    /// </summary>
    /// <param name="uri">The address.</param>
    /// <param name="name">The parameter name.</param>
    /// <returns>The unescaped value, or <see langword="null"/> when the address does not carry it.</returns>
    private static string ReadQuery(Uri uri, string name)
    {
        foreach (string part in uri.Query.TrimStart('?').Split('&'))
        {
            if (part.StartsWith(name + "=", StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(part.Substring(name.Length + 1));
            }
        }
        return null;
    }
}
