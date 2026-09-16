using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
/// request is recorded for assertions. The byte serving, the recording and the injection live in
/// <see cref="FakeHttpHandlerBase"/>, which the other fake services in this suite share.
/// </remarks>
public sealed class FakeRegistryHandler : FakeHttpHandlerBase
{
    private readonly Dictionary<string, byte[]> _manifests = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _blobs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
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

        return ServeBytes(request, data, IgnoreRangeRequests);
    }
}
