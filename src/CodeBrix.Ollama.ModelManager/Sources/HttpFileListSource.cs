using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Lists a bundle that is nothing but a list of addresses. The caller already knows what the files are;
/// this fills in what it does not know - the size of a file the caller did not state one for, and the
/// MD5 a storage bucket sends in a header - so that a pull can report its size before it starts and
/// verify what arrives.
/// </summary>
/// <remarks>
/// A file whose size the caller already stated costs no request at all. A file whose size is unknown is
/// asked for with a HEAD, and, when the server will not answer a HEAD, with a request for the first byte
/// of it, whose <c>Content-Range</c> states the whole length.
/// </remarks>
public sealed class HttpFileListSource : IBundleSource, IDisposable
{
    private readonly RegistryClient _client;
    private bool _disposed;

    /// <summary>
    /// Initializes a source over a list of files, with default options.
    /// </summary>
    /// <param name="files">The files the bundle holds.</param>
    /// <param name="filter">Which of them are wanted, or <see langword="null"/> for all of them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One of the files is <see langword="null"/>.</exception>
    public HttpFileListSource(IReadOnlyList<BundleFile> files, FileFilter filter)
        : this(files, filter, null)
    {
    }

    /// <summary>
    /// Initializes a source over a list of files.
    /// </summary>
    /// <param name="files">The files the bundle holds.</param>
    /// <param name="filter">Which of them are wanted, or <see langword="null"/> for all of them.</param>
    /// <param name="options">
    /// The store options the requests are made with. <see langword="null"/> means default options.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One of the files is <see langword="null"/>.</exception>
    public HttpFileListSource(IReadOnlyList<BundleFile> files, FileFilter filter, ModelStoreOptions options)
    {
        if (files == null)
        {
            throw new ArgumentNullException(nameof(files));
        }

        var copy = new List<BundleFile>(files.Count);
        foreach (BundleFile file in files)
        {
            if (file == null)
            {
                throw new ArgumentException("A file list cannot carry a null file.", nameof(files));
            }
            copy.Add(file);
        }

        Files = copy.AsReadOnly();
        Filter = filter ?? FileFilter.Default;
        _client = new RegistryClient(options ?? new ModelStoreOptions());
    }

    /// <summary>The files the bundle holds, as the caller listed them.</summary>
    public IReadOnlyList<BundleFile> Files { get; }

    /// <summary>Which of them are wanted. Never <see langword="null"/>.</summary>
    public FileFilter Filter { get; }

    /// <summary>
    /// Applies the filter and asks the servers for whatever the caller did not state.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the requests.</param>
    /// <returns>
    /// The files that pass the filter, with their sizes filled in. The listing carries no repository and
    /// no revision, because a list of addresses has neither.
    /// </returns>
    /// <exception cref="RegistryException">A server would say neither how large a file is nor serve a byte of it.</exception>
    public async Task<BundleListing> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BundleFile> kept = Filter.Apply(Files);
        var described = new List<BundleFile>(kept.Count);

        foreach (BundleFile file in kept)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.HasSize)
            {
                described.Add(file);
                continue;
            }

            described.Add(await DescribeAsync(file, cancellationToken).ConfigureAwait(false));
        }

        return new BundleListing(null, null, LicenseRecord.None, described);
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
    /// Asks the server how large one file is, and takes its MD5 while it is answering when it states one.
    /// </summary>
    /// <param name="file">The file to describe.</param>
    /// <param name="cancellationToken">A token that cancels the requests.</param>
    /// <returns>The file with what the server stated filled in.</returns>
    /// <exception cref="RegistryException">The server stated no size either way.</exception>
    private async Task<BundleFile> DescribeAsync(BundleFile file, CancellationToken cancellationToken)
    {
        long size = BundleFile.UnknownSize;
        string md5 = null;

        using (HttpResponseMessage head = await _client.SendWithChallengeAsync(
            HttpMethod.Head, file.Url, null, null, true, false, cancellationToken).ConfigureAwait(false))
        {
            if ((int)head.StatusCode < 400)
            {
                size = head.Content.Headers.ContentLength ?? BundleFile.UnknownSize;
                md5 = GoogleCloudStorageListing.ReadMd5Header(head);
            }
        }

        if (size < 0)
        {
            // Some servers refuse a HEAD outright. Asking for the first byte gets the whole length in
            // the Content-Range of the answer, at the cost of one byte.
            using HttpResponseMessage ranged = await _client.SendWithChallengeAsync(
                HttpMethod.Get, file.Url, null, "bytes=0-0", true, false, cancellationToken).ConfigureAwait(false);

            if ((int)ranged.StatusCode >= 400)
            {
                string body = await RegistryClient.ReadBodyAsync(ranged, cancellationToken).ConfigureAwait(false);
                throw new RegistryException(
                    "the server at " + file.Url.Host + " answered "
                        + ((int)ranged.StatusCode).ToString(CultureInfo.InvariantCulture)
                        + " when asked how large '" + file.Path + "' is",
                    ranged.StatusCode,
                    body);
            }

            if (ranged.StatusCode == HttpStatusCode.PartialContent
                && ranged.Content.Headers.ContentRange != null
                && ranged.Content.Headers.ContentRange.Length != null)
            {
                size = ranged.Content.Headers.ContentRange.Length.Value;
            }
            else if (ranged.StatusCode == HttpStatusCode.OK && ranged.Content.Headers.ContentLength != null)
            {
                size = ranged.Content.Headers.ContentLength.Value;
            }

            if (md5 == null)
            {
                md5 = GoogleCloudStorageListing.ReadMd5Header(ranged);
            }
        }

        if (size < 0)
        {
            throw new RegistryException(
                "the server at " + file.Url.Host + " would not say how large '" + file.Path + "' is",
                null,
                null);
        }

        BundleFile described = file.WithSize(size);
        return md5 != null && described.Md5 == null ? described.WithMd5(md5) : described;
    }
}
