using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Lists a Hugging Face file repository over the Hub's own HTTP API, which needs neither Python nor the
/// Hub client library: the repository document gives the commit to pin and the licence the publisher
/// states, and the tree document gives every file with its size, its git object identifier and, for
/// files kept in large-file storage, the SHA-256 of its content.
/// </summary>
/// <remarks>
/// <para>
/// The listing is always pinned to a COMMIT. A caller may ask for a branch or a tag, and the listing
/// resolves it once and then names that commit in every address it builds, so that a pull cannot pick
/// up half of one revision and half of the next, and so that a later pull of the same tag can tell
/// "unchanged" from "moved".
/// </para>
/// <para>
/// Nothing here downloads a file. The addresses in the listing are the Hub's <c>resolve</c> addresses,
/// which answer a redirect to a content delivery host that supports byte ranges; the download engine
/// follows that redirect and keeps any access token off the host it leads to.
/// </para>
/// <para>
/// A gated or private repository is refused with a message that says what to do about it, unless
/// <see cref="ModelStoreOptions.BearerToken"/> holds an access token. The token is offered to the Hub
/// host and to no other host, ever.
/// </para>
/// </remarks>
public sealed class HuggingFaceHubSource : IBundleSource, IDisposable
{
    /// <summary>The host the Hub API is served from.</summary>
    public const string HubHost = "huggingface.co";

    /// <summary>The short host name that redirects to <see cref="HubHost"/> and is used in model names.</summary>
    public const string ShortHubHost = "hf.co";

    /// <summary>The revision a listing uses when the caller names none, or names the tag "latest".</summary>
    public const string DefaultRevision = "main";

    private readonly RegistryClient _client;
    private readonly bool _hasToken;
    private bool _disposed;

    /// <summary>
    /// Initializes a source over one repository at one revision.
    /// </summary>
    /// <param name="repository">The repository as <c>&lt;namespace&gt;/&lt;repository&gt;</c>.</param>
    /// <param name="revision">
    /// The branch, tag or commit to list. <see langword="null"/>, empty and the model-name tag
    /// <c>latest</c> all mean <see cref="DefaultRevision"/>.
    /// </param>
    /// <param name="filter">Which files are wanted, or <see langword="null"/> for all of them.</param>
    /// <param name="options">
    /// The store options the requests are made with: the HTTP handler, the user agent and the access
    /// token. <see langword="null"/> means default options, which reach the real Hub anonymously.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The repository is missing or is not <c>&lt;namespace&gt;/&lt;repository&gt;</c>.
    /// </exception>
    public HuggingFaceHubSource(string repository, string revision, FileFilter filter, ModelStoreOptions options)
    {
        Repository = CheckRepository(repository);
        Revision = NormalizeRevision(revision);
        Filter = filter ?? FileFilter.Default;

        ModelStoreOptions effective = options ?? new ModelStoreOptions();
        _hasToken = !string.IsNullOrEmpty(effective.BearerToken);
        _client = new RegistryClient(effective);
        if (_hasToken)
        {
            // The Hub answers an anonymous request for a private repository with a plain 404 rather
            // than a challenge, so a token that exists has to travel with the first request. It still
            // only ever reaches the host the request starts at, which here is always the Hub.
            _client.UseBearerTokenProactively();
        }
    }

    /// <summary>The repository as <c>&lt;namespace&gt;/&lt;repository&gt;</c>.</summary>
    public string Repository { get; }

    /// <summary>The branch, tag or commit that was asked for.</summary>
    public string Revision { get; }

    /// <summary>Which files are wanted. Never <see langword="null"/>.</summary>
    public FileFilter Filter { get; }

