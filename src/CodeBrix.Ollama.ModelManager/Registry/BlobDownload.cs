using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama server/download.go;

/// <summary>
/// One blob download: the blob is split into byte ranges, the ranges are fetched concurrently into a
/// single partial file, their progress is recorded in a JSON sidecar so an interrupted download can be
/// resumed, and the finished file is verified against its digest before it is moved into place.
/// </summary>
/// <remarks>
/// <para>
/// This is the port of Ollama's <c>blobDownload</c>. The shape of the work is the same: Prepare splits
/// or resumes, Run resolves the direct URL and downloads the ranges, and each range is retried on its
/// own. Three things are deliberately different, all of them noted where they happen: the per-part
/// files are replaced by one sidecar document, the back-off has no random jitter so that behaviour is
/// reproducible, and byte counts are never rolled back because every byte counted has already been
/// written to the partial file and recorded in the sidecar.
/// </para>
/// <para>
/// A download is used once. Create a new instance for each blob.
/// </para>
/// </remarks>
internal sealed class BlobDownload
{
    /// <summary>The size of the buffer bytes are copied through.</summary>
    private const int CopyBufferSize = 64 * 1024;

    /// <summary>The longest a retry ever waits, however many attempts have failed.</summary>
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

    /// <summary>The longest the back-off between direct URL attempts ever waits, as in Ollama.</summary>
    private static readonly TimeSpan MaxDirectUrlBackoff = TimeSpan.FromSeconds(10);

