using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers pulling a bundle - a model that is a set of the publisher's own files rather than a GGUF
/// weights file - into a store: the progress vocabulary, the manifest and config it writes, the filter
/// that decides what is fetched, the licence and readme that are fetched whatever the filter says, the
/// second pull that costs nothing, the revision that moved, and the pull that must be asked for
/// explicitly because the old one still means what it always meant.
/// </summary>
public sealed class ModelStoreBundlePullTests
{
    private const string Repository = "m-a-p/MuPT-v1-8192-190M";
    private const string BundleName = "hf.co/m-a-p/MuPT-v1-8192-190M:main";
    private const string Commit = "3f2a1b9c0d4e5f60718293a4b5c6d7e8f9a0b1c2";
    private const string MovedCommit = "a1b2c3d4e5f60718293a4b5c6d7e8f9a0b1c2d3e";
    private const string LogPath = "logs/events.out.tfevents.1727438892";
    private const string BucketName = "magentadata";
    private const string BucketPrefix = "models/music_transformer/";
    private const string BucketBundleName =
        "storage.googleapis.com/magentadata/music-transformer:unconditional-16";

    [Fact]
    public async Task PullAsync_with_hugging_face_files_reports_the_statuses_in_order()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateHub();
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        IReadOnlyList<PullProgress> reports = await RunPullAsync(
            store, BundleName, PullOptions.ForHuggingFace(null, null, null));

