using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Fetches the real model files the live tests run against, into a cache directory that survives between
/// runs, and proves that what is there is what was asked for.
/// </summary>
/// <remarks>
/// <para>
/// One of these files is twenty gigabytes, so the download is resumable: the bytes go into a
/// <c>.partial</c> file beside the target and a restart asks for a byte range beginning where that file
/// ends. A server that will not honour the range says so with 200 rather than 206, and the partial file is
/// then started again from nothing.
/// </para>
/// <para>
/// A file already in the cache is checked by size only. Hashing twenty gigabytes takes minutes and would
/// dominate every live run; the SHA-256 is verified once, immediately after a download, which is when a
/// truncated or corrupted transfer would show.
/// </para>
/// </remarks>
public static class ModelDownloader
{
    /// <summary>The directory downloaded models are cached in.</summary>
    public static string CacheDirectory
    {
        get
        {
            string configured = Environment.GetEnvironmentVariable(TestGates.ModelCacheDirectory);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodeBrix.Ollama",
                "test-models");
        }
    }

    /// <summary>The path a cached model file has, whether or not it is there yet.</summary>
    /// <param name="fileName">The file's name.</param>
    /// <returns>The full path.</returns>
    public static string PathOf(string fileName) => Path.Combine(CacheDirectory, fileName);

    /// <summary>
    /// Makes sure a model file is in the cache, downloading it when it is not, and returns its path.
    /// </summary>
    /// <param name="fileName">The file's name in the cache.</param>
    /// <param name="url">Where to download it from.</param>
    /// <param name="expectedSize">How many bytes the finished file has.</param>
    /// <param name="expectedSha256">The file's SHA-256 in lower-case hexadecimal, checked after a download.</param>
    /// <param name="cancellationToken">A token to cancel the download.</param>
    /// <returns>The path of the file.</returns>
    /// <exception cref="InvalidOperationException">The downloaded file is not the size or the hash promised.</exception>
    public static async Task<string> EnsureAsync(
        string fileName,
        string url,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        string directory = CacheDirectory;
        Directory.CreateDirectory(directory);

        string target = Path.Combine(directory, fileName);
        if (File.Exists(target))
        {
            long length = new FileInfo(target).Length;
            if (length == expectedSize) return target;

            throw new InvalidOperationException(
                $"'{target}' is {length} bytes and should be {expectedSize}. Delete it and run again.");
        }

        // Something else may already be fetching this file - the orchestrator downloads the large one - so a
        // growing partial file is waited on rather than raced.
        string partial = target + ".partial";
        if (await WaitForAnotherDownloadAsync(target, partial, cancellationToken).ConfigureAwait(false))
        {
            return target;
        }

        await DownloadAsync(url, partial, expectedSize, cancellationToken).ConfigureAwait(false);

        long downloaded = new FileInfo(partial).Length;
        if (downloaded != expectedSize)
        {
            throw new InvalidOperationException(
                $"The download of '{fileName}' ended at {downloaded} bytes and should be {expectedSize}.");
        }

        string digest = await Sha256Async(partial, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(digest, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            throw new InvalidOperationException(
                $"The download of '{fileName}' hashes to {digest} and should hash to {expectedSha256}.");
        }

        File.Move(partial, target, true);
        return target;
    }

    private static async Task<bool> WaitForAnotherDownloadAsync(
        string target, string partial, CancellationToken cancellationToken)
    {
        if (!File.Exists(partial)) return false;

        long previous = new FileInfo(partial).Length;
        DateTime deadline = DateTime.UtcNow.AddMinutes(90);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

            if (File.Exists(target)) return true;
            if (!File.Exists(partial)) return false;

            long current = new FileInfo(partial).Length;
            if (current == previous) return false;
            previous = current;
        }

        return File.Exists(target);
    }

    private static async Task DownloadAsync(
        string url, string partial, long expectedSize, CancellationToken cancellationToken)
    {
        using HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromHours(6),
        };

        long already = File.Exists(partial) ? new FileInfo(partial).Length : 0L;
        if (already >= expectedSize) already = 0L;

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        if (already > 0) request.Headers.Range = new RangeHeaderValue(already, null);

        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        bool resuming = already > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        FileMode mode = resuming ? FileMode.Append : FileMode.Create;

        using FileStream file = new FileStream(partial, mode, FileAccess.Write, FileShare.None, 1 << 20, true);
        using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await body.CopyToAsync(file, 1 << 20, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        using FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        using SHA256 hash = SHA256.Create();

        byte[] digest = await hash.ComputeHashAsync(file, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
