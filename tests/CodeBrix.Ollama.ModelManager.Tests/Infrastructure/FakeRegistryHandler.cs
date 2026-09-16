using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// An in-memory Docker-distribution v2 registry behind an <see cref="HttpMessageHandler"/>, so the
/// registry client and the blob downloader can be exercised without a network.
/// </summary>
/// <remarks>
/// It serves manifests by <c>&lt;namespace&gt;/&lt;model&gt;:&lt;tag&gt;</c> and blobs by digest, with
/// full support for byte ranges (206, Content-Range and 416), an optional bearer challenge, an
/// optional redirect of blob requests to a second host that stands in for a content delivery network,
/// and per-attempt failures that can be injected for any byte range: dropping the connection after a
/// number of bytes, stalling until the request is cancelled, or answering a chosen status code. Every
/// request is recorded for assertions.
/// </remarks>
public sealed class FakeRegistryHandler : HttpMessageHandler
{
    /// <summary>The key a request without a <c>Range</c> header is counted and injected under.</summary>
    public const long NoRange = -1;

    private readonly Dictionary<string, byte[]> _manifests = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _blobs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    private readonly List<HttpRequestMessage> _requests = new List<HttpRequestMessage>();
    private readonly Dictionary<long, int> _attemptCounts = new Dictionary<long, int>();
    private readonly Dictionary<(long RangeStart, int Attempt), int> _dropAfterBytes = new Dictionary<(long, int), int>();
    private readonly Dictionary<(long RangeStart, int Attempt), int> _stallAfterBytes = new Dictionary<(long, int), int>();
    private readonly Dictionary<(long RangeStart, int Attempt), HttpStatusCode> _statusFailures = new Dictionary<(long, int), HttpStatusCode>();
    private readonly TaskCompletionSource _stallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _sync = new object();

    /// <summary>The host blob requests are redirected to when <see cref="RedirectBlobsToCdn"/> is set.</summary>
    public string CdnHost { get; set; } = "cdn.test";

    /// <summary>Whether requests to the registry host must carry the expected bearer token.</summary>
    public bool RequireAuthorization { get; set; }

    /// <summary>The bearer token <see cref="RequireAuthorization"/> accepts.</summary>
    public string ExpectedBearerToken { get; set; } = "test-token";

    /// <summary>The <c>WWW-Authenticate</c> header value sent with a 401.</summary>
    public string ChallengeHeader { get; set; } =
        "Bearer realm=\"https://auth.test/token\",service=\"registry\",scope=\"repo:library/test:pull\"";

    /// <summary>Whether a blob request to the registry host is answered with a 307 to <see cref="CdnHost"/>.</summary>
    public bool RedirectBlobsToCdn { get; set; }

    /// <summary>Whether blob requests are answered in full with a 200, ignoring any <c>Range</c> header.</summary>
    public bool IgnoreRangeRequests { get; set; }

    /// <summary>
    /// A status code every manifest request is answered with instead of the manifest, or
    /// <see langword="null"/> to serve manifests normally.
    /// </summary>
    public HttpStatusCode? ManifestStatusCodeOverride { get; set; }

    /// <summary>
    /// Completes as soon as an injected stall begins, which lets a test act the moment the download is
    /// known to be waiting instead of sleeping.
    /// </summary>
    public Task StallStarted
    {
        get { return _stallStarted.Task; }
    }

    /// <summary>Every request the handler has seen, oldest first, as method, address and headers.</summary>
    public IReadOnlyList<HttpRequestMessage> Requests
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
    /// Adds a blob and returns its digest.
    /// </summary>
    /// <param name="data">The blob bytes.</param>
    /// <returns>The digest in <c>sha256:&lt;hex&gt;</c> form.</returns>
    public string AddBlob(byte[] data)
    {
        string digest = ComputeDigest(data);
        AddBlob(digest, data);
        return digest;
    }

    /// <summary>
    /// Adds a blob under a digest of the caller's choosing, which is how a test makes the registry
    /// serve bytes that do not match what the manifest promised.
    /// </summary>
    /// <param name="digest">The digest to serve the bytes under.</param>
    /// <param name="data">The blob bytes.</param>
    public void AddBlob(string digest, byte[] data)
    {
        lock (_sync)
        {
            _blobs[digest] = data;
        }
    }

    /// <summary>
    /// Adds a manifest.
    /// </summary>
    /// <param name="repository">The repository, as <c>&lt;namespace&gt;/&lt;model&gt;</c>.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="manifest">The manifest to serve.</param>
    public void AddManifest(string repository, string tag, ModelManifest manifest)
    {
        AddManifest(repository, tag, ModelManagerJson.SerializeLikeGo(manifest));
    }