    /// <summary>
    /// Whether a host is the Hugging Face Hub, which is the only host an access token is offered to.
    /// </summary>
    /// <param name="host">The host to test.</param>
    /// <returns><see langword="true"/> for <see cref="HubHost"/> and <see cref="ShortHubHost"/>.</returns>
    public static bool IsHubHost(string host)
    {
        return string.Equals(host, HubHost, StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, ShortHubHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Turns what a caller asked for into the revision the Hub understands: nothing, an empty string
    /// and the store's default tag <c>latest</c> all mean the default branch.
    /// </summary>
    /// <param name="revision">The revision as it was asked for.</param>
    /// <returns>The revision to list.</returns>
    public static string NormalizeRevision(string revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return DefaultRevision;
        }

        string trimmed = revision.Trim();
        return string.Equals(trimmed, ModelName.DefaultTag, StringComparison.OrdinalIgnoreCase)
            ? DefaultRevision
            : trimmed;
    }

    /// <summary>
    /// The address the bytes of one file of one commit are fetched from.
    /// </summary>
    /// <param name="repository">The repository as <c>&lt;namespace&gt;/&lt;repository&gt;</c>.</param>
    /// <param name="revision">The commit, branch or tag.</param>
    /// <param name="path">The file's path inside the repository.</param>
    /// <returns>The <c>resolve</c> address, with every path segment escaped.</returns>
    /// <exception cref="ArgumentException">The repository, the revision or the path is missing.</exception>
    public static string BuildFileUrl(string repository, string revision, string path)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException("A repository is required.", nameof(repository));
        }
        if (string.IsNullOrWhiteSpace(revision))
        {
            throw new ArgumentException("A revision is required.", nameof(revision));
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        return "https://" + HubHost + "/" + Escape(repository) + "/resolve/" + Uri.EscapeDataString(revision)
            + "/" + Escape(path);
    }

    /// <summary>
    /// Lists the repository: read the repository document, refuse what needs credentials this source
    /// does not have, then walk the tree of the commit the revision resolved to.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the requests.</param>
    /// <returns>The files that pass the filter, the commit they were listed at and the stated licence.</returns>
    /// <exception cref="RegistryException">
    /// The repository does not exist, needs credentials this source does not have, or answered
    /// something that is not a Hub document.
    /// </exception>
    public async Task<BundleListing> ListAsync(CancellationToken cancellationToken = default)
    {
        string licenseId;
        string commit;

        using (JsonDocument metadata = await ReadRepositoryAsync(cancellationToken).ConfigureAwait(false))
        {
            JsonElement root = metadata.RootElement;
            EnsureReadable(root);
            licenseId = ReadLicenseId(root);
            commit = ReadCommit(root);
        }

        IReadOnlyList<BundleFile> files = await ReadTreeAsync(commit, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BundleFile> kept = Filter.Apply(files);

        return new BundleListing(Repository, commit, BuildLicense(licenseId, kept), kept);
    }

    /// <summary>
    /// Disposes the HTTP client this source made its requests with.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _client.Dispose();
    }

    /// <summary>
    /// Reads the repository document of the revision that was asked for. A revision that is already a
    /// commit needs no resolving, the default branch is described by the plain repository document, and
    /// anything else is asked for by name.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The parsed document, which the caller disposes.</returns>
    /// <exception cref="RegistryException">The repository is missing, refused or unreadable.</exception>
    private async Task<JsonDocument> ReadRepositoryAsync(CancellationToken cancellationToken)
    {
        string address = "https://" + HubHost + "/api/models/" + Escape(Repository);
        if (!IsCommitSha(Revision) && !string.Equals(Revision, DefaultRevision, StringComparison.Ordinal))
        {
            address += "/revision/" + Uri.EscapeDataString(Revision);
        }

        (JsonDocument Document, string NextPage) answer = await ReadJsonAsync(
            new Uri(address), cancellationToken).ConfigureAwait(false);
        return answer.Document;
    }

