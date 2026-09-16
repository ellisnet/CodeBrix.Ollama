using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// An in-memory Hugging Face Hub behind an <see cref="HttpMessageHandler"/>: the repository document
/// with its commit, its licence tag and its card, the recursive tree of one commit with files, nested
/// directories and large-file entries, and the <c>resolve</c> addresses that answer a redirect to a
/// content delivery host which then serves the bytes with byte-range support.
/// </summary>
/// <remarks>
/// The knobs cover what the source has to cope with: a gated or private repository, a repository that
/// answers 404 to anyone without a token, a Hub that demands a token, a tree that arrives in pages, and
/// - through <see cref="FakeHttpHandlerBase"/> - the dropped connections and stalls the download engine
/// is retried against.
/// </remarks>
public sealed class FakeHubHandler : FakeHttpHandlerBase
{
    private readonly Dictionary<string, string> _defaultCommits = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<(string Repository, string Revision), string> _revisions =
        new Dictionary<(string, string), string>();
    private readonly Dictionary<(string Repository, string Commit), List<TreeEntry>> _trees =
        new Dictionary<(string, string), List<TreeEntry>>();
    private readonly Dictionary<(string Repository, string Commit, string Path), byte[]> _files =
        new Dictionary<(string, string, string), byte[]>();
    private readonly object _sync = new object();

    /// <summary>The host the Hub API is served from.</summary>
    public string HubHost { get; set; } = "huggingface.co";

    /// <summary>The host a <c>resolve</c> address redirects the bytes to.</summary>
    public string CdnHost { get; set; } = "cdn.fake";

    /// <summary>Whether every request to the Hub host must carry the expected bearer token.</summary>
    public bool RequireAuthorization { get; set; }

    /// <summary>The bearer token <see cref="RequireAuthorization"/> accepts.</summary>
    public string ExpectedBearerToken { get; set; } = "hf-token";

    /// <summary>The <c>WWW-Authenticate</c> header value sent with a 401.</summary>
    public string ChallengeHeader { get; set; } = "Bearer realm=\"https://huggingface.co/\"";

    /// <summary>Whether the repository document says the repository is gated.</summary>
    public bool Gated { get; set; }

    /// <summary>What the <c>gated</c> member says when <see cref="Gated"/> is set.</summary>
    public string GatedMode { get; set; } = "manual";

    /// <summary>Whether the repository document says the repository is private.</summary>
    public bool Restricted { get; set; }

    /// <summary>
    /// Whether the repository document answers 404 to a request that carries no <c>Authorization</c>
    /// header, which is how the real Hub hides a private repository.
    /// </summary>
    public bool NotFoundForAnonymous { get; set; }

    /// <summary>
    /// The SPDX identifier the repository document carries as a <c>license:</c> tag, or
    /// <see langword="null"/> for no such tag.
    /// </summary>
    public string LicenseTag { get; set; }

    /// <summary>
    /// The SPDX identifier the repository document carries in <c>cardData.license</c>, or
    /// <see langword="null"/> for no card licence.
    /// </summary>
    public string CardLicense { get; set; }

    /// <summary>
    /// How many tree entries one answer carries, or 0 for all of them in one answer. A smaller number
    /// makes the Hub page the tree with a <c>Link</c> header.
    /// </summary>
    public int TreePageSize { get; set; }

    /// <summary>Whether a <c>resolve</c> address answers with a redirect to <see cref="CdnHost"/>.</summary>
    public bool RedirectResolveToCdn { get; set; } = true;

    /// <summary>
    /// Adds a repository whose default branch points at a commit.
    /// </summary>
    /// <param name="repository">The repository, as <c>&lt;namespace&gt;/&lt;repository&gt;</c>.</param>
    /// <param name="commit">The commit the default branch points at.</param>
    public void AddRepository(string repository, string commit)
    {
        lock (_sync)
        {
            _defaultCommits[repository] = commit;
        }
    }

    /// <summary>
    /// Adds a branch or tag that points at a commit.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="revision">The branch or tag name.</param>
    /// <param name="commit">The commit it points at.</param>
    public void AddRevision(string repository, string revision, string commit)
    {
        lock (_sync)
        {
            _revisions[(repository, revision)] = commit;
        }
    }

