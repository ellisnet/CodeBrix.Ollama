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
/// What every fake service in this suite needs and none of them should write twice: a record of the
/// requests it saw, the byte-range serving the download engine is exercised against, and the failures a
/// test injects into one attempt at one range - a dropped connection, a stall, a chosen status code.
/// </summary>
/// <remarks>
/// A derived handler decides what lives at which address; this decides how bytes are served once it has
/// found them. Attempts are counted per address and per range, so several files being fetched at once
/// each get their own attempt numbers, while the methods that inject a failure name only the range and
/// therefore apply to whichever file the test is fetching.
/// </remarks>
public abstract class FakeHttpHandlerBase : HttpMessageHandler
{
    /// <summary>The key a request without a <c>Range</c> header is counted and injected under.</summary>
    public const long NoRange = -1;

    private readonly List<HttpRequestMessage> _requests = new List<HttpRequestMessage>();
    private readonly Dictionary<(string Path, long RangeStart), int> _attemptCounts =
        new Dictionary<(string, long), int>();
    private readonly Dictionary<(long RangeStart, int Attempt), int> _dropAfterBytes = new Dictionary<(long, int), int>();
    private readonly Dictionary<(long RangeStart, int Attempt), int> _stallAfterBytes = new Dictionary<(long, int), int>();
    private readonly Dictionary<(long RangeStart, int Attempt), HttpStatusCode> _statusFailures =
        new Dictionary<(long, int), HttpStatusCode>();
    private readonly TaskCompletionSource _stallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _sync = new object();

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
    /// The addresses of every request the handler has seen, oldest first.
    /// </summary>
    /// <returns>The addresses.</returns>
    public IReadOnlyList<string> RequestedUrls()
    {
        return Requests.Select(request => request.RequestUri.AbsoluteUri).ToArray();
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
    /// Records a copy of a request, so that assertions can read it after it has been disposed.
    /// </summary>
    /// <param name="request">The request to record.</param>
    protected void Record(HttpRequestMessage request)
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
    /// Serves a block of bytes: a HEAD with the length, a byte range with 206 and a
    /// <c>Content-Range</c>, a whole body with 200, a 416 for a range past the end, and whatever
    /// failure was injected for this attempt at this range.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="data">The bytes that live at the address.</param>
    /// <param name="ignoreRanges">Whether to answer the whole body however the request asked.</param>
    /// <returns>The response.</returns>
    protected HttpResponseMessage ServeBytes(HttpRequestMessage request, byte[] data, bool ignoreRanges)
    {
        if (request.Method == HttpMethod.Head)
        {
            var head = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            head.Content.Headers.ContentLength = data.Length;
            head.Headers.TryAddWithoutValidation("accept-ranges", "bytes");
            return head;
        }

        string rangeHeader = ignoreRanges ? null : GetHeader(request, "Range");
        long rangeStart = NoRange;
        long rangeEnd = data.Length - 1;
        bool hasRange = TryParseRange(rangeHeader, out long parsedStart, out long parsedEnd);
        if (hasRange)
        {
            rangeStart = parsedStart;
            rangeEnd = Math.Min(parsedEnd, data.Length - 1);
        }

        int attempt = NextAttempt(request.RequestUri.AbsolutePath, rangeStart);
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
        response.Headers.TryAddWithoutValidation("accept-ranges", "bytes");
        if (hasRange)
        {
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(sliceStart, rangeEnd, data.Length);
        }
        return response;
    }

    /// <summary>
    /// Builds a response with a plain-text body.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body text.</param>
    /// <returns>The response.</returns>
    protected static HttpResponseMessage CreateTextResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
    }

    /// <summary>
    /// Builds a response with a body of a chosen media type.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body text.</param>
    /// <param name="mediaType">The media type of the body.</param>
    /// <returns>The response.</returns>
    protected static HttpResponseMessage CreateContentResponse(HttpStatusCode statusCode, string body, string mediaType)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        };
    }

    /// <summary>
    /// Parses a <c>bytes=start-end</c> header.
    /// </summary>
    /// <param name="rangeHeader">The header value, which may be <see langword="null"/>.</param>
    /// <param name="start">The first byte asked for.</param>
    /// <param name="end">The last byte asked for.</param>
    /// <returns><see langword="true"/> when the header held a range.</returns>
    protected static bool TryParseRange(string rangeHeader, out long start, out long end)
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
    /// Counts and returns the attempt number of a request for one byte range of one address.
    /// </summary>
    /// <param name="path">The address path the request was for.</param>
    /// <param name="rangeStart">The first byte of the range, or <see cref="NoRange"/>.</param>
    /// <returns>The one-based attempt number.</returns>
    private int NextAttempt(string path, long rangeStart)
    {
        lock (_sync)
        {
            _attemptCounts.TryGetValue((path, rangeStart), out int count);
            count++;
            _attemptCounts[(path, rangeStart)] = count;
            return count;
        }
    }

    /// <summary>
    /// The body stream of a response, which can be made to end early or to stop sending.
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