    /// <summary>
    /// Reads every file of one commit, following the Hub's paging when a repository holds more entries
    /// than one answer carries.
    /// </summary>
    /// <param name="commit">The commit to list.</param>
    /// <param name="cancellationToken">A token that cancels the requests.</param>
    /// <returns>The files, before the filter is applied.</returns>
    /// <exception cref="RegistryException">The tree could not be read.</exception>
    private async Task<IReadOnlyList<BundleFile>> ReadTreeAsync(string commit, CancellationToken cancellationToken)
    {
        var files = new List<BundleFile>();
        Uri next = new Uri(
            "https://" + HubHost + "/api/models/" + Escape(Repository)
                + "/tree/" + Uri.EscapeDataString(commit) + "?recursive=true");

        while (next != null)
        {
            (JsonDocument Document, string NextPage) answer = await ReadJsonAsync(next, cancellationToken)
                .ConfigureAwait(false);
            using (JsonDocument document = answer.Document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new RegistryException(
                        "the Hugging Face Hub did not answer with a file tree for " + Repository + " at " + commit,
                        null,
                        null);
                }

                foreach (JsonElement entry in document.RootElement.EnumerateArray())
                {
                    BundleFile file = ReadTreeEntry(entry, commit);
                    if (file != null)
                    {
                        files.Add(file);
                    }
                }
            }

            next = answer.NextPage == null ? null : new Uri(answer.NextPage);
        }