    /// <summary>
    /// Adds a file to the tree of one commit.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="commit">The commit.</param>
    /// <param name="path">The path inside the repository, with forward slashes.</param>
    /// <param name="data">The bytes the file holds.</param>
    /// <param name="largeFileStorage">
    /// Whether the entry states an <c>lfs</c> member, which is where the Hub states the SHA-256 of the
    /// content. A file without one states only a git object identifier.
    /// </param>
    public void AddFile(string repository, string commit, string path, byte[] data, bool largeFileStorage)
    {
        lock (_sync)
        {
            if (!_trees.TryGetValue((repository, commit), out List<TreeEntry> entries))
            {
                entries = new List<TreeEntry>();
                _trees[(repository, commit)] = entries;
            }

            entries.Add(new TreeEntry
            {
                Path = path,
                Size = data.Length,
                IsDirectory = false,
                LargeFileStorage = largeFileStorage,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(data)),
                GitSha1 = Convert.ToHexStringLower(SHA1.HashData(data))
            });

            _files[(repository, commit, path)] = data;
        }
    }

    /// <summary>
    /// Adds a directory entry to the tree of one commit, which a recursive listing sends beside the
    /// files inside it.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="commit">The commit.</param>
    /// <param name="path">The directory path inside the repository.</param>
    public void AddDirectory(string repository, string commit, string path)
    {
        lock (_sync)
        {
            if (!_trees.TryGetValue((repository, commit), out List<TreeEntry> entries))
            {
                entries = new List<TreeEntry>();
                _trees[(repository, commit)] = entries;
            }

            entries.Add(new TreeEntry
            {
                Path = path,
                Size = 0,
                IsDirectory = true,
                LargeFileStorage = false,
                Sha256 = null,
                GitSha1 = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(path)))
            });
        }
    }

    /// <summary>
    /// The bytes one file of one commit holds.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="commit">The commit.</param>
    /// <param name="path">The path inside the repository.</param>
    /// <returns>The bytes, or <see langword="null"/> when there is no such file.</returns>
    public byte[] GetFile(string repository, string commit, string path)
    {
        lock (_sync)
        {
            return _files.TryGetValue((repository, commit, path), out byte[] data) ? data : null;
        }
    }

    /// <summary>
    /// Answers one request from the in-memory Hub.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response.</returns>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Record(request);

        Uri uri = request.RequestUri;
        if (string.Equals(uri.Host, CdnHost, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ServeFromCdn(request));
        }

        if (!string.Equals(uri.Host, HubHost, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(CreateTextResponse(HttpStatusCode.NotFound, "no route for " + uri));
        }

        if (RequireAuthorization && !HasExpectedToken(request))
        {
            HttpResponseMessage challenge = CreateTextResponse(HttpStatusCode.Unauthorized, "unauthorized");
            challenge.Headers.TryAddWithoutValidation("WWW-Authenticate", ChallengeHeader);
            return Task.FromResult(challenge);
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = Uri.UnescapeDataString(segments[index]);
        }

        if (segments.Length >= 4 && segments[0] == "api" && segments[1] == "models")
        {
            string repository = segments[2] + "/" + segments[3];

            if (segments.Length == 4)
            {
                return Task.FromResult(ServeRepository(request, repository, ResolveCommit(repository, "main")));
            }

            if (segments.Length == 6 && segments[4] == "revision")
            {
                return Task.FromResult(ServeRepository(request, repository, ResolveCommit(repository, segments[5])));
            }

            if (segments.Length == 6 && segments[4] == "tree")
            {
                return Task.FromResult(ServeTree(repository, ResolveCommit(repository, segments[5]), uri));
            }
        }

        if (segments.Length >= 5 && segments[2] == "resolve")
        {
            string repository = segments[0] + "/" + segments[1];
            string commit = ResolveCommit(repository, segments[3]);
            string path = string.Join("/", segments, 4, segments.Length - 4);
            return Task.FromResult(ServeResolve(request, repository, commit, path));
        }

        return Task.FromResult(CreateTextResponse(HttpStatusCode.NotFound, "no route for " + uri));
    }

    /// <summary>
    /// Whether a request carries the bearer token this Hub expects.
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
    /// The commit a revision names: a branch or tag that was registered, the default branch, or the
    /// revision itself when it is already a commit.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="revision">The revision asked for.</param>
    /// <returns>The commit, or <see langword="null"/> when the repository is unknown.</returns>
    private string ResolveCommit(string repository, string revision)
    {
        lock (_sync)
        {
            if (_revisions.TryGetValue((repository, revision), out string named))
            {
                return named;
            }
            if (_trees.ContainsKey((repository, revision)))
            {
                return revision;
            }
            return _defaultCommits.TryGetValue(repository, out string commit) ? commit : null;
        }
    }

    /// <summary>
    /// Answers a request for the repository document.
    /// </summary>
    /// <param name="request">The request, whose <c>Authorization</c> header decides what an anonymous caller sees.</param>
    /// <param name="repository">The repository.</param>
    /// <param name="commit">The commit the document names.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage ServeRepository(HttpRequestMessage request, string repository, string commit)
    {
        if (commit == null)
        {
            return CreateTextResponse(HttpStatusCode.NotFound, "{\"error\":\"Repository not found\"}");
        }

        if (NotFoundForAnonymous && GetHeader(request, "Authorization") == null)
        {
            return CreateTextResponse(HttpStatusCode.NotFound, "{\"error\":\"Repository not found\"}");
        }

        var tags = new List<string> { "transformers", "safetensors" };
        if (LicenseTag != null)
        {
            tags.Add("license:" + LicenseTag);
        }

        var document = new Dictionary<string, object>
        {
            ["_id"] = "000000000000000000000000",
            ["id"] = repository,
            ["sha"] = commit,
            ["lastModified"] = "2024-10-31T02:12:45.000Z",
            ["private"] = Restricted,
            ["gated"] = Gated ? (object)GatedMode : false,
            ["tags"] = tags
        };

        if (CardLicense != null)
        {
            document["cardData"] = new Dictionary<string, object> { ["license"] = CardLicense };
        }

        return CreateContentResponse(HttpStatusCode.OK, JsonSerializer.Serialize(document), "application/json");
    }

    /// <summary>
    /// Answers a request for the tree of one commit, one page at a time when
    /// <see cref="TreePageSize"/> asks for paging.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="commit">The commit.</param>
    /// <param name="uri">The address, whose query says which page is wanted.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage ServeTree(string repository, string commit, Uri uri)
    {
        List<TreeEntry> entries;
        lock (_sync)
        {
            if (commit == null || !_trees.TryGetValue((repository, commit), out entries))
            {
                return CreateTextResponse(HttpStatusCode.NotFound, "{\"error\":\"Revision not found\"}");
            }
            entries = new List<TreeEntry>(entries);
        }

        int cursor = ReadCursor(uri);
        int pageSize = TreePageSize > 0 ? TreePageSize : entries.Count;
        var page = new List<Dictionary<string, object>>();

        for (int index = cursor; index < entries.Count && page.Count < pageSize; index++)
        {
            TreeEntry entry = entries[index];
            var item = new Dictionary<string, object>
            {
                ["type"] = entry.IsDirectory ? "directory" : "file",
                ["oid"] = entry.GitSha1,
                ["size"] = entry.Size,
                ["path"] = entry.Path
            };

            if (entry.LargeFileStorage)
            {
                item["lfs"] = new Dictionary<string, object>
                {
                    ["oid"] = entry.Sha256,
                    ["size"] = entry.Size,
                    ["pointerSize"] = 134
                };
            }

            page.Add(item);
        }

        HttpResponseMessage response = CreateContentResponse(
            HttpStatusCode.OK, JsonSerializer.Serialize(page), "application/json");

        int next = cursor + page.Count;
        if (next < entries.Count)
        {
            response.Headers.TryAddWithoutValidation(
                "Link",
                "<https://" + HubHost + "/api/models/" + repository + "/tree/" + commit
                    + "?recursive=true&cursor=" + next.ToString(CultureInfo.InvariantCulture) + ">; rel=\"next\"");
        }

        return response;
    }

    /// <summary>
    /// Answers a <c>resolve</c> request, either with the bytes or with the redirect the real Hub sends.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="repository">The repository.</param>
    /// <param name="commit">The commit.</param>
    /// <param name="path">The path inside the repository.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage ServeResolve(HttpRequestMessage request, string repository, string commit, string path)
    {
        byte[] data = GetFile(repository, commit, path);
        if (data == null)
        {
            return CreateTextResponse(HttpStatusCode.NotFound, "Entry not found");
        }

        if (!RedirectResolveToCdn)
        {
            return ServeBytes(request, data, false);
        }

        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri(BuildCdnUrl(repository, commit, path));
        redirect.Headers.TryAddWithoutValidation("x-linked-size", data.Length.ToString(CultureInfo.InvariantCulture));
        redirect.Headers.TryAddWithoutValidation("x-linked-etag", "\"" + Convert.ToHexStringLower(SHA256.HashData(data)) + "\"");
        redirect.Content = new ByteArrayContent(Array.Empty<byte>());
        return redirect;
    }

    /// <summary>
    /// Serves the bytes from the content delivery host a <c>resolve</c> address redirects to.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage ServeFromCdn(HttpRequestMessage request)
    {
        string[] segments = request.RequestUri.AbsolutePath.Trim('/').Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = Uri.UnescapeDataString(segments[index]);
        }

        if (segments.Length < 5 || segments[0] != "repos")
        {
            return CreateTextResponse(HttpStatusCode.NotFound, "no route for " + request.RequestUri);
        }

        string repository = segments[1] + "/" + segments[2];
        string commit = segments[3];
        string path = string.Join("/", segments, 4, segments.Length - 4);

        byte[] data = GetFile(repository, commit, path);
        if (data == null)
        {
            return CreateTextResponse(HttpStatusCode.NotFound, "no such object");
        }

        return ServeBytes(request, data, false);
    }

    /// <summary>
    /// The address on the content delivery host that one file of one commit is served from.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="commit">The commit.</param>
    /// <param name="path">The path inside the repository.</param>
    /// <returns>The address.</returns>
    private string BuildCdnUrl(string repository, string commit, string path)
    {
        var builder = new List<string> { "repos", repository.Split('/')[0], repository.Split('/')[1], commit };
        builder.AddRange(path.Split('/'));

        for (int index = 0; index < builder.Count; index++)
        {
            builder[index] = Uri.EscapeDataString(builder[index]);
        }

        return "https://" + CdnHost + "/" + string.Join("/", builder) + "?signature=fake";
    }

    /// <summary>
    /// The entry a tree page starts at, taken from the <c>cursor</c> query parameter.
    /// </summary>
    /// <param name="uri">The address of the request.</param>
    /// <returns>The index, or 0 when the request named none.</returns>
    private static int ReadCursor(Uri uri)
    {
        foreach (string part in uri.Query.TrimStart('?').Split('&'))
        {
            if (part.StartsWith("cursor=", StringComparison.Ordinal)
                && int.TryParse(part.Substring("cursor=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cursor))
            {
                return cursor;
            }
        }
        return 0;
    }

    /// <summary>
    /// One entry of a fake repository tree.
    /// </summary>
    private sealed class TreeEntry
    {
        /// <summary>The path inside the repository.</summary>
        public string Path { get; set; }

        /// <summary>The size in bytes.</summary>
        public long Size { get; set; }

        /// <summary>Whether the entry is a directory rather than a file.</summary>
        public bool IsDirectory { get; set; }

        /// <summary>Whether the entry states an <c>lfs</c> member.</summary>
        public bool LargeFileStorage { get; set; }

        /// <summary>The SHA-256 of the content, which only a large-file entry states.</summary>
        public string Sha256 { get; set; }

        /// <summary>The git object identifier.</summary>
        public string GitSha1 { get; set; }
    }
}
