using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers exporting to ONNX as far as it can be covered with no Python on the machine: the route the
/// automatic choice takes, the whole pass-through - which needs no interpreter and is therefore an
/// ordinary offline test - the name a derived bundle takes, what replacing one costs, and the message
/// a converting route gives on a machine that cannot run it.
/// </summary>
public sealed class ModelStoreExportTests
{
    private const string OnnxBundleName = "local/skytnt/midi-model:latest";
    private const string CheckpointName = "local/m-a-p/mupt:latest";

    [Fact]
    public async Task ExportToOnnxAsync_of_a_bundle_that_ships_onnx_passes_it_through()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory);

        //Act
        ExportResult result = await store.ExportToOnnxAsync(
            OnnxBundleName, null, null, TestContext.Current.CancellationToken);

        //Assert
        result.RouteUsed.Should().Be(ExportRoute.PublisherOnnx);
        result.Tool.Should().Be("publisher");
        result.ToolVersion.Should().BeNull();
        result.Name.Should().Be("local/skytnt/midi-model:onnx");
        result.Files.Should().Equal("README.md", "config.json", "onnx/model_base.onnx", "onnx/model_token.onnx");
    }

    [Fact]
    public async Task ExportToOnnxAsync_of_a_pass_through_shares_the_blobs_it_came_from()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory);
        ResolvedModel source = await store.ResolveAsync(OnnxBundleName, TestContext.Current.CancellationToken);

        //Act
        ExportResult result = await store.ExportToOnnxAsync(
            OnnxBundleName, null, null, TestContext.Current.CancellationToken);

        //Assert
        ResolvedModel derived = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
        foreach (ResolvedFile file in derived.Files)
        {
            ResolvedFile original = source.Files.First(candidate => candidate.Name == file.Name);
            file.Digest.Should().Be(original.Digest);
            file.Size.Should().Be(original.Size);
            file.BlobPath.Should().Be(original.BlobPath);
        }
        derived.Files.Select(file => file.Name).Should().NotContain("model.safetensors");
    }

    [Fact]
    public async Task ExportToOnnxAsync_records_the_provenance_a_pass_through_has()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory);

        //Act
        ExportResult result = await store.ExportToOnnxAsync(
            OnnxBundleName, null, null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.DerivedFrom.Should().Be(OnnxBundleName);
        info.Tool.Should().Be("publisher");
        info.Format.Should().Be("onnx");
        info.Settings["route"].Should().Be("PublisherOnnx");
        info.Settings["requestedRoute"].Should().Be("Auto");
        info.License.LicenseId.Should().Be("apache-2.0");
    }

    [Fact]
    public async Task ExportToOnnxAsync_reports_the_statuses_a_caller_can_show()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory);
        var progress = new RecordingProgress();

        //Act
        await store.ExportToOnnxAsync(OnnxBundleName, null, progress, TestContext.Current.CancellationToken);

        //Assert
        progress.Statuses.Should().Equal("collecting", "writing manifest", "success");
    }

    [Fact]
    public async Task ExportToOnnxAsync_with_an_output_name_stores_it_under_that_name()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory);
        var options = new ExportOptions { OutputName = "local/skytnt/midi-model:graphs" };

        //Act
        ExportResult result = await store.ExportToOnnxAsync(
            OnnxBundleName, options, null, TestContext.Current.CancellationToken);

        //Assert
        result.Name.Should().Be("local/skytnt/midi-model:graphs");
        (await store.ExistsAsync(result.Name, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task ExportToOnnxAsync_twice_is_refused_until_overwrite_is_set()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory);
        await store.ExportToOnnxAsync(OnnxBundleName, null, null, TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = () => store.ExportToOnnxAsync(
            OnnxBundleName, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("Overwrite");
        await store.ExportToOnnxAsync(
            OnnxBundleName, new ExportOptions { Overwrite = true }, null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExportToOnnxAsync_with_PublisherOnnx_on_a_checkpoint_says_what_is_missing()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportCheckpointBundleAsync(store, directory);
        var options = new ExportOptions { Route = ExportRoute.PublisherOnnx };

        //Act
        Func<Task> act = () => store.ExportToOnnxAsync(
            CheckpointName, options, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("ships no .onnx file");
    }

    [Fact]
    public async Task ExportToOnnxAsync_of_a_model_that_is_not_a_bundle_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        Func<Task> act = () => store.ExportToOnnxAsync(
            builder.Reference, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("no publisher file tree");
    }

    [Fact]
    public async Task ExportToOnnxAsync_of_a_model_that_is_not_there_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));

        //Act
        Func<Task> act = () => store.ExportToOnnxAsync(
            "local/nobody/nothing:latest", null, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }

    [Fact]
    public async Task ExportToOnnxAsync_of_a_checkpoint_with_no_python_says_which_feature_needed_it()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        ModelStoreOptions options = CreateOptions(directory);
        //A library path that is not there is what makes this test the same on every machine, whether or
        //not the one it runs on has a CPython of its own: nothing is started, and nothing can be.
        options.Python.LibraryPath = Path.Combine(directory.DirectoryPath, "no-such-libpython3.13.so");
        using var store = new ModelStore(options);
        await ImportCheckpointBundleAsync(store, directory);

        //Act
        Func<Task> act = () => store.ExportToOnnxAsync(
            CheckpointName, null, null, TestContext.Current.CancellationToken);

        //Assert
        PythonNotAvailableException thrown = (await act.Should().ThrowAsync<PythonNotAvailableException>()).Which;
        thrown.Feature.Should().Be("exporting to ONNX");
        thrown.Message.Should().Contain("exporting to ONNX");
    }

    /// <summary>
    /// The options every test here uses: a temporary store directory and nothing else.
    /// </summary>
    /// <param name="directory">The temporary store directory.</param>
    /// <returns>The options.</returns>
    private static ModelStoreOptions CreateOptions(TempStoreDirectory directory)
        => new ModelStoreOptions
        {
            StoreDirectory = directory.DirectoryPath,
            DefaultRegistryHost = "registry.test"
        };

    /// <summary>
    /// Imports a bundle shaped like a publisher who ships both a checkpoint and the graphs exported from
    /// it, which is the shape the pass-through route is for.
    /// </summary>
    /// <param name="store">The store to import into.</param>
    /// <param name="directory">The temporary directory the folder is written inside.</param>
    /// <returns>A task that completes when the bundle is in the store.</returns>
    private static async Task ImportOnnxBundleAsync(ModelStore store, TempStoreDirectory directory)
    {
        string root = Path.Combine(directory.DirectoryPath, "onnx-source");
        Directory.CreateDirectory(Path.Combine(root, "onnx"));

        await File.WriteAllTextAsync(
            Path.Combine(root, "config.json"), "{\"architectures\":[\"MidiModel\"]}",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "README.md"), "# a model card", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "model.safetensors"), "the checkpoint the graphs came from",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "onnx", "model_base.onnx"), "the base graph",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "onnx", "model_token.onnx"), "the token graph",
            TestContext.Current.CancellationToken);

        var options = new ImportOptions
        {
            License = new LicenseRecord("apache-2.0", "https://example.test/skytnt", null)
        };
        await store.ImportBundleAsync(OnnxBundleName, root, options, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Imports a bundle shaped like a checkpoint with no graph in it, which is what a converting route is
    /// for.
    /// </summary>
    /// <param name="store">The store to import into.</param>
    /// <param name="directory">The temporary directory the folder is written inside.</param>
    /// <returns>A task that completes when the bundle is in the store.</returns>
    private static async Task ImportCheckpointBundleAsync(ModelStore store, TempStoreDirectory directory)
    {
        string root = Path.Combine(directory.DirectoryPath, "checkpoint-source");
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(
            Path.Combine(root, "config.json"), "{\"architectures\":[\"LlamaForCausalLM\"]}",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "pytorch_model.bin"), "the weights, near enough for a test",
            TestContext.Current.CancellationToken);

        await store.ImportBundleAsync(CheckpointName, root, null, TestContext.Current.CancellationToken);
    }
}
