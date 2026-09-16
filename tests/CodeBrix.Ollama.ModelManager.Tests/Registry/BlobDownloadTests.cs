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
/// Covers the resumable parallel blob downloader against the in-memory registry: the split into byte
/// ranges, the reassembly, the sidecar that a second run resumes from, the stall watchdog, the
/// redirect to a content delivery host, digest verification, cancellation and progress reporting.
/// </summary>
public sealed class BlobDownloadTests : IDisposable
{
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(10);

    private readonly string _root;

    /// <summary>
    /// Creates the temporary directory this test class keeps its files in.
    /// </summary>
    public BlobDownloadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "codebrix-ollama-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test run over.
        }
    }

    [Fact]
    public async Task DownloadBlobAsync_splits_the_blob_into_parts_and_reassembles_it_exactly()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        byte[] data = CreateData(10000);
        string digest = handler.AddBlob(data);
        using var client = new RegistryClient(RegistryClientTests.CreateOptions(handler), RetryBaseDelay);

        //Act
        await client.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, data.Length,
            Destination, PartialData, PartialState, null, TestContext.Current.CancellationToken);

        //Assert
        byte[] written = await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken);
        written.Should().Equal(data);
        FakeRegistryHandler.ComputeDigest(written).Should().Be(digest);
        SortedRangeHeaders(handler).Should().Equal(
            "bytes=0-2499", "bytes=2500-4999", "bytes=5000-7499", "bytes=7500-9999");
        File.Exists(PartialData).Should().BeFalse();
        File.Exists(PartialState).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadBlobAsync_with_unknown_size_asks_the_registry_for_it()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        byte[] data = CreateData(3000);
        string digest = handler.AddBlob(data);
        using var client = new RegistryClient(RegistryClientTests.CreateOptions(handler), RetryBaseDelay);

        //Act
        await client.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, -1,
            Destination, PartialData, PartialState, null, TestContext.Current.CancellationToken);

        //Assert
        (await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken)).Should().Equal(data);
        handler.Requests[0].Method.Should().Be(System.Net.Http.HttpMethod.Head);
    }

    [Fact]
    public async Task DownloadBlobAsync_after_an_interrupted_run_resumes_from_the_sidecar()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        byte[] data = CreateData(1000);
        string digest = handler.AddBlob(data);
        handler.DropConnectionAfter(0, 1, 400);
        ModelStoreOptions firstOptions = RegistryClientTests.CreateOptions(handler);
        firstOptions.MaxRetries = 1;
        using var firstClient = new RegistryClient(firstOptions, RetryBaseDelay);

        //Act
        Func<Task> interrupted = () => firstClient.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, data.Length,
            Destination, PartialData, PartialState, null, TestContext.Current.CancellationToken);
        await interrupted.Should().ThrowAsync<RegistryException>();

        BlobDownloadState state = ReadState();
        using var secondClient = new RegistryClient(RegistryClientTests.CreateOptions(handler), RetryBaseDelay);
        await secondClient.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, data.Length,
            Destination, PartialData, PartialState, null, TestContext.Current.CancellationToken);

        //Assert
        state.Digest.Should().Be(digest);
        state.Total.Should().Be(1000L);
        state.Parts.Should().HaveCount(1);
        state.Parts[0].Completed.Should().Be(400L);
        handler.RangeHeaders().Should().Equal("bytes=0-999", "bytes=400-999");
        (await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken)).Should().Equal(data);
        File.Exists(PartialState).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadBlobAsync_with_a_stalled_part_retries_that_part()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        byte[] data = CreateData(10000);
        string digest = handler.AddBlob(data);
        handler.StallAfter(2500, 1, 0);
        using var client = new RegistryClient(RegistryClientTests.CreateOptions(handler), RetryBaseDelay);

        //Act
        await client.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, data.Length,
            Destination, PartialData, PartialState, null, TestContext.Current.CancellationToken);

        //Assert
        (await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken)).Should().Equal(data);
        handler.RangeHeaders().Count(value => value == "bytes=2500-4999").Should().Be(2);
    }

    [Fact]
    public async Task DownloadBlobAsync_with_a_redirect_to_another_host_keeps_the_token_off_that_host()
    {
        //Arrange
        using var handler = new FakeRegistryHandler
        {
            RequireAuthorization = true,
            ExpectedBearerToken = "secret",
            RedirectBlobsToCdn = true
        };
        byte[] data = CreateData(10000);
        string digest = handler.AddBlob(data);
        ModelStoreOptions options = RegistryClientTests.CreateOptions(handler);
        options.BearerToken = "secret";
        using var client = new RegistryClient(options, RetryBaseDelay);

        //Act
        await client.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, data.Length,
            Destination, PartialData, PartialState, null, TestContext.Current.CancellationToken);

        //Assert
        (await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken)).Should().Equal(data);
        handler.AnyAuthorizationSentTo("registry.test").Should().BeTrue();
        handler.AnyAuthorizationSentTo("cdn.test").Should().BeFalse();
        handler.RequestCountForHost("cdn.test").Should().Be(4);
    }

    [Fact]
    public async Task DownloadBlobAsync_with_bytes_that_do_not_match_the_digest_throws_and_removes_the_partial_files()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        byte[] promised = CreateData(2000);
        byte[] served = CreateData(2000);
        served[1999] ^= 0xFF;
        string digest = FakeRegistryHandler.ComputeDigest(promised);
        handler.AddBlob(digest, served);
        using var client = new RegistryClient(RegistryClientTests.CreateOptions(handler), RetryBaseDelay);

        //Act
        Func<Task> act = () => client.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, served.Length,
            Destination, PartialData, PartialState, null, TestContext.Current.CancellationToken);

        //Assert
        DigestMismatchException exception = (await act.Should().ThrowAsync<DigestMismatchException>()).Which;
        exception.ExpectedDigest.Should().Be(digest);
        exception.ActualDigest.Should().Be(FakeRegistryHandler.ComputeDigest(served));
        File.Exists(PartialData).Should().BeFalse();
        File.Exists(PartialState).Should().BeFalse();
        File.Exists(Destination).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadBlobAsync_when_cancelled_leaves_the_partial_files_in_place()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        byte[] data = CreateData(1000);
        string digest = handler.AddBlob(data);
        handler.StallAfter(0, 1, 100);
        ModelStoreOptions options = RegistryClientTests.CreateOptions(handler);
        options.StallTimeout = TimeSpan.FromSeconds(30);
        using var client = new RegistryClient(options, RetryBaseDelay);
        using var cancellation = new CancellationTokenSource();

        //Act
        Task download = client.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, data.Length,
            Destination, PartialData, PartialState, null, cancellation.Token);
        await handler.StallStarted;
        await cancellation.CancelAsync();
        Func<Task> act = () => download;

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(PartialData).Should().BeTrue();
        File.Exists(PartialState).Should().BeTrue();
        new FileInfo(PartialData).Length.Should().Be(1000L);
        ReadState().Parts[0].Completed.Should().Be(100L);
        File.Exists(Destination).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadBlobAsync_reports_progress_that_only_grows_and_ends_at_the_total()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        byte[] data = CreateData(10000);
        string digest = handler.AddBlob(data);
        using var client = new RegistryClient(RegistryClientTests.CreateOptions(handler), RetryBaseDelay);
        var reports = new List<(long Completed, long Total)>();
        var reportLock = new object();

        //Act
        await client.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, data.Length,
            Destination, PartialData, PartialState,
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
    public async Task DownloadBlobAsync_with_the_file_already_in_place_makes_no_requests()
    {
        //Arrange
        using var handler = new FakeRegistryHandler();
        byte[] data = CreateData(500);
        string digest = FakeRegistryHandler.ComputeDigest(data);
        Directory.CreateDirectory(Path.GetDirectoryName(Destination));
        await File.WriteAllBytesAsync(Destination, data, TestContext.Current.CancellationToken);
        using var client = new RegistryClient(RegistryClientTests.CreateOptions(handler), RetryBaseDelay);
        long reported = -1;

        //Act
        await client.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, data.Length,
            Destination, PartialData, PartialState,
            (completed, total) => reported = completed,
            TestContext.Current.CancellationToken);

        //Assert
        handler.Requests.Should().BeEmpty();
        reported.Should().Be(500L);
    }

    [Fact]
    public async Task DownloadBlobAsync_with_a_server_that_ignores_ranges_fails_after_its_retries()
    {
        //Arrange
        using var handler = new FakeRegistryHandler { IgnoreRangeRequests = true };
        byte[] data = CreateData(10000);
        string digest = handler.AddBlob(data);
        using var client = new RegistryClient(RegistryClientTests.CreateOptions(handler), RetryBaseDelay);

        //Act
        Func<Task> act = () => client.DownloadBlobAsync(
            RegistryClientTests.CreateName(), digest, data.Length,
            Destination, PartialData, PartialState, null, TestContext.Current.CancellationToken);

        //Assert
        RegistryException exception = (await act.Should().ThrowAsync<RegistryException>()).Which;
        exception.Message.Should().Contain("max retries exceeded");
        exception.Message.Should().Contain("ignored the Range header");
    }

    /// <summary>Where a finished blob lands.</summary>
    private string Destination
    {
        get { return Path.Combine(_root, "blobs", "sha256-under-test"); }
    }

    /// <summary>The partial file a download writes to.</summary>
    private string PartialData
    {
        get { return Destination + "-partial"; }
    }

    /// <summary>The sidecar a download records its progress in.</summary>
    private string PartialState
    {
        get { return Destination + "-partial.json"; }
    }

    /// <summary>
    /// Reads the sidecar.
    /// </summary>
    /// <returns>The recorded state.</returns>
    private BlobDownloadState ReadState()
    {
        return ModelManagerJson.Deserialize<BlobDownloadState>(File.ReadAllBytes(PartialState));
    }

    /// <summary>
    /// The recorded <c>Range</c> headers ordered by the byte they start at, so that an assertion does
    /// not depend on which of the concurrent parts happened to arrive first.
    /// </summary>
    /// <param name="handler">The fake registry.</param>
    /// <returns>The ordered header values.</returns>
    private static IReadOnlyList<string> SortedRangeHeaders(FakeRegistryHandler handler)
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
