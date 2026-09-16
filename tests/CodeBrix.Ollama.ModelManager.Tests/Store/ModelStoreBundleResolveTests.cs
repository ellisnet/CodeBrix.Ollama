using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers resolving a bundle to the blobs its files live in and laying those files out on disk again
/// under the publisher's own names: the three ways a file is put in place, what happens when something
/// is already there, and the two things that are refused - a model with no file tree, and a path that
/// would be written outside the directory that was asked for.
/// </summary>
public sealed class ModelStoreBundleResolveTests
{
    private const string BundleName = "hf.co/m-a-p/MuPT-v1-8192-190M:main";
    private const string LogPath = "logs/events.out.tfevents.1727438892";

    [Fact]
    public async Task ResolveAsync_with_a_bundle_returns_every_file_and_no_model_path()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);

        //Act
        ResolvedModel resolved = await store.ResolveAsync(BundleName, TestContext.Current.CancellationToken);

        //Assert
        resolved.Format.Should().Be("pytorch");
        resolved.ModelPath.Should().BeNull();
        resolved.Files.Should().HaveCount(6);
        resolved.Files[1].Name.Should().Be("pytorch_model.bin");
        resolved.Files[1].Size.Should().Be(ModelStoreBundlePullTests.Weights.Length);
        resolved.Files[1].Digest.Should().Be(FakeHubHandler.ComputeDigest(ModelStoreBundlePullTests.Weights));
        resolved.Files[1].BlobPath.Should().Be(directory.Paths.GetBlobPath(resolved.Files[1].Digest));
        File.Exists(resolved.Files[1].BlobPath).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_with_a_gguf_model_returns_no_files_and_the_gguf_format()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        ResolvedModel resolved = await store.ResolveAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        resolved.Format.Should().Be("gguf");
        resolved.Files.Should().BeEmpty();
        resolved.ModelPath.Should().Be(directory.Paths.GetBlobPath(builder.ModelDigest));
    }

    [Fact]
    public async Task MaterializeAsync_with_hard_links_writes_every_file_and_adds_no_blob()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);
        string target = Path.Combine(directory.DirectoryPath, "materialized");
        int blobsBefore = Directory.GetFiles(directory.Paths.BlobsDirectory).Length;

        //Act
        IReadOnlyList<string> written = await store.MaterializeAsync(
            BundleName, target, null, TestContext.Current.CancellationToken);

        //Assert
        written.Should().HaveCount(6);
        written[0].Should().Be(Path.Combine(target, "config.json"));
        (await File.ReadAllBytesAsync(written[1], TestContext.Current.CancellationToken))
            .Should().Equal(ModelStoreBundlePullTests.Weights);
        Directory.GetFiles(directory.Paths.BlobsDirectory).Length.Should().Be(blobsBefore);
        File.ResolveLinkTarget(written[1], false).Should().BeNull();
    }

    [Fact]
    public async Task MaterializeAsync_with_copies_writes_every_file()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);
        string target = Path.Combine(directory.DirectoryPath, "materialized");
        var options = new MaterializeOptions { Link = MaterializeLink.Copy };

        //Act
        IReadOnlyList<string> written = await store.MaterializeAsync(
            BundleName, target, options, TestContext.Current.CancellationToken);

        //Assert
        written.Should().HaveCount(6);
        (await File.ReadAllBytesAsync(written[1], TestContext.Current.CancellationToken))
            .Should().Equal(ModelStoreBundlePullTests.Weights);
        (await File.ReadAllTextAsync(written[3], TestContext.Current.CancellationToken))
            .Should().Be(ModelStoreBundlePullTests.LicenseText);
    }

    [Fact]
    public async Task MaterializeAsync_with_symbolic_links_points_at_the_blob()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);
        string target = Path.Combine(directory.DirectoryPath, "materialized");
        var options = new MaterializeOptions { Link = MaterializeLink.Symlink };

        //Act
        IReadOnlyList<string> written = await store.MaterializeAsync(
            BundleName, target, options, TestContext.Current.CancellationToken);

        //Assert
        ResolvedModel resolved = await store.ResolveAsync(BundleName, TestContext.Current.CancellationToken);
        File.ResolveLinkTarget(written[1], false).FullName.Should().Be(resolved.Files[1].BlobPath);
        (await File.ReadAllBytesAsync(written[1], TestContext.Current.CancellationToken))
            .Should().Equal(ModelStoreBundlePullTests.Weights);
    }

    [Fact]
    public async Task MaterializeAsync_creates_the_directories_a_nested_path_needs()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);
        string target = Path.Combine(directory.DirectoryPath, "materialized");

        //Act
        IReadOnlyList<string> written = await store.MaterializeAsync(
            BundleName, target, null, TestContext.Current.CancellationToken);

        //Assert
        string nested = Path.Combine(target, "logs", "events.out.tfevents.1727438892");
        written[5].Should().Be(nested);
        File.Exists(nested).Should().BeTrue();
        Directory.Exists(Path.Combine(target, "logs")).Should().BeTrue();
    }

    [Fact]
    public async Task MaterializeAsync_over_a_file_that_is_already_there_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);
        string target = Path.Combine(directory.DirectoryPath, "materialized");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(
            Path.Combine(target, "config.json"), "work of my own", TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = () => store.MaterializeAsync(
            BundleName, target, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("Overwrite");
        (await File.ReadAllTextAsync(Path.Combine(target, "config.json"), TestContext.Current.CancellationToken))
            .Should().Be("work of my own");
    }

    [Fact]
    public async Task MaterializeAsync_with_overwrite_replaces_a_file_that_is_already_there()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);
        string target = Path.Combine(directory.DirectoryPath, "materialized");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(
            Path.Combine(target, "config.json"), "work of my own", TestContext.Current.CancellationToken);
        var options = new MaterializeOptions { Link = MaterializeLink.Copy, Overwrite = true };

        //Act
        IReadOnlyList<string> written = await store.MaterializeAsync(
            BundleName, target, options, TestContext.Current.CancellationToken);

        //Assert
        (await File.ReadAllBytesAsync(written[0], TestContext.Current.CancellationToken))
            .Should().Equal(ModelStoreBundlePullTests.ConfigJson);
    }

    [Fact]
    public async Task MaterializeAsync_on_a_gguf_model_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);
        string target = Path.Combine(directory.DirectoryPath, "materialized");

        //Act
        Func<Task> act = () => store.MaterializeAsync(
            builder.Reference, target, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("no publisher file tree");
    }

    [Fact]
    public async Task MaterializeAsync_with_a_layer_name_that_walks_up_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(new ModelStoreOptions { StoreDirectory = directory.DirectoryPath });
        await WriteHandMadeBundleAsync(directory, "../escape.txt");
        string target = Path.Combine(directory.DirectoryPath, "materialized");

        //Act
        Func<Task> act = () => store.MaterializeAsync(
            "local/hand/made:one", target, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("walks outside");
        File.Exists(Path.Combine(directory.DirectoryPath, "escape.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task MaterializeAsync_with_an_absolute_layer_name_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(new ModelStoreOptions { StoreDirectory = directory.DirectoryPath });
        await WriteHandMadeBundleAsync(directory, "/etc/escape.txt");
        string target = Path.Combine(directory.DirectoryPath, "materialized");

        //Act
        Func<Task> act = () => store.MaterializeAsync(
            "local/hand/made:one", target, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("absolute");
    }

    [Fact]
    public async Task MaterializeAsync_without_a_target_directory_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);

        //Act
        Func<Task> act = () => store.MaterializeAsync(
            BundleName, "  ", null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Pulls the fake Hugging Face bundle every test here works with.
    /// </summary>
    /// <param name="store">The store to pull into.</param>
    /// <returns>A task that completes when the bundle is in the store.</returns>
    private static Task<IReadOnlyList<PullProgress>> PullBundleAsync(IModelStore store)
    {
        return ModelStoreBundlePullTests.RunPullAsync(
            store, BundleName, PullOptions.ForHuggingFace(null, null, null));
    }

    /// <summary>
    /// Writes a bundle manifest by hand, with a layer name no pull would ever produce. The names a
    /// pull writes are checked when they are built, and the names a materialize reads come off a disk
    /// anything could have written to, which is what this stands in for.
    /// </summary>
    /// <param name="directory">The temporary store directory.</param>
    /// <param name="layerName">The layer name to write.</param>
    /// <returns>A task that completes when the manifest is in place.</returns>
    private static async Task WriteHandMadeBundleAsync(TempStoreDirectory directory, string layerName)
    {
        ModelLayer file = await LayerFactory.CreateFromBytesAsync(
            directory.Paths,
            ModelStoreBundlePullTests.Bytes("nothing good"),
            MediaTypes.BundleFile,
            TestContext.Current.CancellationToken);
        file.Name = layerName;

        ModelLayer config = await LayerFactory.CreateFromBytesAsync(
            directory.Paths,
            ModelManagerJson.SerializeLikeGo(new ModelConfig { ModelFormat = "imported" }),
            MediaTypes.Config,
            TestContext.Current.CancellationToken);

        var manifest = new ModelManifest
        {
            Config = config,
            Layers = new List<ModelLayer> { file }
        };
        await ManifestFiles.WriteAsync(
            directory.Paths,
            ModelName.Parse("local/hand/made:one"),
            manifest,
            TestContext.Current.CancellationToken);
    }
}
