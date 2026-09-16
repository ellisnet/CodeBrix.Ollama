// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: server/images.go at commit a43fad18.
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The HTTP side of pulling a model: manifest fetches, blob size lookups and blob downloads against a
/// Docker-distribution v2 registry, with Ollama's own request shapes, error mapping and retry rules.
/// </summary>
/// <remarks>
/// <para>
/// Redirects are never left to the handler. The client's own handler is created with automatic
/// redirects switched off, and a handler supplied through <see cref="ModelStoreOptions.HttpMessageHandler"/>
/// is left exactly as the caller configured it, so every redirect this class cares about is followed
/// here, by hand. That is what lets it keep the rule Ollama relies on: the <c>Authorization</c> header
/// goes to the host the request started at and to no other, because a redirect to a content delivery
/// host carries its own signature in the URL.
/// </para>
/// <para>
/// <see cref="HttpClient.Timeout"/> is infinite. Nothing here is abandoned because a clock ran out
/// except through a <see cref="CancellationToken"/> this class controls, which is how a stalled byte
/// range is detected and retried without failing the whole download.
/// </para>
/// </remarks>
internal sealed class RegistryClient : IDisposable
{
    /// <summary>How many redirects are followed before a request is given up on, as in Ollama.</summary>
    private const int MaxRedirects = 10;

    /// <summary>The default base delay between retries of a failed byte range.</summary>
    private static readonly TimeSpan DefaultRetryBaseDelay = TimeSpan.FromSeconds(1);

    private readonly ModelStoreOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHandler;
    private readonly TimeSpan _retryBaseDelay;
    private readonly string _userAgent;
    private int _bearerTokenEstablished;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryClient"/> class.
    /// </summary>
    /// <param name="options">The store options the client reads its registry settings from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public RegistryClient(ModelStoreOptions options) : this(options, DefaultRetryBaseDelay)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryClient"/> class with an explicit retry
    /// base delay. Tests use this to keep their retries quick; everything else uses one second, which
    /// is what Ollama waits before its first retry.
    /// </summary>
    /// <param name="options">The store options the client reads its registry settings from.</param>
    /// <param name="retryBaseDelay">
    /// The base delay of the exponential back-off between retries of a failed byte range. The delay
    /// before retry <c>n</c> (counting from zero) is this value doubled <c>n</c> times, capped at one
    /// minute.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public RegistryClient(ModelStoreOptions options, TimeSpan retryBaseDelay)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _options = options;
        _retryBaseDelay = retryBaseDelay > TimeSpan.Zero ? retryBaseDelay : DefaultRetryBaseDelay;

        HttpMessageHandler handler = options.HttpMessageHandler;
        if (handler == null)
        {
            handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            _ownsHandler = true;
        }