    /// <summary>
    /// Adds a manifest as raw bytes, which is how a test makes the registry answer something that is
    /// not a manifest at all.
    /// </summary>
    /// <param name="repository">The repository, as <c>&lt;namespace&gt;/&lt;model&gt;</c>.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="body">The bytes to serve.</param>
    public void AddManifest(string repository, string tag, byte[] body)
    {
        lock (_sync)
        {
            _manifests[repository + ":" + tag] = body;
        }
    }

    /// <summary>
    /// Makes one attempt at one byte range end after a number of bytes, as a dropped connection does.
    /// </summary>
    /// <param name="rangeStart">The first byte of the range, or <see cref="NoRange"/> for a request without one.</param>
    /// <param name="attempt">The one-based attempt at that range the failure applies to.</param>
    /// <param name="afterBytes">How many bytes are served before the connection ends.</param>
    public void DropConnectionAfter(long rangeStart, int attempt, int afterBytes)
    {
        lock (_sync)
        {
            _dropAfterBytes[(rangeStart, attempt)] = afterBytes;
        }
    }

    /// <summary>
    /// Makes one attempt at one byte range stop sending and never finish, until the request is
    /// cancelled.
    /// </summary>
    /// <param name="rangeStart">The first byte of the range, or <see cref="NoRange"/> for a request without one.</param>
    /// <param name="attempt">The one-based attempt at that range the stall applies to.</param>
    /// <param name="afterBytes">How many bytes are served before the stall begins.</param>
    public void StallAfter(long rangeStart, int attempt, int afterBytes)
    {
        lock (_sync)
        {
            _stallAfterBytes[(rangeStart, attempt)] = afterBytes;
        }
    }

    /// <summary>
    /// Makes one attempt at one byte range answer with a chosen status code instead of the bytes.
    /// </summary>
    /// <param name="rangeStart">The first byte of the range, or <see cref="NoRange"/> for a request without one.</param>
    /// <param name="attempt">The one-based attempt at that range the status applies to.</param>
    /// <param name="statusCode">The status code to answer with.</param>
    public void FailWithStatus(long rangeStart, int attempt, HttpStatusCode statusCode)
    {
        lock (_sync)
        {
            _statusFailures[(rangeStart, attempt)] = statusCode;
        }
    }

    /// <summary>
    /// The <c>Range</c> header values of every request that carried one, oldest first.
    /// </summary>
    /// <returns>The header values.</returns>
    public IReadOnlyList<string> RangeHeaders()
    {
        return Requests
            .Select(request => GetHeader(request, "Range"))
            .Where(value => value != null)
            .ToArray();
    }

    /// <summary>
    /// Whether any request to a host carried an <c>Authorization</c> header.
    /// </summary>
    /// <param name="host">The host to look at.</param>
    /// <returns><see langword="true"/> when at least one request to that host was authorized.</returns>
    public bool AnyAuthorizationSentTo(string host)
    {
        return Requests.Any(request =>
            string.Equals(request.RequestUri.Host, host, StringComparison.OrdinalIgnoreCase)
            && GetHeader(request, "Authorization") != null);
    }

    /// <summary>
    /// How many requests were made to a host.
    /// </summary>
    /// <param name="host">The host to count.</param>
    /// <returns>The number of requests.</returns>
    public int RequestCountForHost(string host)
    {
        return Requests.Count(request =>
            string.Equals(request.RequestUri.Host, host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads one header of a recorded request.
    /// </summary>
    /// <param name="request">The recorded request.</param>
    /// <param name="name">The header name.</param>
    /// <returns>The header value, or <see langword="null"/> when the request did not carry it.</returns>
    public static string GetHeader(HttpRequestMessage request, string name)
    {
        if (request.Headers.TryGetValues(name, out IEnumerable<string> values))
        {
            return string.Join(", ", values);
        }
        return null;
    }

    /// <summary>
    /// The SHA-256 digest of a buffer, written the way a manifest writes one.
    /// </summary>
    /// <param name="data">The bytes to hash.</param>
    /// <returns>The digest in <c>sha256:&lt;hex&gt;</c> form.</returns>
    public static string ComputeDigest(byte[] data)
    {
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(data));
    }

    /// <summary>
    /// Answers one request from the in-memory registry.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response.</returns>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Record(request);

        Uri uri = request.RequestUri;
        bool isCdn = string.Equals(uri.Host, CdnHost, StringComparison.OrdinalIgnoreCase);

        if (RequireAuthorization && !isCdn && !HasExpectedToken(request))
        {
            return Task.FromResult(CreateChallenge());
        }

        string path = uri.AbsolutePath.Trim('/');
        string[] segments = path.Split('/');

        if (isCdn)
        {
            return Task.FromResult(ServeBlob(request, segments[segments.Length - 1], true));
        }

        if (segments.Length == 5 && segments[0] == "v2" && segments[3] == "manifests")
        {
            return Task.FromResult(ServeManifest(segments[1] + "/" + segments[2], segments[4]));
        }

        if (segments.Length == 5 && segments[0] == "v2" && segments[3] == "blobs")
        {
            return Task.FromResult(ServeBlob(request, segments[4], false));
        }

        return Task.FromResult(CreateTextResponse(HttpStatusCode.NotFound, "no route for " + uri));
    }

    /// <summary>
    /// Records a copy of a request, so that assertions can read it after it has been disposed.
    /// </summary>
    /// <param name="request">The request to record.</param>
    private void Record(HttpRequestMessage request)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        lock (_sync)
        {
            _requests.Add(copy);
        }
    }

