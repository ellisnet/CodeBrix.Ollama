using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers pulling a model into a store against the in-memory registry: the progress statuses, the
/// bytes written, the caching of layers already present, the pruning of layers a new manifest dropped,
/// and every failure a pull can end in.
/// </summary>
public sealed class ModelStorePullTests
{
    [Fact]
    public async Task PullAsync_WithANewModel_ReportsTheStatusesInOrder()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        IReadOnlyList<string> statuses = await CollectStatusesAsync(store, builder.Reference);

        //Assert
        statuses.Should().Equal(ExpectedStatuses(builder));
    }

    [Fact]
    public async Task PullAsync_WithANewModel_WritesTheManifestBytesTheRegistryServed()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        await CollectStatusesAsync(store, builder.Reference);

        //Assert
        string manifestPath = Path.Combine(
            directory.DirectoryPath, "manifests", "registry.test", "library", "test", "latest");
        File.Exists(manifestPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(manifestPath, TestContext.Current.CancellationToken))
            .Should().Equal(builder.ManifestBytes);
    }

    [Fact]
    public async Task PullAsync_WithANewModel_WritesEveryBlobAtItsManifestSize()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        await CollectStatusesAsync(store, builder.Reference);

        //Assert
        foreach (ModelLayer layer in AllLayers(builder))
        {
            var blob = new FileInfo(directory.Paths.GetBlobPath(layer.Digest));
            blob.Exists.Should().BeTrue();
            blob.Length.Should().Be(layer.Size);
        }
    }

    [Fact]
    public async Task PullAsync_WhenEveryLayerIsCached_MakesNoBlobRequests()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(CreateOptions(handler, directory));
        await CollectStatusesAsync(store, builder.Reference);
        int blobRequestsBefore = CountBlobRequests(handler);

        //Act
        IReadOnlyList<PullProgress> reports = await CollectAsync(store, builder.Reference);

        //Assert
        blobRequestsBefore.Should().BeGreaterThan(0);
        CountBlobRequests(handler).Should().Be(blobRequestsBefore);
        reports.Select(report => report.Status).Should().Equal(ExpectedStatuses(builder));
    }

    [Fact]
    public async Task PullAsync_WithAReplacedLayer_RemovesTheOldBlobAndKeepsTheSharedOnes()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(CreateOptions(handler, directory));
        await CollectStatusesAsync(store, builder.Reference);
        string replacedTemplateDigest = builder.TemplateDigest;
        string sharedModelDigest = builder.ModelDigest;

        //Act
        builder.Template = "a completely different template";
        builder.Build(handler);
        IReadOnlyList<string> statuses = await CollectStatusesAsync(store, builder.Reference);

        //Assert
        statuses.Should().Contain("removing unused layers");
        File.Exists(directory.Paths.GetBlobPath(replacedTemplateDigest)).Should().BeFalse();
        File.Exists(directory.Paths.GetBlobPath(sharedModelDigest)).Should().BeTrue();
        File.Exists(directory.Paths.GetBlobPath(builder.TemplateDigest)).Should().BeTrue();
    }

    [Fact]
    public async Task PullAsync_WithASafetensorsManifest_ThrowsBeforeAnyBlobRequest()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder { IncludeTensorLayer = true };
        builder.Build(handler);
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        Exception captured = null;
        try
        {
            await CollectStatusesAsync(store, builder.Reference);
        }
        catch (Exception exception)
        {
            captured = exception;
        }

        //Assert
        captured.Should().BeOfType<ModelManagerException>();
        captured.Message.Should().Contain("safetensors");
        CountBlobRequests(handler).Should().Be(0);
    }

    [Fact]
    public async Task PullAsync_WithAnUnknownModel_ThrowsModelNotFound()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => CollectStatusesAsync(store, "missing:latest");

        //Assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }

    [Fact]
    public async Task PullAsync_WithACorruptedBlob_ThrowsDigestMismatchAndLeavesNoBlob()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        byte[] corrupted = (byte[])builder.ModelBytes.Clone();
        corrupted[corrupted.Length - 1] ^= 0xFF;
        handler.AddBlob(builder.ModelDigest, corrupted);
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => CollectStatusesAsync(store, builder.Reference);

        //Assert
        await act.Should().ThrowAsync<DigestMismatchException>();
        File.Exists(directory.Paths.GetBlobPath(builder.ModelDigest)).Should().BeFalse();
    }

    [Fact]
    public async Task PullAsync_WhenCancelledMidDownload_LeavesThePartialFilesAndNoManifest()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        handler.StallAfter(0, 1, 100);
        ModelStoreOptions options = CreateOptions(handler, directory);
        options.StallTimeout = TimeSpan.FromSeconds(30);
        using var store = new ModelStore(options);
        using var cancellation = new CancellationTokenSource();

        //Act
        Task pull = Task.Run(async () =>
        {
            await foreach (PullProgress report in store.PullAsync(builder.Reference, cancellation.Token))
            {
                _ = report;
            }
        }, TestContext.Current.CancellationToken);
        await handler.StallStarted;
        await cancellation.CancelAsync();
        Func<Task> act = () => pull;

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(directory.Paths.GetPartialDataPath(builder.ModelDigest)).Should().BeTrue();
        File.Exists(directory.Paths.GetPartialStatePath(builder.ModelDigest)).Should().BeTrue();
        File.Exists(Path.Combine(
            directory.DirectoryPath, "manifests", "registry.test", "library", "test", "latest"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task PullAsync_WithAHuggingFaceStyleName_PullsThroughTheSamePath()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder("user", "repo", "Q8_0") { Host = "hf.co" };
        builder.Build(handler);
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        IReadOnlyList<string> statuses = await CollectStatusesAsync(store, builder.Reference);

        //Assert
        statuses.Should().Equal(ExpectedStatuses(builder));
        File.Exists(Path.Combine(directory.DirectoryPath, "manifests", "hf.co", "user", "repo", "Q8_0"))
            .Should().BeTrue();
        File.Exists(directory.Paths.GetBlobPath(builder.ModelDigest)).Should().BeTrue();
    }

    /// <summary>
    /// Builds the store options every store test uses: the fake registry, a temporary store directory
    /// and a default host the fake registry answers for.
    /// </summary>
    /// <param name="handler">The fake registry.</param>
    /// <param name="directory">The temporary store directory.</param>
    /// <returns>The options.</returns>
    internal static ModelStoreOptions CreateOptions(FakeRegistryHandler handler, TempStoreDirectory directory)
    {
        ModelStoreOptions options = RegistryClientTests.CreateOptions(handler);
        options.StoreDirectory = directory.DirectoryPath;
        options.DefaultRegistryHost = "registry.test";
        return options;
    }

    /// <summary>
    /// Pulls a model and returns every progress report.
    /// </summary>
    /// <param name="store">The store to pull with.</param>
    /// <param name="name">The model name.</param>
    /// <returns>The reports in order.</returns>
    internal static async Task<IReadOnlyList<PullProgress>> CollectAsync(IModelStore store, string name)
    {
        var reports = new List<PullProgress>();
        await foreach (PullProgress report in store.PullAsync(name, TestContext.Current.CancellationToken))
        {
            if (report.Digest == null || reports.Count == 0 || reports[reports.Count - 1].Status != report.Status)
            {
                reports.Add(report);
            }
        }
        return reports;
    }

    /// <summary>
    /// Pulls a model and returns the statuses it reported, with the repeated reports of one layer's
    /// download collapsed into one.
    /// </summary>
    /// <param name="store">The store to pull with.</param>
    /// <param name="name">The model name.</param>
    /// <returns>The statuses in order.</returns>
    internal static async Task<IReadOnlyList<string>> CollectStatusesAsync(IModelStore store, string name)
    {
        IReadOnlyList<PullProgress> reports = await CollectAsync(store, name);
        return reports.Select(report => report.Status).ToList();
    }

    /// <summary>
    /// The statuses a clean pull of a fake model reports, in order.
    /// </summary>
    /// <param name="builder">The fake model.</param>
    /// <returns>The statuses.</returns>
    private static IReadOnlyList<string> ExpectedStatuses(FakeModelBuilder builder)
    {
        var statuses = new List<string> { "pulling manifest" };
        foreach (ModelLayer layer in AllLayers(builder))
        {
            statuses.Add("pulling " + Sha256Digest.Short(layer.Digest));
        }
        statuses.Add("verifying sha256 digest");
        statuses.Add("writing manifest");
        statuses.Add("success");
        return statuses;
    }

    /// <summary>
    /// Every layer of a fake model's manifest, its content layers followed by its config layer.
    /// </summary>
    /// <param name="builder">The fake model.</param>
    /// <returns>The layers.</returns>
    private static IReadOnlyList<ModelLayer> AllLayers(FakeModelBuilder builder)
    {
        var layers = new List<ModelLayer>(builder.Manifest.Layers);
        if (builder.Manifest.Config != null)
        {
            layers.Add(builder.Manifest.Config);
        }
        return layers;
    }

    /// <summary>
    /// How many blob requests the fake registry has seen.
    /// </summary>
    /// <param name="handler">The fake registry.</param>
    /// <returns>The request count.</returns>
    private static int CountBlobRequests(FakeRegistryHandler handler)
    {
        return handler.Requests.Count(request =>
            request.RequestUri.AbsolutePath.Contains("/blobs/", StringComparison.Ordinal));
    }
}