        _httpClient = new HttpClient(handler, _ownsHandler);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _userAgent = string.IsNullOrWhiteSpace(options.UserAgent) ? BuildDefaultUserAgent() : options.UserAgent;
    }

    /// <summary>The store options this client was built from.</summary>
    public ModelStoreOptions Options
    {
        get { return _options; }
    }

    /// <summary>The <c>User-Agent</c> header value sent on every request.</summary>
    public string UserAgent
    {
        get { return _userAgent; }
    }

    /// <summary>
    /// The address of the manifest of <paramref name="name"/>:
    /// <c>&lt;scheme&gt;://&lt;host&gt;/v2/&lt;namespace&gt;/&lt;model&gt;/manifests/&lt;tag&gt;</c>.
    /// </summary>
    /// <param name="name">The model name. It must be fully qualified.</param>
    /// <returns>The manifest address.</returns>
    public Uri GetManifestUri(ModelName name)
    {
        return new Uri(name.BaseUrl(), "/v2/" + name.DisplayNamespaceModel() + "/manifests/" + name.Tag);
    }

    /// <summary>
    /// The address of one blob of <paramref name="name"/>:
    /// <c>&lt;scheme&gt;://&lt;host&gt;/v2/&lt;namespace&gt;/&lt;model&gt;/blobs/&lt;digest&gt;</c>.
    /// </summary>
    /// <param name="name">The model name. It must be fully qualified.</param>
    /// <param name="digest">The blob digest in <c>sha256:&lt;hex&gt;</c> form.</param>
    /// <returns>The blob address.</returns>
    /// <exception cref="ArgumentException"><paramref name="digest"/> is null or empty.</exception>
    public Uri GetBlobUri(ModelName name, string digest)
    {
        if (string.IsNullOrEmpty(digest))
        {
            throw new ArgumentException("A blob digest is required.", nameof(digest));
        }
        return new Uri(name.BaseUrl(), "/v2/" + name.DisplayNamespaceModel() + "/blobs/" + digest);
    }

    /// <summary>
    /// Fetches the manifest of <paramref name="name"/> from the registry the name points at.
    /// </summary>
    /// <param name="name">The model name. It must be fully qualified.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The parsed manifest together with the bytes it arrived in.</returns>
    /// <exception cref="ModelNotFoundException">The registry answered 404.</exception>
    /// <exception cref="RegistryException">
    /// The name asks for plain HTTP and <see cref="ModelStoreOptions.AllowInsecureHttp"/> is not set;
    /// or the registry answered an error status; or the answer was not a manifest; or the request
    /// never reached the registry.
    /// </exception>
    public async Task<RegistryManifestResponse> GetManifestAsync(ModelName name, CancellationToken cancellationToken = default)
    {
        EnsureSchemeAllowed(name);

        Uri requestUri = GetManifestUri(name);
        using HttpResponseMessage response = await SendWithChallengeAsync(
            HttpMethod.Get, requestUri, MediaTypes.Manifest, null, true, false, cancellationToken).ConfigureAwait(false);

        await ThrowForErrorStatusAsync(
            response, name, "model " + name + " not found on " + name.Host, cancellationToken).ConfigureAwait(false);

        byte[] raw;
        try
        {
            raw = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new RegistryException(exception.Message, exception);
        }
        catch (IOException exception)
        {
            throw new RegistryException(exception.Message, exception);
        }

        ModelManifest manifest;
        try
        {
            manifest = ModelManagerJson.Deserialize<ModelManifest>(raw);
        }
        catch (JsonException exception)
        {
            throw new RegistryException(
                "the registry at " + name.Host + " did not answer with a model manifest for " + name, exception);
        }

        if (manifest == null || (manifest.Config == null && (manifest.Layers == null || manifest.Layers.Count == 0)))
        {
            throw new RegistryException(
                "the registry at " + name.Host + " did not answer with a model manifest for " + name, null, null);
        }

        return new RegistryManifestResponse(manifest, raw);
    }

    /// <summary>
    /// Asks the registry how large a blob is, with a HEAD request, and reads the answer's
    /// <c>Content-Length</c>.
    /// </summary>
    /// <param name="name">The model name the blob belongs to. It must be fully qualified.</param>
    /// <param name="digest">The blob digest in <c>sha256:&lt;hex&gt;</c> form.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The blob size in bytes.</returns>
    /// <exception cref="ModelNotFoundException">The registry answered 404.</exception>
    /// <exception cref="RegistryException">
    /// The name asks for plain HTTP and <see cref="ModelStoreOptions.AllowInsecureHttp"/> is not set;
    /// or the registry answered an error status or no size at all; or the request never reached the
    /// registry.
    /// </exception>
    public async Task<long> GetBlobSizeAsync(ModelName name, string digest, CancellationToken cancellationToken = default)
    {
        EnsureSchemeAllowed(name);

        Uri requestUri = GetBlobUri(name, digest);
        using HttpResponseMessage response = await SendWithChallengeAsync(
            HttpMethod.Head, requestUri, null, null, true, false, cancellationToken).ConfigureAwait(false);

        await ThrowForErrorStatusAsync(
            response, name, "blob " + digest + " of model " + name + " not found on " + name.Host, cancellationToken).ConfigureAwait(false);

        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength == null)
        {
            throw new RegistryException(
                "the registry at " + name.Host + " did not report a size for blob " + digest, response.StatusCode, null);
        }
        return contentLength.Value;
    }

    /// <summary>
    /// Downloads one blob to <paramref name="destinationPath"/>, resuming an interrupted earlier
    /// attempt when the sidecar files are still there, and verifying the result against
    /// <paramref name="digest"/> before the file is put in place.
    /// </summary>
    /// <param name="name">The model name the blob belongs to. It must be fully qualified.</param>
    /// <param name="digest">The blob digest in <c>sha256:&lt;hex&gt;</c> form.</param>
    /// <param name="expectedSize">
    /// The blob size in bytes as the manifest states it, or -1 when it is not known and the registry
    /// should be asked for it.
    /// </param>
    /// <param name="destinationPath">Where the finished, verified file is moved to.</param>
    /// <param name="partialDataPath">The file the bytes are written to while the download runs.</param>
    /// <param name="partialStatePath">The JSON sidecar that records the byte ranges and their progress.</param>
    /// <param name="progress">
    /// Called with the completed and total byte counts while the download runs, at most once per
    /// <see cref="ModelStoreOptions.ProgressInterval"/> and once more when it finishes.
    /// <see langword="null"/> asks for no reports.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels the download. A cancelled download leaves the partial file and its sidecar
    /// in place so that a later call can pick it up.
    /// </param>
    /// <returns>A task that completes when the file is at <paramref name="destinationPath"/>.</returns>
    /// <exception cref="ModelNotFoundException">The registry answered 404 for the blob.</exception>
    /// <exception cref="DigestMismatchException">
    /// The downloaded bytes do not hash to <paramref name="digest"/>. The partial file and its sidecar
    /// have been deleted.
    /// </exception>
    /// <exception cref="RegistryException">
    /// The name asks for plain HTTP and <see cref="ModelStoreOptions.AllowInsecureHttp"/> is not set;
    /// or a byte range failed every retry; or the registry answered an error status.
    /// </exception>
    public async Task DownloadBlobAsync(
        ModelName name,
        string digest,
        long expectedSize,
        string destinationPath,
        string partialDataPath,
        string partialStatePath,
        Action<long, long> progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(digest))
        {
            throw new ArgumentException("A blob digest is required.", nameof(digest));
        }
        if (string.IsNullOrEmpty(destinationPath))
        {
            throw new ArgumentException("A destination path is required.", nameof(destinationPath));
        }
        if (string.IsNullOrEmpty(partialDataPath))
        {
            throw new ArgumentException("A partial data path is required.", nameof(partialDataPath));
        }
        if (string.IsNullOrEmpty(partialStatePath))
        {
            throw new ArgumentException("A partial state path is required.", nameof(partialStatePath));
        }

        EnsureSchemeAllowed(name);

        var download = new BlobDownload(
            this, _options, name, digest, expectedSize, destinationPath, partialDataPath, partialStatePath, _retryBaseDelay);
        await download.RunAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a request, following redirects, and answers a 401 once by repeating the request with the
    /// configured bearer token. This is the port of Ollama's <c>makeRequestWithRetry</c>, with the
    /// token taken from the options instead of fetched from the challenge realm.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="requestUri">The address to request.</param>
    /// <param name="accept">The <c>Accept</c> header value, or <see langword="null"/> for none.</param>
    /// <param name="rangeHeader">The <c>Range</c> header value, or <see langword="null"/> for none.</param>
    /// <param name="sendAuthorization">
    /// Whether the <c>Authorization</c> header may be sent. It is only ever sent to the host the
    /// request started at, never to a host a redirect led to.
    /// </param>
    /// <param name="stopAtCrossHostRedirect">
    /// When <see langword="true"/>, a redirect that points at a different host is returned to the
    /// caller instead of being followed. Redirects within the same host are always followed.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response, which the caller owns and must dispose.</returns>
    /// <exception cref="RegistryException">
    /// The registry answered 401 and no bearer token is configured, or one was already sent; or the
    /// request never reached the registry; or there were too many redirects.
    /// </exception>
    internal async Task<HttpResponseMessage> SendWithChallengeAsync(
        HttpMethod method,
        Uri requestUri,
        string accept,
        string rangeHeader,
        bool sendAuthorization,
        bool stopAtCrossHostRedirect,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            HttpResponseMessage response = await SendFollowingRedirectsAsync(
                method, requestUri, accept, rangeHeader, sendAuthorization, stopAtCrossHostRedirect, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return response;
            }

            string body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            RegistryChallenge challenge = RegistryChallenge.Parse(ReadWwwAuthenticate(response));
            response.Dispose();

            bool haveToken = !string.IsNullOrEmpty(_options.BearerToken);
            bool alreadySent = Volatile.Read(ref _bearerTokenEstablished) != 0;
            if (attempt == 0 && sendAuthorization && haveToken && !alreadySent)
            {
                Interlocked.Exchange(ref _bearerTokenEstablished, 1);
                continue;
            }

            throw new RegistryException(
                BuildUnauthorizedMessage(requestUri, challenge), HttpStatusCode.Unauthorized, body);
        }

        throw new RegistryException(
            BuildUnauthorizedMessage(requestUri, RegistryChallenge.Parse(null)), HttpStatusCode.Unauthorized, null);
    }

    /// <summary>
    /// Throws the exception an error status calls for, and does nothing for a status below 400.
    /// </summary>
    /// <param name="response">The response to inspect.</param>
    /// <param name="name">The model name the request was about.</param>
    /// <param name="notFoundMessage">The message a 404 is reported with.</param>
    /// <param name="cancellationToken">A token that cancels reading the response body.</param>
    /// <returns>A task that completes when the response has been inspected.</returns>
    /// <exception cref="ModelNotFoundException">The status is 404.</exception>
    /// <exception cref="RegistryException">The status is 400 or above and not 404.</exception>
    internal static async Task ThrowForErrorStatusAsync(
        HttpResponseMessage response, ModelName name, string notFoundMessage, CancellationToken cancellationToken)
    {
        if ((int)response.StatusCode < 400)
        {
            return;
        }

        string body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ModelNotFoundException(name.ToString(), notFoundMessage);
        }

        throw new RegistryException(
            ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ": " + body, response.StatusCode, body);
    }

    /// <summary>
    /// Reads a response body as text, answering an empty string when it cannot be read.
    /// </summary>
    /// <param name="response">The response to read.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The body text.</returns>
    internal static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Whether a status code is a redirect this client follows.
    /// </summary>
    /// <param name="statusCode">The status code to test.</param>
    /// <returns><see langword="true"/> for 301, 302, 303, 307 and 308.</returns>
    internal static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.MovedPermanently
            || statusCode == HttpStatusCode.Found
            || statusCode == HttpStatusCode.SeeOther
            || statusCode == HttpStatusCode.TemporaryRedirect
            || statusCode == HttpStatusCode.PermanentRedirect;
    }

    /// <summary>
    /// Refuses a name that asks for plain HTTP unless the options allow it. Ollama refuses the same
    /// case with <c>errInsecureProtocol</c>, before any request goes out.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <exception cref="RegistryException">The scheme is http and plain HTTP is not allowed.</exception>
    private void EnsureSchemeAllowed(ModelName name)
    {
        if (string.Equals(name.ProtocolScheme, "http", StringComparison.OrdinalIgnoreCase) && !_options.AllowInsecureHttp)
        {
            throw new RegistryException("insecure protocol http", null, null);
        }
    }

    /// <summary>
    /// Sends one request and follows redirects by hand, up to <see cref="MaxRedirects"/> of them.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="requestUri">The address to request.</param>
    /// <param name="accept">The <c>Accept</c> header value, or <see langword="null"/> for none.</param>
    /// <param name="rangeHeader">The <c>Range</c> header value, or <see langword="null"/> for none.</param>
    /// <param name="sendAuthorization">Whether the <c>Authorization</c> header may be sent to the original host.</param>
    /// <param name="stopAtCrossHostRedirect">Whether a redirect to another host is returned rather than followed.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response, which the caller owns and must dispose.</returns>
    /// <exception cref="RegistryException">There were too many redirects, or the request failed in transport.</exception>
    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(
        HttpMethod method,
        Uri requestUri,
        string accept,
        string rangeHeader,
        bool sendAuthorization,
        bool stopAtCrossHostRedirect,
        CancellationToken cancellationToken)
    {
        Uri originalUri = requestUri;
        Uri currentUri = requestUri;

        for (int redirects = 0; ; redirects++)
        {
            if (redirects > MaxRedirects)
            {
                throw new RegistryException(
                    "maximum redirects exceeded (" + MaxRedirects.ToString(CultureInfo.InvariantCulture) + ") for " + originalUri,
                    null,
                    null);
            }

            bool sameHost = string.Equals(currentUri.Host, originalUri.Host, StringComparison.OrdinalIgnoreCase);
            HttpRequestMessage request = CreateRequest(method, currentUri, accept, rangeHeader, sendAuthorization && sameHost);
            HttpResponseMessage response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode) || response.Headers.Location == null)
            {
                return response;
            }

            Uri next = new Uri(currentUri, response.Headers.Location);
            if (stopAtCrossHostRedirect && !string.Equals(next.Host, originalUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return response;
            }

            response.Dispose();
            currentUri = next;
        }
    }

    /// <summary>
    /// Sends one request and turns a transport failure into a <see cref="RegistryException"/>.
    /// </summary>
    /// <param name="request">The request to send. This method takes ownership of it.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response, with only the headers read so far.</returns>
    /// <exception cref="RegistryException">The request never reached the registry.</exception>
    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new RegistryException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Builds one request with the headers every registry request carries.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="requestUri">The address to request.</param>
    /// <param name="accept">The <c>Accept</c> header value, or <see langword="null"/> for none.</param>
    /// <param name="rangeHeader">The <c>Range</c> header value, or <see langword="null"/> for none.</param>
    /// <param name="includeAuthorization">Whether to send the bearer token, when one has been established.</param>
    /// <returns>The request.</returns>
    private HttpRequestMessage CreateRequest(
        HttpMethod method, Uri requestUri, string accept, string rangeHeader, bool includeAuthorization)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        if (!string.IsNullOrEmpty(accept))
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }
        if (!string.IsNullOrEmpty(rangeHeader))
        {
            request.Headers.TryAddWithoutValidation("Range", rangeHeader);
        }
        if (includeAuthorization
            && !string.IsNullOrEmpty(_options.BearerToken)
            && Volatile.Read(ref _bearerTokenEstablished) != 0)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _options.BearerToken);
        }
        return request;
    }

    /// <summary>
    /// Reads the <c>WWW-Authenticate</c> header of a response as a single string.
    /// </summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The header value, or an empty string when the response did not carry one.</returns>
    private static string ReadWwwAuthenticate(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("WWW-Authenticate", out IEnumerable<string> values))
        {
            return string.Join(", ", values);
        }
        return string.Empty;
    }

    /// <summary>
    /// Builds the message of the exception a final 401 is reported with.
    /// </summary>
    /// <param name="requestUri">The address that was refused.</param>
    /// <param name="challenge">The challenge the registry sent with the refusal.</param>
    /// <returns>The message.</returns>
    private static string BuildUnauthorizedMessage(Uri requestUri, RegistryChallenge challenge)
    {
        string message = "unauthorized: access denied for " + requestUri;
        if (!string.IsNullOrEmpty(challenge.Realm))
        {
            message += " (realm " + challenge.Realm + ")";
        }
        return message;
    }

    /// <summary>
    /// Builds the default <c>User-Agent</c>: the library name and version, then the operating system
    /// and the process architecture, in the shape Ollama uses for its own.
    /// </summary>
    /// <returns>The header value.</returns>
    private static string BuildDefaultUserAgent()
    {
        Assembly assembly = typeof(RegistryClient).Assembly;
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
        {
            Version assemblyVersion = assembly.GetName().Version;
            version = assemblyVersion == null ? "0.0.0" : assemblyVersion.ToString();
        }
        int plus = version.IndexOf('+');
        if (plus >= 0)
        {
            version = version.Substring(0, plus);
        }

        string operatingSystem = "unknown";
        if (OperatingSystem.IsWindows())
        {
            operatingSystem = "windows";
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            operatingSystem = "darwin";
        }
        else if (OperatingSystem.IsLinux())
        {
            operatingSystem = "linux";
        }
        else if (OperatingSystem.IsAndroid())
        {
            operatingSystem = "android";
        }
        else if (OperatingSystem.IsIOS())
        {
            operatingSystem = "ios";
        }
        else if (OperatingSystem.IsFreeBSD())
        {
            operatingSystem = "freebsd";
        }

        string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        return "CodeBrix.Ollama.ModelManager/" + version + " (" + operatingSystem + "; " + architecture + ")";
    }

    /// <summary>
    /// Disposes the <see cref="HttpClient"/>. A handler supplied through
    /// <see cref="ModelStoreOptions.HttpMessageHandler"/> belongs to the caller and is left open; only
    /// a handler this client created is disposed with it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _httpClient.Dispose();
    }
}
