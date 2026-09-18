using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the writer behind every artifact this library produces itself: the layers and the provenance
/// a derived bundle records, the two ways its files reach the store, what replacing one costs, and that
/// a derived bundle is an ordinary bundle in every other respect - it lists, shows, resolves,
/// materializes and deletes, and deleting it leaves the model it came from alone.
/// </summary>
public sealed class ModelStoreDerivedBundleTests
{
    private const string SourceName = "local/probe/checkpoint:latest";
    private const string DerivedName = "local/probe/checkpoint:onnx";

    [Fact]
    public async Task WriteDerivedBundleAsync_records_one_layer_per_file_and_the_provenance()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string produced = await CreateProducedFolderAsync(directory);

        //Act
        IReadOnlyList<string> written = await store.WriteDerivedBundleAsync(
            DerivedName, produced, CreateProvenance(), false, TestContext.Current.CancellationToken);

        //Assert
        written.Should().Equal("genai_config.json", "model.onnx", "tokenizer.json");
        ModelInfo info = await store.ShowAsync(DerivedName, TestContext.Current.CancellationToken);
        info.Manifest.Layers.Select(layer => layer.Name).Should().Equal(
            "genai_config.json", "model.onnx", "tokenizer.json");
        info.Manifest.Layers.All(layer => layer.MediaType == MediaTypes.BundleFile).Should().BeTrue();
        info.DerivedFrom.Should().Be(SourceName);
        info.Tool.Should().Be("onnxruntime-genai");
        info.ToolVersion.Should().Be("0.15.2");
        info.Format.Should().Be("onnx");
    }

    [Fact]
    public async Task WriteDerivedBundleAsync_writes_every_provenance_field_into_the_config()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string produced = await CreateProducedFolderAsync(directory);

        //Act
        await store.WriteDerivedBundleAsync(
            DerivedName, produced, CreateProvenance(), false, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(DerivedName, TestContext.Current.CancellationToken);
        ModelStoreBundlePullTests.ConfigProperty(info, ModelConfigKeys.Source).Should().Be("derived");
        ModelStoreBundlePullTests.ConfigProperty(info, ModelConfigKeys.DerivedFrom).Should().Be(SourceName);
        ModelStoreBundlePullTests.ConfigProperty(info, ModelConfigKeys.Tool).Should().Be("onnxruntime-genai");
        ModelStoreBundlePullTests.ConfigProperty(info, ModelConfigKeys.ToolVersion).Should().Be("0.15.2");
        ModelStoreBundlePullTests.ConfigProperty(info, ModelConfigKeys.LicenseId).Should().Be("apache-2.0");
        DateTimeOffset.TryParse(
            ModelStoreBundlePullTests.ConfigProperty(info, ModelConfigKeys.DerivedAt), out DateTimeOffset _)
            .Should().BeTrue();
        DateTimeOffset.TryParse(
            ModelStoreBundlePullTests.ConfigProperty(info, ModelConfigKeys.PulledAt), out DateTimeOffset _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task ShowAsync_reads_the_settings_a_derived_bundle_records()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string produced = await CreateProducedFolderAsync(directory);

        //Act
        await store.WriteDerivedBundleAsync(
            DerivedName, produced, CreateProvenance(), false, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(DerivedName, TestContext.Current.CancellationToken);
        info.Settings.Should().NotBeNull();
        info.Settings["precision"].Should().Be("int4");
        info.Settings["route"].Should().Be("GenAiBuilder");
    }

    [Fact]
    public async Task ShowAsync_of_a_bundle_that_was_not_derived_reports_no_provenance()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string produced = await CreateProducedFolderAsync(directory);
        await store.ImportBundleAsync(SourceName, produced, null, TestContext.Current.CancellationToken);

        //Act
        ModelInfo info = await store.ShowAsync(SourceName, TestContext.Current.CancellationToken);

        //Assert
        info.DerivedFrom.Should().BeNull();
        info.Tool.Should().BeNull();
        info.ToolVersion.Should().BeNull();
        info.Settings.Should().BeNull();
    }

    [Fact]
    public async Task WriteDerivedBundleAsync_over_a_name_that_is_taken_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string produced = await CreateProducedFolderAsync(directory);
        await store.WriteDerivedBundleAsync(
            DerivedName, produced, CreateProvenance(), false, TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = () => store.WriteDerivedBundleAsync(
            DerivedName, produced, CreateProvenance(), false, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("already in the store");
    }

    [Fact]
    public async Task WriteDerivedBundleAsync_with_overwrite_replaces_what_was_there()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string produced = await CreateProducedFolderAsync(directory);
        await store.WriteDerivedBundleAsync(
            DerivedName, produced, CreateProvenance(), false, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(produced, "model.onnx"), "a second graph", TestContext.Current.CancellationToken);

        //Act
        await store.WriteDerivedBundleAsync(
            DerivedName, produced, CreateProvenance(), true, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(DerivedName, TestContext.Current.CancellationToken);
        info.Manifest.Layers.Should().HaveCount(3);
        (await store.ListAsync(TestContext.Current.CancellationToken)).Should().HaveCount(1);
        ResolvedModel resolved = await store.ResolveAsync(DerivedName, TestContext.Current.CancellationToken);
        (await File.ReadAllTextAsync(
            resolved.Files.First(file => file.Name == "model.onnx").BlobPath,
            TestContext.Current.CancellationToken)).Should().Be("a second graph");
    }

    [Fact]
    public async Task WriteDerivedBundleAsync_of_a_folder_with_nothing_in_it_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string empty = Path.Combine(directory.DirectoryPath, "empty");
        Directory.CreateDirectory(empty);

        //Act
        Func<Task> act = () => store.WriteDerivedBundleAsync(
            DerivedName, empty, CreateProvenance(), false, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("no file to store");
    }

    [Fact]
    public async Task WriteDerivedBundleFromStoreAsync_names_the_blobs_the_source_already_holds()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string produced = await CreateProducedFolderAsync(directory);
        await store.ImportBundleAsync(SourceName, produced, null, TestContext.Current.CancellationToken);
        ResolvedModel source = await store.ResolveAsync(SourceName, TestContext.Current.CancellationToken);
        int blobsBefore = Directory.GetFiles(directory.Paths.BlobsDirectory).Length;

        //Act
        await store.WriteDerivedBundleFromStoreAsync(
            DerivedName, source.Files, CreateProvenance(), false, TestContext.Current.CancellationToken);

        //Assert
        ResolvedModel derived = await store.ResolveAsync(DerivedName, TestContext.Current.CancellationToken);
        derived.Files.Select(file => file.Digest).Should().Equal(
            source.Files.Select(file => file.Digest).ToArray());
        //One more file in the blobs directory, and it is the new config: not one byte of content was copied.
        Directory.GetFiles(directory.Paths.BlobsDirectory).Should().HaveCount(blobsBefore + 1);
    }

    [Fact]
    public async Task DeleteAsync_of_a_derived_bundle_leaves_the_model_it_came_from_alone()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string produced = await CreateProducedFolderAsync(directory);
        await store.ImportBundleAsync(SourceName, produced, null, TestContext.Current.CancellationToken);
        ResolvedModel source = await store.ResolveAsync(SourceName, TestContext.Current.CancellationToken);
        await store.WriteDerivedBundleFromStoreAsync(
            DerivedName, source.Files, CreateProvenance(), false, TestContext.Current.CancellationToken);

        //Act
        await store.DeleteAsync(DerivedName, TestContext.Current.CancellationToken);

        //Assert
        (await store.ExistsAsync(DerivedName, TestContext.Current.CancellationToken)).Should().BeFalse();
        (await store.ExistsAsync(SourceName, TestContext.Current.CancellationToken)).Should().BeTrue();
        foreach (ResolvedFile file in source.Files)
        {
            File.Exists(file.BlobPath).Should().BeTrue();
        }
    }

    [Fact]
    public async Task MaterializeAsync_of_a_derived_bundle_writes_the_tree_the_tool_wrote()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string produced = await CreateProducedFolderAsync(directory);
        await store.WriteDerivedBundleAsync(
            DerivedName, produced, CreateProvenance(), false, TestContext.Current.CancellationToken);
        string target = Path.Combine(directory.DirectoryPath, "materialized");

        //Act
        IReadOnlyList<string> written = await store.MaterializeAsync(
            DerivedName, target, null, TestContext.Current.CancellationToken);

        //Assert
        written.Should().HaveCount(3);
        File.Exists(Path.Combine(target, "model.onnx")).Should().BeTrue();
        File.Exists(Path.Combine(target, "genai_config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_reports_a_derived_bundle_like_any_other_model()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string produced = await CreateProducedFolderAsync(directory);
        await store.ImportBundleAsync(SourceName, produced, null, TestContext.Current.CancellationToken);

        //Act
        await store.WriteDerivedBundleAsync(
            DerivedName, produced, CreateProvenance(), false, TestContext.Current.CancellationToken);

        //Assert
        IReadOnlyList<ModelSummary> listed = await store.ListAsync(TestContext.Current.CancellationToken);
        listed.Select(summary => summary.DisplayName).Should().Contain(DerivedName);
        listed.Should().HaveCount(2);
    }

    /// <summary>
    /// The provenance every test here writes: a checkpoint turned into a graph by a named tool at a
    /// named version, with the source's licence carried over.
    /// </summary>
    /// <returns>The provenance.</returns>
    private static DerivedProvenance CreateProvenance()
        => new DerivedProvenance(
            SourceName,
            "onnxruntime-genai",
            "0.15.2",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["route"] = "GenAiBuilder",
                ["precision"] = "int4"
            },
            BundleFormatDetector.Derived,
            new LicenseRecord("apache-2.0", "https://example.test/terms", null));

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
    /// Writes the folder a tool is imagined to have filled: a graph, the configuration that goes with
    /// it and a tokenizer.
    /// </summary>
    /// <param name="directory">The temporary directory the folder is written inside.</param>
    /// <returns>The absolute path of the folder.</returns>
    private static async Task<string> CreateProducedFolderAsync(TempStoreDirectory directory)
    {
        string root = Path.Combine(directory.DirectoryPath, "produced");
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(
            Path.Combine(root, "model.onnx"), "a graph, near enough for a test",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "genai_config.json"), "{\"model\":{\"type\":\"llama\"}}",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "tokenizer.json"), "{\"version\":\"1.0\"}",
            TestContext.Current.CancellationToken);

        return root;
    }
}
