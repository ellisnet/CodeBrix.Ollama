using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the download engine driven by an address rather than a registry blob: the ranged parts, the
/// sidecar a second run resumes from, the redirect to a content delivery host and the token that never
/// follows it, verification against a stated SHA-256 or a stated MD5, the SHA-256 that is computed even
/// when the source states nothing, and the sizes and statuses that fail a download.
/// </summary>
public sealed class FileDownloadTests : IDisposable
{
    private const string Repository = "skytnt/midi-model-tv2o-medium";
    private const string Commit = "0f8f265d4330f4e46527ac2313200254c5757f5f";
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(10);

    private readonly TempStoreDirectory _store;

    /// <summary>
    /// Creates the temporary store this test class works inside.
    /// </summary>
    public FileDownloadTests()
    {
        _store = new TempStoreDirectory();
    }

    /// <summary>
    /// Removes the temporary store.
    /// </summary>
    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task DownloadFileAsync_writes_the_blob_the_stated_sha256_names()
    {
        //Arrange
        byte[] data = CreateData(10000);
        using FakeHubHandler handler = CreateHub(data);
        BundleFile file = await ListFileAsync(handler, "big.bin");
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);

        //Act
        BlobDownloadResult result = await downloader.DownloadFileAsync(
            file, false, null, TestContext.Current.CancellationToken);