        //Assert
        Statuses(reports).Should().Equal(
            "listing " + Repository,
            "pulling config.json",
            "pulling pytorch_model.bin",
            "pulling vocab.json",
            "pulling LICENSE",
            "pulling README.md",
            "pulling " + LogPath,
            "verifying sha256 digest",
            "writing manifest",
            "success");
    }

    [Fact]
    public async Task PullAsync_with_hugging_face_files_writes_one_layer_per_file()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateHub();
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        await RunPullAsync(store, BundleName, PullOptions.ForHuggingFace(null, null, null));

        //Assert
        ModelInfo info = await store.ShowAsync(BundleName, TestContext.Current.CancellationToken);
        info.Manifest.Layers.Select(layer => layer.Name).Should().Equal(
            "config.json", "pytorch_model.bin", "vocab.json", "LICENSE", "README.md", LogPath);
        foreach (ModelLayer layer in info.Manifest.Layers)
        {
            layer.MediaType.Should().Be(MediaTypes.BundleFile);
        }
        ModelLayer weights = info.Manifest.Layers[1];
        weights.Digest.Should().Be(FakeHubHandler.ComputeDigest(Weights));
        weights.Size.Should().Be(Weights.Length);
        File.Exists(directory.Paths.GetBlobPath(weights.Digest)).Should().BeTrue();
    }

    [Fact]
    public async Task PullAsync_with_hugging_face_files_reports_the_digest_of_every_file()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateHub();
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        IReadOnlyList<PullProgress> reports = await RunPullAsync(
            store, BundleName, PullOptions.ForHuggingFace(null, null, null));

        //Assert
        LastReport(reports, "pulling pytorch_model.bin").Digest
            .Should().Be(FakeHubHandler.ComputeDigest(Weights));
        LastReport(reports, "pulling config.json").Digest
            .Should().Be(FakeHubHandler.ComputeDigest(ConfigJson));
        LastReport(reports, "pulling pytorch_model.bin").CompletedBytes.Should().Be(Weights.Length);
        LastReport(reports, "pulling pytorch_model.bin").TotalBytes.Should().Be(Weights.Length);
    }

    [Fact]
    public async Task PullAsync_with_hugging_face_files_records_the_source_the_revision_and_the_licence()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateHub();
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        await RunPullAsync(store, BundleName, PullOptions.ForHuggingFace(null, null, null));

        //Assert
        ModelInfo info = await store.ShowAsync(BundleName, TestContext.Current.CancellationToken);
        info.Config.ModelFormat.Should().Be("pytorch");
        info.Config.ModelFamily.Should().Be("MuPT-v1-8192-190M");
        ConfigProperty(info, "source").Should().Be("hf.co");
        ConfigProperty(info, "repository").Should().Be(Repository);
        ConfigProperty(info, "revision").Should().Be(Commit);
        ConfigProperty(info, "licenseId").Should().Be("apache-2.0");
        ConfigProperty(info, "licenseSource").Should().Be("https://huggingface.co/" + Repository);
        DateTimeOffset.TryParse(ConfigProperty(info, "pulledAt"), out DateTimeOffset _).Should().BeTrue();
    }

    [Fact]
    public async Task PullAsync_with_an_aggressive_filter_still_pulls_the_licence_and_the_readme()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateHub();
        using var store = new ModelStore(CreateOptions(handler, directory));
        var filter = new FileFilter(new[] { "*.bin" }, null);

        //Act
        await RunPullAsync(store, BundleName, PullOptions.ForHuggingFace(null, null, filter));

        //Assert
        ModelInfo info = await store.ShowAsync(BundleName, TestContext.Current.CancellationToken);
        info.Manifest.Layers.Select(layer => layer.Name).Should().Equal(
            "pytorch_model.bin", "LICENSE", "README.md");
    }

    [Fact]
    public async Task PullAsync_with_a_filter_that_drops_the_logs_leaves_them_behind()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateHub();
        using var store = new ModelStore(CreateOptions(handler, directory));
        var filter = new FileFilter(null, new[] { "logs/**" });

        //Act
        await RunPullAsync(store, BundleName, PullOptions.ForHuggingFace(null, null, filter));

        //Assert
        ModelInfo info = await store.ShowAsync(BundleName, TestContext.Current.CancellationToken);
        info.Manifest.Layers.Select(layer => layer.Name).Should().NotContain(LogPath);
        info.Manifest.Layers.Should().HaveCount(5);
    }

    [Fact]
    public async Task PullAsync_with_required_hashes_refuses_a_file_the_source_states_none_for()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateHub();
        using var store = new ModelStore(CreateOptions(handler, directory));
        var options = new PullOptions
        {
            Source = PullSource.HuggingFaceFiles,
            RequireHashes = true
        };

        //Act
        Func<Task> act = () => RunPullAsync(store, BundleName, options);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("config.json");
        (await store.ExistsAsync(BundleName, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task PullAsync_of_files_that_state_their_hashes_costs_no_request_the_second_time()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateVerifiableHub(Commit);
        using var store = new ModelStore(CreateOptions(handler, directory));
        await RunPullAsync(store, BundleName, PullOptions.ForHuggingFace(null, null, null));
        int fileRequestsBefore = CountFileRequests(handler);

        //Act
        IReadOnlyList<PullProgress> reports = await RunPullAsync(
            store, BundleName, PullOptions.ForHuggingFace(null, null, null));

        //Assert
        fileRequestsBefore.Should().BeGreaterThan(0);
        CountFileRequests(handler).Should().Be(fileRequestsBefore);
        Statuses(reports).Should().Contain("success");
    }

    [Fact]
    public async Task PullAsync_after_the_revision_moved_replaces_the_manifest_and_prunes_the_old_blobs()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateVerifiableHub(Commit);
        using var store = new ModelStore(CreateOptions(handler, directory));
        await RunPullAsync(store, BundleName, PullOptions.ForHuggingFace(null, null, null));
        ModelInfo before = await store.ShowAsync(BundleName, TestContext.Current.CancellationToken);
        string replacedDigest = before.Manifest.Layers[0].Digest;
        string sharedDigest = before.Manifest.Layers[1].Digest;

        //Act
        handler.AddFile(Repository, MovedCommit, "config.json", Bytes("{\"hidden_size\":256}"), true);
        handler.AddFile(Repository, MovedCommit, "pytorch_model.bin", Weights, true);
        handler.AddRepository(Repository, MovedCommit);
        IReadOnlyList<PullProgress> reports = await RunPullAsync(
            store, BundleName, PullOptions.ForHuggingFace(null, null, null));

        //Assert
        ModelInfo after = await store.ShowAsync(BundleName, TestContext.Current.CancellationToken);
        ConfigProperty(before, "revision").Should().Be(Commit);
        ConfigProperty(after, "revision").Should().Be(MovedCommit);
        Statuses(reports)[Statuses(reports).Count - 1].Should().Be("success");
        File.Exists(directory.Paths.GetBlobPath(replacedDigest)).Should().BeFalse();
        File.Exists(directory.Paths.GetBlobPath(sharedDigest)).Should().BeTrue();
        after.Manifest.Layers[1].Digest.Should().Be(sharedDigest);
    }

    [Fact]
    public async Task PullAsync_with_the_repository_in_the_options_uses_it_instead_of_the_name()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateHub();
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        await RunPullAsync(store, "local/mirror/mupt:main", PullOptions.ForHuggingFace(Repository, null, null));

        //Assert
        ModelInfo info = await store.ShowAsync("local/mirror/mupt:main", TestContext.Current.CancellationToken);
        ConfigProperty(info, "repository").Should().Be(Repository);
        File.Exists(Path.Combine(directory.DirectoryPath, "manifests", "local", "mirror", "mupt", "main"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task PullAsync_with_a_name_that_is_not_hugging_face_and_no_repository_throws()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateHub();
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => RunPullAsync(
            store, "registry.test/library/test:latest", PullOptions.ForHuggingFace(null, null, null));

        //Assert
        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("PullOptions.Repository");
    }

    [Fact]
    public async Task PullAsync_without_options_still_takes_the_registry_path_for_a_hugging_face_name()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = CreateHub();
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => ModelStorePullTests.CollectStatusesAsync(store, BundleName);

        //Assert
        // The fake Hub speaks the Hub API and nothing else, exactly as the real one does for a
        // repository that holds no GGUF file: the manifest address of the registry protocol is not a
        // route it has. The old pull still goes there and still fails there.
        await act.Should().ThrowAsync<ModelNotFoundException>();
        (await store.ExistsAsync(BundleName, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task PullAsync_with_registry_options_is_the_pull_it_always_was()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        IReadOnlyList<PullProgress> reports = await RunPullAsync(
            store, builder.Reference, PullOptions.ForRegistry());

        //Assert
        IReadOnlyList<string> statuses = Statuses(reports);
        statuses[0].Should().Be("pulling manifest");
        statuses[statuses.Count - 1].Should().Be("success");
        statuses.Should().Contain("verifying sha256 digest");
        File.Exists(directory.Paths.GetBlobPath(builder.ModelDigest)).Should().BeTrue();
    }

    [Fact]
    public async Task PullAsync_with_no_options_at_all_is_the_pull_it_always_was()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        IReadOnlyList<PullProgress> reports = await RunPullAsync(store, builder.Reference, null);

        //Assert
        Statuses(reports)[0].Should().Be("pulling manifest");
        (await store.ExistsAsync(builder.Reference, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task PullAsync_with_a_file_list_records_the_computed_sha256_and_the_url_source()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        using var store = new ModelStore(CreateOptions(handler, directory));
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            BucketName, BucketPrefix, handler, TestContext.Current.CancellationToken);

        //Act
        await RunPullAsync(store, BucketBundleName, PullOptions.ForFileList(files, null));

        //Assert
        ModelInfo info = await store.ShowAsync(BucketBundleName, TestContext.Current.CancellationToken);
        info.Manifest.Layers.Should().HaveCount(5);
        info.Manifest.Layers[3].Name.Should().Be("primers/c_major_scale.mid");
        info.Manifest.Layers[3].Digest.Should().Be(
            FakeHubHandler.ComputeDigest(GoogleCloudStorageListingTests.Bytes("c major scale")));
        ConfigProperty(info, "source").Should().Be("url");
        IsNull(info, "repository").Should().BeTrue();
        IsNull(info, "revision").Should().BeTrue();
        IsNull(info, "licenseId").Should().BeTrue();
    }

    [Fact]
    public async Task PullAsync_with_a_file_list_of_checkpoint_files_records_the_tensorflow_format()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        using var store = new ModelStore(CreateOptions(handler, directory));
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            BucketName, BucketPrefix, handler, TestContext.Current.CancellationToken);

        //Act
        await RunPullAsync(store, BucketBundleName, PullOptions.ForFileList(files, null));

        //Assert
        ModelInfo info = await store.ShowAsync(BucketBundleName, TestContext.Current.CancellationToken);
        info.Config.ModelFormat.Should().Be("tensorflow-checkpoint");
        info.Format.Should().Be("tensorflow-checkpoint");
    }

    [Fact]
    public async Task PullAsync_with_a_file_list_verifies_the_md5_the_bucket_stated()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        using var store = new ModelStore(CreateOptions(handler, directory));
        IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
            BucketName, BucketPrefix, handler, TestContext.Current.CancellationToken);

        //Act
        // The object is replaced by bytes of the same length, so only the hash the bucket stated in its
        // listing can tell that what arrives is not what was listed.
        handler.AddObject(BucketPrefix + "primers/fur_elise.mid", GoogleCloudStorageListingTests.Bytes("FUR ELISE"));
        Func<Task> act = () => RunPullAsync(store, BucketBundleName, PullOptions.ForFileList(files, null));

        //Assert
        (await act.Should().ThrowAsync<DigestMismatchException>()).Which.Message.Should().Contain("md5:");
        (await store.ExistsAsync(BucketBundleName, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task PullAsync_with_a_file_list_and_no_files_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeBucketHandler handler = GoogleCloudStorageListingTests.CreateBucket();
        using var store = new ModelStore(CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => RunPullAsync(
            store, BucketBundleName, new PullOptions { Source = PullSource.FileList });

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// The bytes of the configuration file the fake repository ships.
    /// </summary>
    internal static byte[] ConfigJson
    {
        get { return Bytes("{\"model_type\":\"llama\",\"hidden_size\":128}"); }
    }

    /// <summary>
    /// The bytes of the weights file the fake repository ships, large enough to be fetched in several
    /// byte ranges under the part sizes these tests use.
    /// </summary>
    internal static byte[] Weights
    {
        get { return CreateData(4096); }
    }

    /// <summary>
    /// The text of the licence file the fake repository ships.
    /// </summary>
    internal static string LicenseText
    {
        get { return "Apache License, Version 2.0 - the text a publisher ships beside its weights."; }
    }

    /// <summary>
    /// A fake Hugging Face repository shaped like a small transformers model: a configuration file, a
    /// weights file in large-file storage, a tokenizer vocabulary, a licence, a readme and a training
    /// log. Only the weights file states a hash, which is what the Hub does.
    /// </summary>
    /// <returns>The handler. The caller disposes it.</returns>
    internal static FakeHubHandler CreateHub()
    {
        var handler = new FakeHubHandler { LicenseTag = "apache-2.0" };
        handler.AddRepository(Repository, Commit);
        handler.AddFile(Repository, Commit, "config.json", ConfigJson, false);
        handler.AddFile(Repository, Commit, "pytorch_model.bin", Weights, true);
        handler.AddFile(Repository, Commit, "vocab.json", Bytes("{\"a\":0,\"b\":1}"), false);
        handler.AddFile(Repository, Commit, "LICENSE", Bytes(LicenseText), false);
        handler.AddFile(Repository, Commit, "README.md", Bytes("# A model card"), false);
        handler.AddDirectory(Repository, Commit, "logs");
        handler.AddFile(Repository, Commit, LogPath, Bytes("training events"), false);
        return handler;
    }

    /// <summary>
    /// A fake Hugging Face repository whose every file is in large-file storage, so that every file
    /// states the SHA-256 of its content and a second pull can find every blob without a request.
    /// </summary>
    /// <param name="commit">The commit the default branch points at.</param>
    /// <returns>The handler. The caller disposes it.</returns>
    internal static FakeHubHandler CreateVerifiableHub(string commit)
    {
        var handler = new FakeHubHandler { LicenseTag = "apache-2.0" };
        handler.AddRepository(Repository, commit);
        handler.AddFile(Repository, commit, "config.json", ConfigJson, true);
        handler.AddFile(Repository, commit, "pytorch_model.bin", Weights, true);
        return handler;
    }

    /// <summary>
    /// The store options every bundle test uses: the fake service, a temporary store directory, and
    /// part sizes and timings small enough that a download splits and reports within a test run.
    /// </summary>
    /// <param name="handler">The fake service.</param>
    /// <param name="directory">The temporary store directory.</param>
    /// <returns>The options.</returns>
    internal static ModelStoreOptions CreateOptions(FakeHttpHandlerBase handler, TempStoreDirectory directory)
    {
        return new ModelStoreOptions
        {
            HttpMessageHandler = handler,
            StoreDirectory = directory.DirectoryPath,
            DefaultRegistryHost = "registry.test",
            MinPartSize = 1024,
            MaxPartSize = 4096,
            MaxConcurrentParts = 4,
            StallTimeout = TimeSpan.FromMilliseconds(300),
            ProgressInterval = TimeSpan.FromMilliseconds(20),
            MaxRetries = 3
        };
    }

    /// <summary>
    /// Pulls a bundle and returns every progress report it made.
    /// </summary>
    /// <param name="store">The store to pull with.</param>
    /// <param name="name">The name the bundle is stored under.</param>
    /// <param name="options">What the pull is asked to do.</param>
    /// <returns>The reports in order.</returns>
    internal static async Task<IReadOnlyList<PullProgress>> RunPullAsync(
        IModelStore store, string name, PullOptions options)
    {
        var reports = new List<PullProgress>();
        await foreach (PullProgress report in store.PullAsync(name, options, TestContext.Current.CancellationToken))
        {
            reports.Add(report);
        }
        return reports;
    }

    /// <summary>
    /// The statuses a run of reports carries, with the repeated reports of one file's download
    /// collapsed into one.
    /// </summary>
    /// <param name="reports">The reports in the order they arrived.</param>
    /// <returns>The statuses in order.</returns>
    internal static IReadOnlyList<string> Statuses(IReadOnlyList<PullProgress> reports)
    {
        var statuses = new List<string>();
        foreach (PullProgress report in reports)
        {
            if (statuses.Count == 0 || statuses[statuses.Count - 1] != report.Status)
            {
                statuses.Add(report.Status);
            }
        }
        return statuses;
    }

    /// <summary>
    /// One of the properties a bundle config records beside the ones the library models.
    /// </summary>
    /// <param name="info">The described model.</param>
    /// <param name="propertyName">The property to read.</param>
    /// <returns>The value as a string, or <see langword="null"/> when it is not a string.</returns>
    internal static string ConfigProperty(ModelInfo info, string propertyName)
    {
        JsonElement value = info.Config.AdditionalProperties[propertyName];
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>
    /// Whether one of the properties a bundle config records is written as a JSON null, which is how a
    /// config says that the source stated nothing rather than saying nothing at all.
    /// </summary>
    /// <param name="info">The described model.</param>
    /// <param name="propertyName">The property to read.</param>
    /// <returns><see langword="true"/> when the property is there and is null.</returns>
    internal static bool IsNull(ModelInfo info, string propertyName)
    {
        return info.Config.AdditionalProperties[propertyName].ValueKind == JsonValueKind.Null;
    }

    /// <summary>
    /// A deterministic body for a fake file.
    /// </summary>
    /// <param name="text">The text the file holds.</param>
    /// <returns>The bytes.</returns>
    internal static byte[] Bytes(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// The last report of one status, which is the one carrying the totals a file finished at.
    /// </summary>
    /// <param name="reports">The reports in the order they arrived.</param>
    /// <param name="status">The status to look for.</param>
    /// <returns>The last report of that status.</returns>
    private static PullProgress LastReport(IReadOnlyList<PullProgress> reports, string status)
    {
        return reports.Last(report => report.Status == status);
    }

    /// <summary>
    /// How many requests for the bytes of a file the fake Hub has seen, which is what a second pull of
    /// an unchanged repository must not add to.
    /// </summary>
    /// <param name="handler">The fake Hub.</param>
    /// <returns>The request count.</returns>
    private static int CountFileRequests(FakeHubHandler handler)
    {
        return handler.Requests.Count(request =>
            request.RequestUri.AbsolutePath.Contains("/resolve/", StringComparison.Ordinal)
            || string.Equals(request.RequestUri.Host, handler.CdnHost, StringComparison.OrdinalIgnoreCase));
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
            data[index] = (byte)((index * 17 + 3) % 251);
        }
        return data;
    }
}
