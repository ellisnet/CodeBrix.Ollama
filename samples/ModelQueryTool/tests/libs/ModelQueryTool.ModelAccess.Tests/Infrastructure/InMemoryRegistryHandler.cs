using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// A registry that exists only in memory, behind the one seam every request of the store library goes
/// through. It answers manifests by <c>&lt;namespace&gt;/&lt;model&gt;:&lt;tag&gt;</c> and files by
/// digest, with byte ranges, and it writes down every request so a test can say that nothing was asked
/// for at all.
/// </summary>
public sealed class InMemoryRegistryHandler : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> _manifests = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _requests = new List<string>();
    private readonly object _sync = new object();

    /// <summary>
    /// Gets or sets a status code every manifest request is answered with instead of the manifest, or
    /// <see langword="null"/> to answer manifests as usual.
    /// </summary>
    public HttpStatusCode? ManifestStatusCodeOverride { get; set; }

    /// <summary>
    /// Gets or sets something to run before a file's bytes are served, which is where a test that wants
    /// to interrupt a download does the interrupting. The digest asked for is handed to it.
    /// </summary>
    public Func<string, CancellationToken, Task> BeforeFileServed { get; set; }

    /// <summary>Gets every address that has been asked for, oldest first.</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>
    /// The digest of a block of bytes, spelled the way a manifest spells one.
    /// </summary>
    /// <param name="content">The bytes.</param>
    /// <returns>The digest as <c>sha256:&lt;hex&gt;</c>.</returns>
    public static string ComputeDigest(byte[] content)
    {
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));
    }

    /// <summary>
    /// Puts a file into the registry under its own digest.
    /// </summary>
    /// <param name="content">The bytes to serve.</param>
    /// <returns>The digest the file is served under.</returns>
    public string AddFile(byte[] content)
    {
        string digest = ComputeDigest(content);
        lock (_sync)
        {
            _files[digest] = content;
        }
        return digest;
    }

    /// <summary>
    /// Puts a manifest into the registry.
    /// </summary>
    /// <param name="repository">The repository, as <c>&lt;namespace&gt;/&lt;model&gt;</c>.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="body">The exact bytes to serve.</param>
    public void AddManifest(string repository, string tag, byte[] body)
    {
        lock (_sync)
        {
            _manifests[repository + ":" + tag] = body;
        }
    }

    /// <summary>
    /// Forgets every request written down so far, so that a test can ask what happened after a moment
    /// of its choosing.
    /// </summary>
    public void ClearRequests()
    {
        lock (_sync)
        {
            _requests.Clear();
        }
    }

    /// <summary>
    /// Answers one request.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The answer.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _requests.Add(request.Method + " " + request.RequestUri.AbsoluteUri);
        }

        string[] segments = request.RequestUri.AbsolutePath.Trim('/').Split('/');

        if (segments.Length == 5
            && string.Equals(segments[0], "v2", StringComparison.Ordinal)
            && string.Equals(segments[3], "manifests", StringComparison.Ordinal))
        {
            return ServeManifest(segments[1] + "/" + segments[2], segments[4]);
        }

        if (segments.Length == 5
            && string.Equals(segments[0], "v2", StringComparison.Ordinal)
            && string.Equals(segments[3], "blobs", StringComparison.Ordinal))
        {
            string digest = segments[4];
            Func<string, CancellationToken, Task> hook = BeforeFileServed;
            if (hook != null)
            {
                await hook(digest, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return ServeFile(request, digest);
        }

        return CreateTextResponse(HttpStatusCode.NotFound, "no route for " + request.RequestUri);
    }

    /// <summary>
    /// Answers a manifest request.
    /// </summary>
    /// <param name="repository">The repository, as <c>&lt;namespace&gt;/&lt;model&gt;</c>.</param>
    /// <param name="tag">The tag.</param>
    /// <returns>The answer.</returns>
    private HttpResponseMessage ServeManifest(string repository, string tag)
    {
        if (ManifestStatusCodeOverride != null)
        {
            return CreateTextResponse(ManifestStatusCodeOverride.Value, "manifest unknown");
        }

        byte[] body;
        lock (_sync)
        {
            if (!_manifests.TryGetValue(repository + ":" + tag, out body))
            {
                return CreateTextResponse(HttpStatusCode.NotFound, "manifest unknown");
            }
        }

        var content = new ByteArrayContent(body);
        content.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.docker.distribution.manifest.v2+json");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>
    /// Answers a file request: the whole file, a byte range of it, or its length for a HEAD.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="digest">The digest taken from the address.</param>
    /// <returns>The answer.</returns>
    private HttpResponseMessage ServeFile(HttpRequestMessage request, string digest)
    {
        byte[] data;
        lock (_sync)
        {
            if (!_files.TryGetValue(digest, out data))
            {
                return CreateTextResponse(HttpStatusCode.NotFound, "file unknown");
            }
        }

        if (request.Method == HttpMethod.Head)
        {
            var head = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            };
            head.Content.Headers.ContentLength = data.Length;
            head.Headers.TryAddWithoutValidation("accept-ranges", "bytes");
            return head;
        }

        bool hasRange = TryParseRange(GetHeader(request, "Range"), out long start, out long end);
        if (!hasRange)
        {
            var whole = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
            whole.Headers.TryAddWithoutValidation("accept-ranges", "bytes");
            return whole;
        }

        if (start >= data.Length)
        {
            HttpResponseMessage unsatisfiable = CreateTextResponse(
                HttpStatusCode.RequestedRangeNotSatisfiable, "range not satisfiable");
            unsatisfiable.Content.Headers.TryAddWithoutValidation(
                "Content-Range", "bytes */" + data.Length.ToString(CultureInfo.InvariantCulture));
            return unsatisfiable;
        }

        long last = Math.Min(end, data.Length - 1);
        int length = (int)(last - start + 1);
        byte[] slice = new byte[length];
        Array.Copy(data, start, slice, 0, length);

        var partial = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(slice)
        };
        partial.Headers.TryAddWithoutValidation("accept-ranges", "bytes");
        partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, last, data.Length);
        return partial;
    }

    /// <summary>
    /// Builds an answer with a plain-text body.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body text.</param>
    /// <returns>The answer.</returns>
    private static HttpResponseMessage CreateTextResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
    }

    /// <summary>
    /// Reads one header of a request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="name">The header name.</param>
    /// <returns>The value, or <see langword="null"/> when the request did not carry the header.</returns>
    private static string GetHeader(HttpRequestMessage request, string name)
    {
        if (request.Headers.TryGetValues(name, out IEnumerable<string> values))
        {
            return string.Join(", ", values);
        }
        return null;
    }

    /// <summary>
    /// Parses a <c>bytes=start-end</c> header.
    /// </summary>
    /// <param name="rangeHeader">The header value, which may be <see langword="null"/>.</param>
    /// <param name="start">The first byte asked for.</param>
    /// <param name="end">The last byte asked for.</param>
    /// <returns><see langword="true"/> when the header held a range.</returns>
    private static bool TryParseRange(string rangeHeader, out long start, out long end)
    {
        start = 0L;
        end = 0L;
        if (string.IsNullOrEmpty(rangeHeader)
            || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = rangeHeader.Substring("bytes=".Length).Split('-');
        return parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out end);
    }
}