        return files;
    }

    /// <summary>
    /// Turns one tree entry into a bundle file, and answers <see langword="null"/> for an entry that is
    /// not a file - a directory, which a recursive listing names beside the files inside it.
    /// </summary>
    /// <param name="entry">The entry to read.</param>
    /// <param name="commit">The commit the addresses are pinned to.</param>
    /// <returns>The file, or <see langword="null"/>.</returns>
    private BundleFile ReadTreeEntry(JsonElement entry, string commit)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string type = ReadString(entry, "type");
        if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string path = ReadString(entry, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        long size = ReadInt64(entry, "size");
        string gitSha1 = HexOrNull(ReadString(entry, "oid"), 40);
        string sha256 = null;

        if (entry.TryGetProperty("lfs", out JsonElement lfs) && lfs.ValueKind == JsonValueKind.Object)
        {
            // For a file in large-file storage the Hub states the SHA-256 of the content itself, which
            // is the one hash a download can be held to.
            sha256 = HexOrNull(StripAlgorithm(ReadString(lfs, "oid")), 64);
            long lfsSize = ReadInt64(lfs, "size");
            if (lfsSize >= 0)
            {
                size = lfsSize;
            }
        }

        return new BundleFile(path, BuildFileUrl(Repository, commit, path), size, sha256, null, gitSha1);
    }

    /// <summary>
    /// Fetches one Hub document.
    /// </summary>
    /// <param name="uri">The address to read.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>
    /// The parsed document, which the caller disposes, and the address of the next page when the answer
    /// named one.
    /// </returns>
    /// <exception cref="RegistryException">The request was refused or the answer is not JSON.</exception>
    private async Task<(JsonDocument Document, string NextPage)> ReadJsonAsync(
        Uri uri, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _client.SendWithChallengeAsync(
                HttpMethod.Get, uri, "application/json", null, IsHubHost(uri.Host), false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RegistryException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new RegistryException(BuildNeedsCredentialsMessage(), HttpStatusCode.Unauthorized, exception.ResponseBody);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                string missing = await RegistryClient.ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                throw new RegistryException(
                    "the Hugging Face repository " + Repository + " at revision " + Revision
                        + " was not found. A private repository answers the same way when the request carries no access token.",
                    HttpStatusCode.NotFound,
                    missing);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                string refused = await RegistryClient.ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                throw new RegistryException(BuildNeedsCredentialsMessage(), HttpStatusCode.Forbidden, refused);
            }

            if ((int)response.StatusCode >= 400)
            {
                string body = await RegistryClient.ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                throw new RegistryException(
                    "the Hugging Face Hub answered "
                        + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " for " + uri,
                    response.StatusCode,
                    body);
            }

            byte[] raw = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(raw);
            }
            catch (JsonException exception)
            {
                throw new RegistryException(
                    "the Hugging Face Hub did not answer with JSON for " + uri, exception);
            }

            return (document, ReadNextPage(response));
        }
    }

    /// <summary>
    /// Refuses a repository that needs credentials this source does not have.
    /// </summary>
    /// <param name="root">The repository document.</param>
    /// <exception cref="RegistryException">The repository is gated or private and no token is configured.</exception>
    private void EnsureReadable(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new RegistryException(
                "the Hugging Face Hub did not answer with a repository document for " + Repository, null, null);
        }

        bool gated = false;
        if (root.TryGetProperty("gated", out JsonElement gatedElement))
        {
            // The Hub answers false when a repository is open and the name of the gate - "auto" or
            // "manual" - when it is not.
            gated = gatedElement.ValueKind == JsonValueKind.True
                || (gatedElement.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(gatedElement.GetString()));
        }

        bool restricted = root.TryGetProperty("private", out JsonElement privateElement)
            && privateElement.ValueKind == JsonValueKind.True;

        if ((gated || restricted) && !_hasToken)
        {
            throw new RegistryException(BuildNeedsCredentialsMessage(), null, null);
        }
    }

    /// <summary>
    /// The message a repository that needs credentials is refused with.
    /// </summary>
    /// <returns>The message.</returns>
    private string BuildNeedsCredentialsMessage()
    {
        return "the Hugging Face repository " + Repository
            + " is gated or private. Accept its terms on huggingface.co and set ModelStoreOptions.BearerToken"
            + " to an access token that has been granted access to it.";
    }

    /// <summary>
    /// The commit a repository document names.
    /// </summary>
    /// <param name="root">The repository document.</param>
    /// <returns>The commit.</returns>
    /// <exception cref="RegistryException">The document names no commit and the revision is not one.</exception>
    private string ReadCommit(JsonElement root)
    {
        string sha = ReadString(root, "sha");
        if (!string.IsNullOrWhiteSpace(sha))
        {
            return sha.Trim();
        }

        if (IsCommitSha(Revision))
        {
            return Revision;
        }

        throw new RegistryException(
            "the Hugging Face Hub named no commit for " + Repository + " at revision " + Revision, null, null);
    }

    /// <summary>
    /// The licence identifier a repository document states: the <c>license:</c> tag first, because that
    /// is what the Hub itself indexes, and the card's own <c>license</c> field after it.
    /// </summary>
    /// <param name="root">The repository document.</param>
    /// <returns>The identifier, or <see langword="null"/> when the publisher states none.</returns>
    private static string ReadLicenseId(JsonElement root)
    {
        const string tagPrefix = "license:";

        if (root.TryGetProperty("tags", out JsonElement tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement tag in tags.EnumerateArray())
            {
                if (tag.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                string value = tag.GetString();
                if (value != null && value.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string identifier = value.Substring(tagPrefix.Length).Trim();
                    if (identifier.Length > 0)
                    {
                        return identifier;
                    }
                }
            }
        }

        if (root.TryGetProperty("cardData", out JsonElement card) && card.ValueKind == JsonValueKind.Object
            && card.TryGetProperty("license", out JsonElement license))
        {
            if (license.ValueKind == JsonValueKind.String)
            {
                string value = license.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }

            if (license.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in license.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.GetString()))
                    {
                        return entry.GetString().Trim();
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the licence record a listing reports: the identifier with the repository page it was read
    /// from, or, when the publisher states no identifier at all, the address of a licence file among the
    /// files if there is one.
    /// </summary>
    /// <param name="licenseId">The identifier, or <see langword="null"/>.</param>
    /// <param name="files">The files the listing kept.</param>
    /// <returns>The record.</returns>
    private LicenseRecord BuildLicense(string licenseId, IReadOnlyList<BundleFile> files)
    {
        if (licenseId != null)
        {
            return new LicenseRecord(licenseId, "https://" + HubHost + "/" + Repository, null);
        }

        foreach (BundleFile file in files)
        {
            string name = file.FileName;
            if (name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LICENSE.", StringComparison.OrdinalIgnoreCase))
            {
                return new LicenseRecord(null, file.Url.AbsoluteUri, "The repository states no licence; it ships this file.");
            }
        }

        return LicenseRecord.None;
    }

    /// <summary>
    /// The address of the next page of an answer, taken from the <c>Link</c> header the Hub pages with.
    /// </summary>
    /// <param name="response">The answer to read.</param>
    /// <returns>The address, or <see langword="null"/> when this is the last page.</returns>
    private static string ReadNextPage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string> values))
        {
            return null;
        }

        foreach (string header in values)
        {
            foreach (string part in header.Split(','))
            {
                int open = part.IndexOf('<');
                int close = part.IndexOf('>');
                if (open < 0 || close <= open || part.IndexOf("rel=\"next\"", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string address = part.Substring(open + 1, close - open - 1).Trim();
                if (Uri.TryCreate(address, UriKind.Absolute, out Uri parsed) && IsHubHost(parsed.Host))
                {
                    return parsed.AbsoluteUri;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks the repository a source was built for.
    /// </summary>
    /// <param name="repository">The repository as it was supplied.</param>
    /// <returns>The trimmed repository.</returns>
    /// <exception cref="ArgumentException">It is missing or not <c>&lt;namespace&gt;/&lt;repository&gt;</c>.</exception>
    private static string CheckRepository(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ArgumentException(
                "A Hugging Face source needs a repository, as <namespace>/<repository>.", nameof(repository));
        }

        string trimmed = repository.Trim().Trim('/');
        string[] parts = trimmed.Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new ArgumentException(
                "The Hugging Face repository '" + repository + "' is not <namespace>/<repository>.", nameof(repository));
        }

        return trimmed;
    }

    /// <summary>
    /// Escapes every segment of a path and joins them back with forward slashes, so that a space or a
    /// character outside the ASCII range in a publisher's file name survives the trip.
    /// </summary>
    /// <param name="path">The path, with forward slashes.</param>
    /// <returns>The escaped path.</returns>
    private static string Escape(string path)
    {
        string[] segments = path.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = Uri.EscapeDataString(segments[index]);
        }
        return string.Join("/", segments);
    }

    /// <summary>
    /// Whether a revision is already a commit, in which case there is nothing to resolve.
    /// </summary>
    /// <param name="revision">The revision to test.</param>
    /// <returns><see langword="true"/> for 40 hexadecimal characters.</returns>
    private static bool IsCommitSha(string revision)
    {
        return HexOrNull(revision, 40) != null;
    }

    /// <summary>
    /// Drops an algorithm prefix from a hash the Hub states as <c>sha256:&lt;hex&gt;</c>.
    /// </summary>
    /// <param name="value">The hash as it was stated.</param>
    /// <returns>The hash without a prefix.</returns>
    private static string StripAlgorithm(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        int colon = value.IndexOf(':');
        return colon < 0 ? value : value.Substring(colon + 1);
    }

    /// <summary>
    /// A hash exactly as long as it should be and made only of hexadecimal characters, or
    /// <see langword="null"/>. An answer that states something else states nothing usable, and a
    /// listing is more useful than an exception over a field nothing is verified against.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="length">How many characters the hash has.</param>
    /// <returns>The lower-case value, or <see langword="null"/>.</returns>
    private static string HexOrNull(string value, int length)
    {
        if (value == null || value.Length != length)
        {
            return null;
        }

        foreach (char character in value)
        {
            bool hexadecimal = (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F');
            if (!hexadecimal)
            {
                return null;
            }
        }

        return value.ToLowerInvariant();
    }

    /// <summary>
    /// Reads a string member of a JSON object.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing or not a string.</returns>
    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Reads a number member of a JSON object.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The value, or -1 when it is missing or not a number.</returns>
    private static long ReadInt64(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long parsed))
        {
            return parsed;
        }
        return -1L;
    }
}