    private readonly RegistryClient _client;
    private readonly ModelStoreOptions _options;
    private readonly ModelName _name;
    private readonly string _digest;
    private readonly long _expectedSize;
    private readonly string _destinationPath;
    private readonly string _partialDataPath;
    private readonly string _partialStatePath;
    private readonly TimeSpan _retryBaseDelay;
    private readonly object _stateLock = new object();
    private readonly List<BlobDownloadPart> _parts = new List<BlobDownloadPart>();
    private long _completed;
    private long _total;
    private bool _prepared;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlobDownload"/> class.
    /// </summary>
    /// <param name="client">The registry client the requests go through.</param>
    /// <param name="options">The store options that set the part sizes, the retries and the timeouts.</param>
    /// <param name="name">The model name the blob belongs to.</param>
    /// <param name="digest">The blob digest in <c>sha256:&lt;hex&gt;</c> form.</param>
    /// <param name="expectedSize">The blob size in bytes, or -1 to ask the registry for it.</param>
    /// <param name="destinationPath">Where the finished, verified file is moved to.</param>
    /// <param name="partialDataPath">The file the bytes are written to while the download runs.</param>
    /// <param name="partialStatePath">The JSON sidecar that records the byte ranges and their progress.</param>
    /// <param name="retryBaseDelay">The base delay of the back-off between retries of a failed range.</param>
    public BlobDownload(
        RegistryClient client,
        ModelStoreOptions options,
        ModelName name,
        string digest,
        long expectedSize,
        string destinationPath,
        string partialDataPath,
        string partialStatePath,
        TimeSpan retryBaseDelay)
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _client = client;
        _options = options;
        _name = name;
        _digest = NormalizeDigest(digest);
        _expectedSize = expectedSize;
        _destinationPath = destinationPath;
        _partialDataPath = partialDataPath;
        _partialStatePath = partialStatePath;
        _retryBaseDelay = retryBaseDelay > TimeSpan.Zero ? retryBaseDelay : TimeSpan.FromSeconds(1);
    }

    /// <summary>The blob size in bytes, known once <see cref="PrepareAsync"/> has run.</summary>
    public long Total
    {
        get { return Interlocked.Read(ref _total); }
    }

    /// <summary>How many bytes of the blob are on disk, including bytes resumed from an earlier run.</summary>
    public long Completed
    {
        get { return Interlocked.Read(ref _completed); }
    }

    /// <summary>The byte ranges the blob is split into, in offset order.</summary>
    public IReadOnlyList<BlobDownloadPart> Parts
    {
        get { return _parts; }
    }

    /// <summary>
    /// Works out the byte ranges: the sidecar of an interrupted download is resumed when it is
    /// consistent with this blob, and otherwise the blob is split afresh and a new sidecar written.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the size lookup.</param>
    /// <returns>A task that completes when the parts are known.</returns>
    /// <remarks>
    /// The split is Ollama's: the blob size divided by the number of concurrent parts, clamped to
    /// <see cref="ModelStoreOptions.MinPartSize"/> and <see cref="ModelStoreOptions.MaxPartSize"/>,
    /// with the last part shortened to whatever is left.
    /// </remarks>
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (_prepared)
        {
            return;
        }

        BlobDownloadState state = TryReadState();
        if (state != null && IsUsableState(state))
        {
            _total = state.Total;
            foreach (BlobDownloadPart part in state.Parts)
            {
                _parts.Add(part);
                _completed += part.Completed;
            }
            _prepared = true;
            return;
        }

        DeleteFile(_partialStatePath);
        DeleteFile(_partialDataPath);

        _total = _expectedSize >= 0
            ? _expectedSize
            : await _client.GetBlobSizeAsync(_name, _digest, cancellationToken).ConfigureAwait(false);

        CreateParts();
        PersistState();
        _prepared = true;
    }

    /// <summary>
    /// Runs the download to completion: resolve the direct URL, fetch every incomplete range, verify
    /// the result and move it into place.
    /// </summary>
    /// <param name="progress">
    /// Called with the completed and total byte counts while the download runs, at most once per
    /// <see cref="ModelStoreOptions.ProgressInterval"/> and once more when it finishes.
    /// <see langword="null"/> asks for no reports.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels the download, leaving the partial file and its sidecar in place.
    /// </param>
    /// <returns>A task that completes when the file is at the destination path.</returns>
    /// <exception cref="ModelNotFoundException">The registry answered 404 for the blob.</exception>
    /// <exception cref="DigestMismatchException">The downloaded bytes do not hash to the digest.</exception>
    /// <exception cref="RegistryException">A byte range failed every retry, or the registry answered an error.</exception>
    public async Task RunAsync(Action<long, long> progress, CancellationToken cancellationToken = default)
    {
        if (File.Exists(_destinationPath))
        {
            // Cache hit: the blob is already in the store, so there is nothing to fetch.
            long existingSize = new FileInfo(_destinationPath).Length;
            ReportProgress(progress, existingSize, existingSize);
            return;
        }

        await PrepareAsync(cancellationToken).ConfigureAwait(false);
        EnsurePartialFile();

        using var progressCancellation = new CancellationTokenSource();
        Task progressTask = progress == null
            ? Task.CompletedTask
            : ReportProgressPeriodicallyAsync(progress, progressCancellation.Token);

        try
        {
            (Uri Url, bool SendAuthorization) direct = await ResolveDirectUrlAsync(cancellationToken).ConfigureAwait(false);
            await DownloadPartsAsync(direct.Url, direct.SendAuthorization, cancellationToken).ConfigureAwait(false);
            await FinishAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            progressCancellation.Cancel();
            await progressTask.ConfigureAwait(false);
        }

        ReportProgress(progress, Total, Total);
    }

    /// <summary>
    /// Finds the address the bytes are actually fetched from. A registry usually answers the blob
    /// address with a redirect to a content delivery host, and that redirected address carries its own
    /// signature, so it is used without the <c>Authorization</c> header. A 200 means the registry
    /// serves the blob itself, and then the blob address is used with the header.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>The address to fetch from and whether it may carry the <c>Authorization</c> header.</returns>
    /// <exception cref="ModelNotFoundException">The registry answered 404 for the blob.</exception>
    /// <exception cref="RegistryException">Every attempt within the time budget failed.</exception>
    private async Task<(Uri Url, bool SendAuthorization)> ResolveDirectUrlAsync(CancellationToken cancellationToken)
    {
        Uri blobUri = _client.GetBlobUri(_name, _digest);

        // Ollama gives this step 30 seconds. Here the budget is thirty times the retry base delay,
        // which is the same 30 seconds at the default one-second delay and stays proportional when a
        // caller shortens the delay.
        TimeSpan budget = TimeSpan.FromMilliseconds(Math.Max(1d, _retryBaseDelay.TotalMilliseconds * 30d));
        DateTime deadline = DateTime.UtcNow + budget;

        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpResponseMessage response = await _client.SendWithChallengeAsync(
                    HttpMethod.Get, blobUri, null, null, true, true, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new ModelNotFoundException(
                        _name.ToString(), "blob " + _digest + " of model " + _name + " not found on " + _name.Host);
                }

                if (RegistryClient.IsRedirect(response.StatusCode) && response.Headers.Location != null)
                {
                    Uri location = new Uri(blobUri, response.Headers.Location);
                    bool sameHost = string.Equals(location.Host, blobUri.Host, StringComparison.OrdinalIgnoreCase);
                    return (location, sameHost);
                }

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return (blobUri, true);
                }

                string body = await RegistryClient.ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                throw new RegistryException(
                    "unexpected status code "
                        + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                        + " for blob " + _digest,
                    response.StatusCode,
                    body);
            }
            catch (RegistryException exception)
            {
                if (exception.StatusCode == HttpStatusCode.Unauthorized || DateTime.UtcNow >= deadline)
                {
                    throw;
                }
                await Task.Delay(ComputeDirectUrlBackoff(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Fetches every range that is not already complete, at most
    /// <see cref="ModelStoreOptions.MaxConcurrentParts"/> of them at a time.
    /// </summary>
    /// <param name="url">The address to fetch from.</param>
    /// <param name="sendAuthorization">Whether that address may carry the <c>Authorization</c> header.</param>
    /// <param name="cancellationToken">A token that cancels the download.</param>
    /// <returns>A task that completes when every range is on disk.</returns>
    private async Task DownloadPartsAsync(Uri url, bool sendAuthorization, CancellationToken cancellationToken)
    {
        using var limiter = new SemaphoreSlim(GetConcurrency());
        var running = new List<Task>();

        foreach (BlobDownloadPart part in _parts)
        {
            if (part.Completed >= part.Size)
            {
                continue;
            }
            running.Add(DownloadPartWithRetriesAsync(part, url, sendAuthorization, limiter, cancellationToken));
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches one range, retrying it as Ollama does: a stall costs the range its attempt but not one
    /// of its retries, cancellation and an out-of-space error end the download at once, and anything
    /// else waits out an exponential back-off and tries again.
    /// </summary>
    /// <param name="part">The range to fetch.</param>
    /// <param name="url">The address to fetch from.</param>
    /// <param name="sendAuthorization">Whether that address may carry the <c>Authorization</c> header.</param>
    /// <param name="limiter">The semaphore that caps how many ranges are in flight.</param>
    /// <param name="cancellationToken">A token that cancels the download.</param>
    /// <returns>A task that completes when the range is on disk.</returns>
    /// <exception cref="RegistryException">Every retry failed.</exception>
    private async Task DownloadPartWithRetriesAsync(
        BlobDownloadPart part, Uri url, bool sendAuthorization, SemaphoreSlim limiter, CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int maxAttempts = Math.Max(1, _options.MaxRetries);
            Exception lastError = null;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (await TryDownloadPartAsync(part, url, sendAuthorization, cancellationToken).ConfigureAwait(false))
                    {
                        return;
                    }

                    // The range stalled. Ollama does not count a stall against the retry budget.
                    attempt--;
                    continue;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (IOException exception) when (IsOutOfSpace(exception))
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                }

                await Task.Delay(ComputeRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
            }

            string reason = lastError == null ? "no attempt succeeded" : lastError.Message;
            throw new RegistryException(
                "max retries exceeded downloading part "
                    + part.N.ToString(CultureInfo.InvariantCulture)
                    + " of blob " + _digest + ": " + reason,
                lastError);
        }
        finally
        {
            limiter.Release();
        }
    }

    /// <summary>
    /// Makes one attempt at a range: request the bytes that are still missing, write them at the right
    /// offset of the partial file, and record the progress in the sidecar however the attempt ends.
    /// </summary>
    /// <param name="part">The range to fetch.</param>
    /// <param name="url">The address to fetch from.</param>
    /// <param name="sendAuthorization">Whether that address may carry the <c>Authorization</c> header.</param>
    /// <param name="cancellationToken">A token that cancels the download.</param>
    /// <returns>
    /// <see langword="true"/> when the range is complete, <see langword="false"/> when the attempt was
    /// abandoned because no bytes arrived for <see cref="ModelStoreOptions.StallTimeout"/>.
    /// </returns>
    private async Task<bool> TryDownloadPartAsync(
        BlobDownloadPart part, Uri url, bool sendAuthorization, CancellationToken cancellationToken)
    {
        long start = part.StartsAt;
        long stop = part.StopsAt;
        if (start >= stop)
        {
            return true;
        }

        // The attempt is cancelled when no bytes have arrived for the stall timeout; the timer is put
        // back to the full timeout every time bytes do arrive. This is the same watchdog Ollama runs
        // beside the transfer, written as a deadline on the attempt's own token.
        TimeSpan stallTimeout = _options.StallTimeout > TimeSpan.Zero ? _options.StallTimeout : Timeout.InfiniteTimeSpan;
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ResetStallTimer(attemptCancellation, stallTimeout);

        try
        {
            string range = "bytes="
                + start.ToString(CultureInfo.InvariantCulture)
                + "-"
                + (stop - 1).ToString(CultureInfo.InvariantCulture);

            using HttpResponseMessage response = await _client.SendWithChallengeAsync(
                HttpMethod.Get, url, null, range, sendAuthorization, false, attemptCancellation.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                // A 200 means the server ignored the Range header. That is only usable when this range
                // is the whole blob and nothing of it has been fetched yet.
                if (_parts.Count != 1 || start != 0)
                {
                    throw new RegistryException(
                        "the server ignored the Range header for part "
                            + part.N.ToString(CultureInfo.InvariantCulture) + " of blob " + _digest,
                        response.StatusCode,
                        null);
                }
            }
            else if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                await RegistryClient.ThrowForErrorStatusAsync(
                    response,
                    _name,
                    "blob " + _digest + " of model " + _name + " not found on " + _name.Host,
                    attemptCancellation.Token).ConfigureAwait(false);

                throw new RegistryException(
                    "unexpected status code "
                        + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                        + " for part " + part.N.ToString(CultureInfo.InvariantCulture) + " of blob " + _digest,
                    response.StatusCode,
                    null);
            }

            using Stream source = await response.Content
                .ReadAsStreamAsync(attemptCancellation.Token).ConfigureAwait(false);
            using var destination = new FileStream(
                _partialDataPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, CopyBufferSize, true);
            destination.Seek(start, SeekOrigin.Begin);

            byte[] buffer = new byte[CopyBufferSize];
            long remaining = stop - start;
            while (remaining > 0)
            {
                int wanted = (int)Math.Min(remaining, buffer.Length);
                int read = await source
                    .ReadAsync(buffer.AsMemory(0, wanted), attemptCancellation.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), attemptCancellation.Token).ConfigureAwait(false);
                remaining -= read;
                part.Completed += read;
                Interlocked.Add(ref _completed, read);
                ResetStallTimer(attemptCancellation, stallTimeout);
            }

            await destination.FlushAsync(attemptCancellation.Token).ConfigureAwait(false);

            if (remaining > 0)
            {
                throw new RegistryException(
                    "the connection ended with "
                        + remaining.ToString(CultureInfo.InvariantCulture)
                        + " bytes of part " + part.N.ToString(CultureInfo.InvariantCulture)
                        + " of blob " + _digest + " still missing",
                    null,
                    null);
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            PersistState();
        }
    }

    /// <summary>
    /// Verifies the partial file against the digest, drops the sidecar and moves the file to the
    /// destination.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the verification.</param>
    /// <returns>A task that completes when the file is at the destination path.</returns>
    /// <exception cref="DigestMismatchException">
    /// The bytes do not hash to the digest. The partial file and its sidecar have been deleted, so the
    /// next attempt starts over.
    /// </exception>
    private async Task FinishAsync(CancellationToken cancellationToken)
    {
        string actualDigest = await ComputeFileDigestAsync(_partialDataPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actualDigest, _digest, StringComparison.OrdinalIgnoreCase))
        {
            DeleteFile(_partialDataPath);
            DeleteFile(_partialStatePath);
            throw new DigestMismatchException(_digest, actualDigest);
        }

        DeleteFile(_partialStatePath);

        string destinationDirectory = Path.GetDirectoryName(_destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Move(_partialDataPath, _destinationPath, true);
    }

    /// <summary>
    /// Splits the blob into byte ranges, exactly as Ollama's <c>Prepare</c> does.
    /// </summary>
    private void CreateParts()
    {
        _parts.Clear();
        _completed = 0;

        long total = _total;
        if (total <= 0)
        {
            return;
        }

        long size = total / GetConcurrency();
        if (size < _options.MinPartSize)
        {
            size = _options.MinPartSize;
        }
        else if (size > _options.MaxPartSize)
        {
            size = _options.MaxPartSize;
        }
        if (size <= 0)
        {
            size = total;
        }

        long offset = 0;
        while (offset < total)
        {
            if (offset + size > total)
            {
                size = total - offset;
            }

            _parts.Add(new BlobDownloadPart
            {
                N = _parts.Count,
                Offset = offset,
                Size = size,
                Completed = 0
            });

            offset += size;
        }
    }

    /// <summary>
    /// How many ranges are fetched at a time. A configured value of zero or less means one.
    /// </summary>
    /// <returns>The concurrency, at least one.</returns>
    private int GetConcurrency()
    {
        return _options.MaxConcurrentParts > 0 ? _options.MaxConcurrentParts : 1;
    }

    /// <summary>
    /// Reads the sidecar of an earlier run, answering <see langword="null"/> when there is none or it
    /// cannot be read.
    /// </summary>
    /// <returns>The recorded state, or <see langword="null"/>.</returns>
    private BlobDownloadState TryReadState()
    {
        try
        {
            if (!File.Exists(_partialStatePath))
            {
                return null;
            }
            byte[] bytes = File.ReadAllBytes(_partialStatePath);
            return ModelManagerJson.Deserialize<BlobDownloadState>(bytes);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a recorded state describes this very download and still matches the partial file on
    /// disk. Anything that does not add up means the download starts over.
    /// </summary>
    /// <param name="state">The recorded state.</param>
    /// <returns><see langword="true"/> when the state can be resumed.</returns>
    private bool IsUsableState(BlobDownloadState state)
    {
        if (!string.Equals(state.Digest, _digest, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (_expectedSize >= 0 && state.Total != _expectedSize)
        {
            return false;
        }
        if (state.Parts == null)
        {
            return false;
        }
        if (!File.Exists(_partialDataPath) || new FileInfo(_partialDataPath).Length != state.Total)
        {
            return false;
        }

        long offset = 0;
        foreach (BlobDownloadPart part in state.Parts)
        {
            if (part == null || part.Offset != offset || part.Size <= 0)
            {
                return false;
            }
            if (part.Completed < 0 || part.Completed > part.Size)
            {
                return false;
            }
            offset += part.Size;
        }

        return offset == state.Total;
    }

    /// <summary>
    /// Writes the sidecar. It is written after every attempt at a range, so whatever is on disk is
    /// always described by what the sidecar says.
    /// </summary>
    private void PersistState()
    {
        lock (_stateLock)
        {
            var state = new BlobDownloadState
            {
                Digest = _digest,
                Total = _total,
                Parts = new List<BlobDownloadPart>(_parts.Count)
            };

            foreach (BlobDownloadPart part in _parts)
            {
                state.Parts.Add(new BlobDownloadPart
                {
                    N = part.N,
                    Offset = part.Offset,
                    Size = part.Size,
                    Completed = part.Completed
                });
            }

            string directory = Path.GetDirectoryName(_partialStatePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(_partialStatePath, ModelManagerJson.SerializeLikeGo(state));
        }
    }

    /// <summary>
    /// Creates the partial file when it is not there yet and gives it the full length of the blob, so
    /// that every range can be written at its own offset.
    /// </summary>
    private void EnsurePartialFile()
    {
        string directory = Path.GetDirectoryName(_partialDataPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var file = new FileStream(_partialDataPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        if (file.Length != _total)
        {
            file.SetLength(_total);
        }
    }

    /// <summary>
    /// Reports progress until the token is cancelled.
    /// </summary>
    /// <param name="progress">The callback to invoke.</param>
    /// <param name="cancellationToken">The token that ends the reporting.</param>
    /// <returns>A task that completes when reporting ends.</returns>
    private async Task ReportProgressPeriodicallyAsync(Action<long, long> progress, CancellationToken cancellationToken)
    {
        TimeSpan interval = _options.ProgressInterval > TimeSpan.Zero
            ? _options.ProgressInterval
            : TimeSpan.FromMilliseconds(100);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                ReportProgress(progress, Completed, Total);
            }
        }
        catch (OperationCanceledException)
        {
            // The download finished, failed or was cancelled; reporting simply stops.
        }
    }

    /// <summary>
    /// Invokes the progress callback, ignoring anything it throws so that a faulty callback cannot
    /// fail a download.
    /// </summary>
    /// <param name="progress">The callback, which may be <see langword="null"/>.</param>
    /// <param name="completed">The bytes downloaded so far.</param>
    /// <param name="total">The total size of the blob.</param>
    private static void ReportProgress(Action<long, long> progress, long completed, long total)
    {
        if (progress == null)
        {
            return;
        }
        progress(completed, total);
    }

    /// <summary>
    /// Puts the stall watchdog back to the full timeout.
    /// </summary>
    /// <param name="source">The attempt's cancellation source.</param>
    /// <param name="timeout">The stall timeout, or <see cref="Timeout.InfiniteTimeSpan"/> to switch the watchdog off.</param>
    private static void ResetStallTimer(CancellationTokenSource source, TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }
        try
        {
            source.CancelAfter(timeout);
        }
        catch (ObjectDisposedException)
        {
            // The attempt is already over.
        }
    }

    /// <summary>
    /// The delay before retry <paramref name="attempt"/> of a range: the base delay doubled once per
    /// failed attempt, capped at one minute. Ollama waits two seconds to the power of the attempt
    /// number; with the default one-second base delay this is the same sequence.
    /// </summary>
    /// <param name="attempt">The zero-based number of the attempt that just failed.</param>
    /// <returns>The delay.</returns>
    private TimeSpan ComputeRetryDelay(int attempt)
    {
        double milliseconds = _retryBaseDelay.TotalMilliseconds * Math.Pow(2d, Math.Max(0, attempt));
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, MaxRetryDelay.TotalMilliseconds));
    }

    /// <summary>
    /// The delay before the next attempt at resolving the direct URL: the attempt number squared times
    /// a hundredth of the retry base delay, capped at ten seconds. With the default one-second base
    /// delay that is Ollama's own ten milliseconds times the attempt number squared, without the
    /// random jitter, which keeps the behaviour reproducible.
    /// </summary>
    /// <param name="attempt">The one-based number of the attempt that just failed.</param>
    /// <returns>The delay.</returns>
    private TimeSpan ComputeDirectUrlBackoff(int attempt)
    {
        double milliseconds = attempt * (double)attempt * (_retryBaseDelay.TotalMilliseconds / 100d);
        return TimeSpan.FromMilliseconds(Math.Max(1d, Math.Min(milliseconds, MaxDirectUrlBackoff.TotalMilliseconds)));
    }

    /// <summary>
    /// Hashes a file with SHA-256 and formats the result the way a manifest digest is written.
    /// </summary>
    /// <param name="path">The file to hash.</param>
    /// <param name="cancellationToken">A token that cancels the hashing.</param>
    /// <returns>The digest in <c>sha256:&lt;hex&gt;</c> form.</returns>
    private static async Task<string> ComputeFileDigestAsync(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, true);
        using var algorithm = SHA256.Create();
        byte[] hash = await algorithm.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Puts a digest in the <c>sha256:&lt;hex&gt;</c> form the rest of this class compares against.
    /// </summary>
    /// <param name="digest">The digest, with or without the algorithm prefix.</param>
    /// <returns>The prefixed digest.</returns>
    private static string NormalizeDigest(string digest)
    {
        if (string.IsNullOrEmpty(digest))
        {
            throw new ArgumentException("A blob digest is required.", nameof(digest));
        }
        return digest.Contains(':') ? digest : "sha256:" + digest;
    }

    /// <summary>
    /// Whether an I/O error is the disk running out of space, which is never worth retrying.
    /// </summary>
    /// <param name="exception">The error to inspect.</param>
    /// <returns><see langword="true"/> when the device is full.</returns>
    private static bool IsOutOfSpace(IOException exception)
    {
        const int unixNoSpaceLeft = 28;
        const int windowsDiskFull = unchecked((int)0x80070070);
        const int windowsHandleDiskFull = unchecked((int)0x80070027);

        if (exception.HResult == unixNoSpaceLeft
            || exception.HResult == windowsDiskFull
            || exception.HResult == windowsHandleDiskFull)
        {
            return true;
        }

        string message = exception.Message ?? string.Empty;
        return message.Contains("No space left on device", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not enough space on the disk", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deletes a file, ignoring the case where it is not there.
    /// </summary>
    /// <param name="path">The file to delete.</param>
    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Leaving a stray partial file behind is better than failing the download over it.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