        //Assert
        result.Digest.Should().Be(FakeHubHandler.ComputeDigest(data));
        result.Size.Should().Be(10000L);
        result.FilePath.Should().Be(_store.Paths.GetBlobPath(result.Digest));
        (await File.ReadAllBytesAsync(result.FilePath, TestContext.Current.CancellationToken)).Should().Equal(data);
    }

    [Fact]
    public async Task DownloadFileAsync_splits_the_file_into_ranged_parts()
    {
        //Arrange
        byte[] data = CreateData(10000);
        using FakeHubHandler handler = CreateHub(data);
        BundleFile file = await ListFileAsync(handler, "big.bin");
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);

        //Act
        await downloader.DownloadFileAsync(file, false, null, TestContext.Current.CancellationToken);

        //Assert
        SortedRangeHeaders(handler).Should().Equal(
            "bytes=0-2499", "bytes=2500-4999", "bytes=5000-7499", "bytes=7500-9999");
        handler.RequestCountForHost("cdn.fake").Should().Be(4);
    }

    [Fact]
    public async Task DownloadFileAsync_after_an_interrupted_run_resumes_from_the_sidecar()
    {
        //Arrange
        byte[] data = CreateData(1000);
        using FakeHubHandler handler = CreateHub(data);
        BundleFile file = await ListFileAsync(handler, "big.bin");
        handler.DropConnectionAfter(0, 1, 400);
        ModelStoreOptions first = CreateOptions(handler);
        first.MaxRetries = 1;
        string partialData = _store.Paths.GetPartialDataPath(file.Url);
        string partialState = _store.Paths.GetPartialStatePath(file.Url);

        //Act
        using (var interruptedDownloader = new FileDownloader(first, _store.Paths, RetryBaseDelay))
        {
            Func<Task> interrupted = () => interruptedDownloader.DownloadFileAsync(
                file, false, null, TestContext.Current.CancellationToken);
            await interrupted.Should().ThrowAsync<RegistryException>();
        }

        BlobDownloadState state = ReadState(partialState);
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);
        BlobDownloadResult result = await downloader.DownloadFileAsync(
            file, false, null, TestContext.Current.CancellationToken);

        //Assert
        state.Digest.Should().Be(ModelStorePaths.GetDownloadKey(file.Url));
        state.Total.Should().Be(1000L);
        state.Parts[0].Completed.Should().Be(400L);
        handler.RangeHeaders().Should().Equal("bytes=0-999", "bytes=400-999");
        (await File.ReadAllBytesAsync(result.FilePath, TestContext.Current.CancellationToken)).Should().Equal(data);
        File.Exists(partialData).Should().BeFalse();
        File.Exists(partialState).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadFileAsync_with_a_stalled_part_retries_that_part()
    {
        //Arrange
        byte[] data = CreateData(10000);
        using FakeHubHandler handler = CreateHub(data);
        BundleFile file = await ListFileAsync(handler, "big.bin");
        handler.StallAfter(2500, 1, 0);
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);

        //Act
        BlobDownloadResult result = await downloader.DownloadFileAsync(
            file, false, null, TestContext.Current.CancellationToken);

        //Assert
        (await File.ReadAllBytesAsync(result.FilePath, TestContext.Current.CancellationToken)).Should().Equal(data);
        handler.RangeHeaders().Count(value => value == "bytes=2500-4999").Should().Be(2);
    }

    [Fact]
    public async Task DownloadFileAsync_follows_the_redirect_and_keeps_the_token_off_the_other_host()
    {
        //Arrange
        byte[] data = CreateData(10000);
        using FakeHubHandler handler = CreateHub(data);
        handler.RequireAuthorization = true;
        BundleFile file = await ListFileAsync(handler, "big.bin", "hf-token");
        ModelStoreOptions options = CreateOptions(handler);
        options.BearerToken = "hf-token";
        using var downloader = new FileDownloader(options, _store.Paths, RetryBaseDelay);

        //Act
        BlobDownloadResult result = await downloader.DownloadFileAsync(
            file, false, null, TestContext.Current.CancellationToken);

        //Assert
        (await File.ReadAllBytesAsync(result.FilePath, TestContext.Current.CancellationToken)).Should().Equal(data);
        handler.AnyAuthorizationSentTo("huggingface.co").Should().BeTrue();
        handler.AnyAuthorizationSentTo("cdn.fake").Should().BeFalse();
    }

    [Fact]
    public async Task DownloadFileAsync_verifies_an_md5_when_that_is_the_only_hash_stated()
    {
        //Arrange
        byte[] data = GoogleCloudStorageListingTests.Bytes("fur elise");
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            "magentadata", "models/music_transformer/", handler, TestContext.Current.CancellationToken);
        BundleFile file = files.First(candidate => candidate.Path == "primers/fur_elise.mid");
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);

        //Act
        BlobDownloadResult result = await downloader.DownloadFileAsync(
            file, false, null, TestContext.Current.CancellationToken);

        //Assert
        file.Sha256.Should().BeNull();
        file.Md5.Should().NotBeNull();
        result.Md5.Should().Be(file.Md5);
        result.Digest.Should().Be(FakeHubHandler.ComputeDigest(data));
        (await File.ReadAllBytesAsync(result.FilePath, TestContext.Current.CancellationToken)).Should().Equal(data);
    }

    [Fact]
    public async Task DownloadFileAsync_with_an_md5_that_does_not_match_fails_and_removes_the_partial_files()
    {
        //Arrange
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        string url = GoogleCloudStorageListing.BuildObjectUrl(
            "magentadata", "models/music_transformer/primers/fur_elise.mid");
        var file = new BundleFile(
            "primers/fur_elise.mid", url, GoogleCloudStorageListingTests.Bytes("fur elise").Length,
            null, new string('a', 32), null);
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);

        //Act
        Func<Task> act = () => downloader.DownloadFileAsync(file, false, null, TestContext.Current.CancellationToken);

        //Assert
        DigestMismatchException exception = (await act.Should().ThrowAsync<DigestMismatchException>()).Which;
        exception.ExpectedDigest.Should().Be("md5:" + new string('a', 32));
        exception.ActualDigest.Should().StartWith("md5:");
        File.Exists(_store.Paths.GetPartialDataPath(file.Url)).Should().BeFalse();
        File.Exists(_store.Paths.GetPartialStatePath(file.Url)).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadFileAsync_computes_the_sha256_when_the_source_stated_nothing()
    {
        //Arrange
        byte[] data = CreateData(3000);
        using FakeHubHandler handler = CreateHub(data);
        handler.AddFile(Repository, Commit, "plain.bin", data, false);
        BundleFile file = await ListFileAsync(handler, "plain.bin");
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);

        //Act
        BlobDownloadResult result = await downloader.DownloadFileAsync(
            file, false, null, TestContext.Current.CancellationToken);

        //Assert
        file.Sha256.Should().BeNull();
        file.Md5.Should().BeNull();
        result.Digest.Should().Be(FakeHubHandler.ComputeDigest(data));
        result.FilePath.Should().Be(_store.Paths.GetBlobPath(result.Digest));
        File.Exists(result.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadFileAsync_with_bytes_that_do_not_match_the_stated_sha256_fails()
    {
        //Arrange
        byte[] data = CreateData(2000);
        using FakeHubHandler handler = CreateHub(data);
        string promised = new string('b', 64);
        var file = new BundleFile(
            "big.bin",
            HuggingFaceHubSource.BuildFileUrl(Repository, Commit, "big.bin"),
            data.Length,
            promised,
            null,
            null);
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);

        //Act
        Func<Task> act = () => downloader.DownloadFileAsync(file, false, null, TestContext.Current.CancellationToken);

        //Assert
        DigestMismatchException exception = (await act.Should().ThrowAsync<DigestMismatchException>()).Which;
        exception.ExpectedDigest.Should().Be("sha256:" + promised);
        exception.ActualDigest.Should().Be(FakeHubHandler.ComputeDigest(data));
        File.Exists(_store.Paths.GetBlobPath("sha256:" + promised)).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadFileAsync_when_the_server_states_another_size_fails_before_it_fetches_anything()
    {
        //Arrange
        byte[] data = CreateData(2000);
        using FakeHubHandler handler = CreateHub(data);
        var file = new BundleFile("big.bin", HuggingFaceHubSource.BuildFileUrl(Repository, Commit, "big.bin"), 999);
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);

        //Act
        Func<Task> act = () => downloader.DownloadFileAsync(file, false, null, TestContext.Current.CancellationToken);

        //Assert
        RegistryException exception = (await act.Should().ThrowAsync<RegistryException>()).Which;
        exception.Message.Should().Contain("reports 2000 bytes");
        exception.Message.Should().Contain("999 were expected");
        handler.RequestCountForHost("cdn.fake").Should().Be(0);
    }

    [Fact]
    public async Task DownloadFileAsync_of_an_address_that_is_not_there_fails_without_retrying_it()
    {
        //Arrange
        byte[] data = CreateData(1000);
        using FakeHubHandler handler = CreateHub(data);
        var file = new BundleFile("gone.bin", HuggingFaceHubSource.BuildFileUrl(Repository, Commit, "gone.bin"), 10);
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);

        //Act
        Func<Task> act = () => downloader.DownloadFileAsync(file, false, null, TestContext.Current.CancellationToken);

        //Assert
        RegistryException exception = (await act.Should().ThrowAsync<RegistryException>()).Which;
        exception.Message.Should().Contain("does not have");
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task DownloadFileAsync_with_the_file_already_in_the_store_makes_no_requests()
    {
        //Arrange
        byte[] data = CreateData(500);
        using FakeHubHandler handler = CreateHub(data);
        BundleFile file = await ListFileAsync(handler, "big.bin");
        int listingRequests = handler.Requests.Count;
        string blobPath = _store.Paths.GetBlobPath("sha256:" + file.Sha256);
        Directory.CreateDirectory(Path.GetDirectoryName(blobPath));
        await File.WriteAllBytesAsync(blobPath, data, TestContext.Current.CancellationToken);
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);
        long reported = -1;

        //Act
        BlobDownloadResult result = await downloader.DownloadFileAsync(
            file, false, (completed, total) => reported = completed, TestContext.Current.CancellationToken);

        //Assert
        result.Existed.Should().BeTrue();
        result.FilePath.Should().Be(blobPath);
        reported.Should().Be(500L);
        handler.Requests.Should().HaveCount(listingRequests);
    }

    [Fact]
    public async Task DownloadFileAsync_reports_progress_that_only_grows_and_ends_at_the_total()
    {
        //Arrange
        byte[] data = CreateData(10000);
        using FakeHubHandler handler = CreateHub(data);
        BundleFile file = await ListFileAsync(handler, "big.bin");
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);
        var reports = new List<(long Completed, long Total)>();
        var reportLock = new object();

        //Act
        await downloader.DownloadFileAsync(
            file,
            false,
            (completed, total) =>
            {
                lock (reportLock)
                {
                    reports.Add((completed, total));
                }
            },
            TestContext.Current.CancellationToken);

        //Assert
        reports.Count.Should().BeGreaterThan(0);
        reports[reports.Count - 1].Completed.Should().Be(10000L);
        reports[reports.Count - 1].Total.Should().Be(10000L);
        IsMonotonic(reports).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadFileAsync_that_must_have_a_hash_refuses_a_file_whose_source_stated_none()
    {
        //Arrange
        byte[] data = CreateData(100);
        using FakeHubHandler handler = CreateHub(data);
        handler.AddFile(Repository, Commit, "plain.bin", data, false);
        BundleFile file = await ListFileAsync(handler, "plain.bin");
        using var downloader = new FileDownloader(CreateOptions(handler), _store.Paths, RetryBaseDelay);

        //Act
        Func<Task> act = () => downloader.DownloadFileAsync(file, true, null, TestContext.Current.CancellationToken);

        //Assert
        ModelManagerException exception = (await act.Should().ThrowAsync<ModelManagerException>()).Which;
        exception.Message.Should().Contain("plain.bin");
        exception.Message.Should().Contain("no sha256 and no md5");
    }

    [Fact]
    public async Task DownloadFileAsync_when_cancelled_leaves_the_partial_files_in_place()
    {
        //Arrange
        byte[] data = CreateData(1000);
        using FakeHubHandler handler = CreateHub(data);
        BundleFile file = await ListFileAsync(handler, "big.bin");
        handler.StallAfter(0, 1, 100);
        ModelStoreOptions options = CreateOptions(handler);
        options.StallTimeout = TimeSpan.FromSeconds(30);
        using var downloader = new FileDownloader(options, _store.Paths, RetryBaseDelay);
        using var cancellation = new CancellationTokenSource();

        //Act
        Task download = downloader.DownloadFileAsync(file, false, null, cancellation.Token);
        await handler.StallStarted;
        await cancellation.CancelAsync();
        Func<Task> act = () => download;

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(_store.Paths.GetPartialDataPath(file.Url)).Should().BeTrue();
        ReadState(_store.Paths.GetPartialStatePath(file.Url)).Parts[0].Completed.Should().Be(100L);
    }

    [Fact]
    public void GetDownloadKey_is_the_same_for_one_address_and_different_for_another()
    {
        //Arrange
        var first = new Uri("https://huggingface.co/a/b/resolve/main/model.bin");
        var second = new Uri("https://huggingface.co/a/b/resolve/main/other.bin");

        //Act and assert
        ModelStorePaths.GetDownloadKey(first).Should().Be(ModelStorePaths.GetDownloadKey(first));
        ModelStorePaths.GetDownloadKey(first).Should().NotBe(ModelStorePaths.GetDownloadKey(second));
        ModelStorePaths.IsPartialSidecarFileName(
            Path.GetFileName(_store.Paths.GetPartialDataPath(first))).Should().BeTrue();
    }

    /// <summary>
    /// A fake Hub holding one large-file entry called <c>big.bin</c> with the bytes a test wants served.
    /// </summary>
    /// <param name="data">The bytes the file holds.</param>
    /// <returns>The handler. The caller disposes it.</returns>
    private static FakeHubHandler CreateHub(byte[] data)
    {
        var handler = new FakeHubHandler { LicenseTag = "apache-2.0" };
        handler.AddRepository(Repository, Commit);
        handler.AddFile(Repository, Commit, "big.bin", data, true);
        return handler;
    }

    /// <summary>
    /// Lists the fake Hub and returns one file of the listing, so that the download runs against exactly
    /// what the source would hand a pull.
    /// </summary>
    /// <param name="handler">The fake Hub.</param>
    /// <param name="path">The path to return.</param>
    /// <returns>The file.</returns>
    private static Task<BundleFile> ListFileAsync(FakeHubHandler handler, string path)
    {
        return ListFileAsync(handler, path, null);
    }

    /// <summary>
    /// Lists the fake Hub with an access token and returns one file of the listing.
    /// </summary>
    /// <param name="handler">The fake Hub.</param>
    /// <param name="path">The path to return.</param>
    /// <param name="token">The token, or <see langword="null"/> for an anonymous caller.</param>
    /// <returns>The file.</returns>
    private static async Task<BundleFile> ListFileAsync(FakeHubHandler handler, string path, string token)
    {
        using var source = new HuggingFaceHubSource(
            Repository, null, null, HuggingFaceHubSourceTests.CreateOptions(handler, token));
        BundleListing listing = await source.ListAsync(TestContext.Current.CancellationToken);
        return listing.Files.First(file => file.Path == path);
    }

    /// <summary>
    /// The options a download is made with.
    /// </summary>
    /// <param name="handler">The fake service.</param>
    /// <returns>The options.</returns>
    private static ModelStoreOptions CreateOptions(FakeHttpHandlerBase handler)
    {
        return new ModelStoreOptions
        {
            HttpMessageHandler = handler,
            MinPartSize = 1024,
            MaxPartSize = 4096,
            MaxConcurrentParts = 4,
            StallTimeout = TimeSpan.FromMilliseconds(300),
            ProgressInterval = TimeSpan.FromMilliseconds(20),
            MaxRetries = 3
        };
    }

    /// <summary>
    /// Reads a download sidecar.
    /// </summary>
    /// <param name="path">Where the sidecar is.</param>
    /// <returns>The recorded state.</returns>
    private static BlobDownloadState ReadState(string path)
    {
        return ModelManagerJson.Deserialize<BlobDownloadState>(File.ReadAllBytes(path));
    }

    /// <summary>
    /// The recorded <c>Range</c> headers ordered by the byte they start at, so that an assertion does
    /// not depend on which of the concurrent parts happened to arrive first.
    /// </summary>
    /// <param name="handler">The fake service.</param>
    /// <returns>The ordered header values.</returns>
    private static IReadOnlyList<string> SortedRangeHeaders(FakeHttpHandlerBase handler)
    {
        return handler.RangeHeaders()
            .OrderBy(value => long.Parse(
                value.Substring("bytes=".Length).Split('-')[0], CultureInfo.InvariantCulture))
            .ToArray();
    }

    /// <summary>
    /// Whether a run of progress reports never goes backwards.
    /// </summary>
    /// <param name="reports">The reports in the order they arrived.</param>
    /// <returns><see langword="true"/> when every report is at least as far along as the one before it.</returns>
    private static bool IsMonotonic(IReadOnlyList<(long Completed, long Total)> reports)
    {
        for (int index = 1; index < reports.Count; index++)
        {
            if (reports[index].Completed < reports[index - 1].Completed)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Builds a deterministic buffer of the given length.
    /// </summary>
    /// <param name="length">How many bytes to build.</param>
    /// <returns>The buffer.</returns>
    private static byte[] CreateData(int length)
    {
        byte[] data = new byte[length];
        for (int index = 0; index < length; index++)
        {
            data[index] = (byte)((index * 31 + 7) % 251);
        }
        return data;
    }
}