    /// <summary>
    /// Whether a request carries the bearer token this registry expects.
    /// </summary>
    /// <param name="request">The request to inspect.</param>
    /// <returns><see langword="true"/> when the token is there and correct.</returns>
    private bool HasExpectedToken(HttpRequestMessage request)
    {
        string authorization = GetHeader(request, "Authorization");
        return authorization != null
            && string.Equals(authorization, "Bearer " + ExpectedBearerToken, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the 401 answer, with the challenge header.
    /// </summary>
    /// <returns>The response.</returns>
    private HttpResponseMessage CreateChallenge()
    {
        HttpResponseMessage response = CreateTextResponse(HttpStatusCode.Unauthorized, "unauthorized");
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", ChallengeHeader);
        return response;
    }

    /// <summary>
    /// Answers a manifest request.
    /// </summary>
    /// <param name="repository">The repository, as <c>&lt;namespace&gt;/&lt;model&gt;</c>.</param>
    /// <param name="tag">The tag.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage ServeManifest(string repository, string tag)
    {
        if (ManifestStatusCodeOverride != null)
        {
            return CreateTextResponse(ManifestStatusCodeOverride.Value, "the registry is unwell");
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
        content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypes.Manifest);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>
    /// Answers a blob request: a redirect, a size, a byte range or the whole blob, with any injected
    /// failure for this attempt applied.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="digest">The digest taken from the address.</param>
    /// <param name="isCdn">Whether the request arrived at the content delivery host.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage ServeBlob(HttpRequestMessage request, string digest, bool isCdn)
    {
        byte[] data;
        lock (_sync)
        {
            if (!_blobs.TryGetValue(digest, out data))
            {
                return CreateTextResponse(HttpStatusCode.NotFound, "blob unknown");
            }
        }

        if (RedirectBlobsToCdn && !isCdn)
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            redirect.Headers.Location = new Uri("https://" + CdnHost + "/blobs/" + digest + "?signature=fake");
            redirect.Content = new ByteArrayContent(Array.Empty<byte>());
            return redirect;
        }

        if (request.Method == HttpMethod.Head)
        {
            var head = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            head.Content.Headers.ContentLength = data.Length;
            return head;
        }

        string rangeHeader = IgnoreRangeRequests ? null : GetHeader(request, "Range");
        long rangeStart = NoRange;
        long rangeEnd = data.Length - 1;
        bool hasRange = TryParseRange(rangeHeader, out long parsedStart, out long parsedEnd);
        if (hasRange)
        {
            rangeStart = parsedStart;
            rangeEnd = Math.Min(parsedEnd, data.Length - 1);
        }

        int attempt = NextAttempt(rangeStart);
        (long, int) key = (rangeStart, attempt);

        HttpStatusCode injectedStatus;
        lock (_sync)
        {
            if (_statusFailures.TryGetValue(key, out injectedStatus))
            {
                return CreateTextResponse(injectedStatus, "injected failure");
            }
        }

        if (hasRange && rangeStart >= data.Length)
        {
            HttpResponseMessage unsatisfiable = CreateTextResponse(
                HttpStatusCode.RequestedRangeNotSatisfiable, "range not satisfiable");
            unsatisfiable.Content.Headers.TryAddWithoutValidation(
                "Content-Range", "bytes */" + data.Length.ToString(CultureInfo.InvariantCulture));
            return unsatisfiable;
        }

        long sliceStart = hasRange ? rangeStart : 0;
        int sliceLength = (int)(rangeEnd - sliceStart + 1);
        byte[] slice = new byte[sliceLength];
        Array.Copy(data, sliceStart, slice, 0, sliceLength);

        int dropAfter = -1;
        int stallAfter = -1;
        lock (_sync)
        {
            if (_dropAfterBytes.TryGetValue(key, out int drop))
            {
                dropAfter = drop;
            }
            if (_stallAfterBytes.TryGetValue(key, out int stall))
            {
                stallAfter = stall;
            }
        }

        var content = new StreamContent(new InjectedStream(slice, dropAfter, stallAfter, _stallStarted));
        content.Headers.ContentLength = sliceLength;

        var response = new HttpResponseMessage(hasRange ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = content
        };
        if (hasRange)
        {
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(sliceStart, rangeEnd, data.Length);
        }
        return response;
    }

    /// <summary>
    /// Counts and returns the attempt number of a request for one byte range.
    /// </summary>
    /// <param name="rangeStart">The first byte of the range, or <see cref="NoRange"/>.</param>
    /// <returns>The one-based attempt number.</returns>
    private int NextAttempt(long rangeStart)
    {
        lock (_sync)
        {
            _attemptCounts.TryGetValue(rangeStart, out int count);
            count++;
            _attemptCounts[rangeStart] = count;
            return count;
        }
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
        start = 0;
        end = 0;
        if (string.IsNullOrEmpty(rangeHeader) || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = rangeHeader.Substring("bytes=".Length).Split('-');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out end))
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Builds a response with a plain-text body.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body text.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage CreateTextResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
    }

    /// <summary>
    /// The body stream of a blob response, which can be made to end early or to stop sending.
    /// </summary>
    private sealed class InjectedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _dropAfter;
        private readonly int _stallAfter;
        private readonly TaskCompletionSource _stallStarted;
        private int _position;

        /// <summary>
        /// Initializes a new instance of the <see cref="InjectedStream"/> class.
        /// </summary>
        /// <param name="data">The bytes to serve.</param>
        /// <param name="dropAfter">How many bytes to serve before ending the connection, or -1 to serve them all.</param>
        /// <param name="stallAfter">How many bytes to serve before stalling, or -1 never to stall.</param>
        /// <param name="stallStarted">The signal completed when a stall begins.</param>
        public InjectedStream(byte[] data, int dropAfter, int stallAfter, TaskCompletionSource stallStarted)
        {
            _data = data;
            _dropAfter = dropAfter;
            _stallAfter = stallAfter;
            _stallStarted = stallStarted;
        }

        /// <summary>Always <see langword="true"/>.</summary>
        public override bool CanRead
        {
            get { return true; }
        }

        /// <summary>Always <see langword="false"/>.</summary>
        public override bool CanSeek
        {
            get { return false; }
        }

        /// <summary>Always <see langword="false"/>.</summary>
        public override bool CanWrite
        {
            get { return false; }
        }

        /// <summary>Not supported.</summary>
        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        /// <summary>Not supported.</summary>
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        /// <summary>Does nothing.</summary>
        public override void Flush()
        {
        }

        /// <summary>
        /// Reads the next bytes, applying whatever failure was injected for this response.
        /// </summary>
        /// <param name="buffer">The buffer to read into.</param>
        /// <param name="cancellationToken">A token that cancels the read, which is what ends a stall.</param>
        /// <returns>The number of bytes read, or zero at the end of the data.</returns>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_dropAfter >= 0 && _position >= _dropAfter)
            {
                throw new IOException("The server closed the connection.");
            }

            if (_stallAfter >= 0 && _position >= _stallAfter)
            {
                _stallStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            if (_position >= _data.Length)
            {
                return 0;
            }

            int available = _data.Length - _position;
            if (_dropAfter >= 0)
            {
                available = Math.Min(available, _dropAfter - _position);
            }
            if (_stallAfter >= 0)
            {
                available = Math.Min(available, _stallAfter - _position);
            }

            int count = Math.Min(buffer.Length, available);
            _data.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            return count;
        }

        /// <summary>
        /// Reads the next bytes into an array segment.
        /// </summary>
        /// <param name="buffer">The buffer to read into.</param>
        /// <param name="offset">Where in the buffer to start.</param>
        /// <param name="count">How many bytes to read at most.</param>
        /// <param name="cancellationToken">A token that cancels the read.</param>
        /// <returns>The number of bytes read.</returns>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        /// <summary>
        /// Reads the next bytes, blocking.
        /// </summary>
        /// <param name="buffer">The buffer to read into.</param>
        /// <param name="offset">Where in the buffer to start.</param>
        /// <param name="count">How many bytes to read at most.</param>
        /// <returns>The number of bytes read.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>Not supported.</summary>
        /// <param name="offset">Unused.</param>
        /// <param name="origin">Unused.</param>
        /// <returns>Never returns.</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <summary>Not supported.</summary>
        /// <param name="value">Unused.</param>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <summary>Not supported.</summary>
        /// <param name="buffer">Unused.</param>
        /// <param name="offset">Unused.</param>
        /// <param name="count">Unused.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
