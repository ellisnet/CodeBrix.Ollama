using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers importing a folder already on disk as a bundle: the layers and the config it writes, the two
/// ways a file reaches the blobs directory, where the licence comes from, the second import that
/// changes nothing, and the folders that are refused. Nothing here touches a network.
/// </summary>
public sealed class ModelStoreBundleImportTests
{
    private const string ImportedName = "local/microsoft/museformer:lmd6remi-1";
    private const string LicenseText = "MIT License - the text the folder holds.";

    [Fact]
    public async Task ImportBundleAsync_records_one_layer_per_file_in_a_fixed_order()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string source = await CreateSourceFolderAsync(directory);

        //Act
        await store.ImportBundleAsync(ImportedName, source, null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(ImportedName, TestContext.Current.CancellationToken);
        info.Manifest.Layers.Select(layer => layer.Name).Should().Equal(
            "LICENSE", "config.json", "logs/train.log", "weights/model.safetensors");
        foreach (ModelLayer layer in info.Manifest.Layers)
        {
            layer.MediaType.Should().Be(MediaTypes.BundleFile);
            File.Exists(directory.Paths.GetBlobPath(layer.Digest)).Should().BeTrue();
        }
        info.Manifest.Layers[3].Size.Should().Be(Weights.Length);
        info.Manifest.Layers[3].Digest.Should().Be(FakeHubHandler.ComputeDigest(Weights));
    }

    [Fact]
    public async Task ImportBundleAsync_records_the_folder_the_format_and_the_licence_file()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string source = await CreateSourceFolderAsync(directory);

        //Act
        await store.ImportBundleAsync(ImportedName, source, null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(ImportedName, TestContext.Current.CancellationToken);
        info.Config.ModelFormat.Should().Be("pytorch");
        info.Config.ModelFamily.Should().Be("museformer");
        ModelStoreBundlePullTests.ConfigProperty(info, "source").Should().Be("local");
        ModelStoreBundlePullTests.ConfigProperty(info, "repository").Should().Be(source);
        ModelStoreBundlePullTests.IsNull(info, "revision").Should().BeTrue();
        ModelStoreBundlePullTests.IsNull(info, "licenseId").Should().BeTrue();
        ModelStoreBundlePullTests.ConfigProperty(info, "licenseSource").Should().Be("LICENSE");
        DateTimeOffset.TryParse(
            ModelStoreBundlePullTests.ConfigProperty(info, "pulledAt"), out DateTimeOffset _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task ImportBundleAsync_with_a_licence_of_its_own_records_that_instead()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string source = await CreateSourceFolderAsync(directory);
        var options = new ImportOptions
        {
            License = new LicenseRecord("mit", "https://example.test/terms", null)
        };

        //Act
        await store.ImportBundleAsync(ImportedName, source, options, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(ImportedName, TestContext.Current.CancellationToken);
        info.License.LicenseId.Should().Be("mit");
        info.License.LicenseSource.Should().Be("https://example.test/terms");
        ModelStoreBundlePullTests.ConfigProperty(info, "licenseId").Should().Be("mit");
    }

    [Fact]
    public async Task ImportBundleAsync_with_links_stores_the_same_bytes_as_a_copy_would()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string source = await CreateSourceFolderAsync(directory);
        var options = new ImportOptions { Link = true };

        //Act
        await store.ImportBundleAsync(ImportedName, source, options, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(ImportedName, TestContext.Current.CancellationToken);
        string blobPath = directory.Paths.GetBlobPath(info.Manifest.Layers[3].Digest);
        (await File.ReadAllBytesAsync(blobPath, TestContext.Current.CancellationToken)).Should().Equal(Weights);
        File.Exists(Path.Combine(source, "weights", "model.safetensors")).Should().BeTrue();
        Directory.GetFiles(directory.Paths.BlobsDirectory).Should().HaveCount(5);
    }

    [Fact]
    public async Task ImportBundleAsync_with_a_filter_leaves_the_excluded_files_behind()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string source = await CreateSourceFolderAsync(directory);
        var options = new ImportOptions { Filter = new FileFilter(null, new[] { "logs/**" }) };

        //Act
        await store.ImportBundleAsync(ImportedName, source, options, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(ImportedName, TestContext.Current.CancellationToken);
        info.Manifest.Layers.Select(layer => layer.Name).Should().Equal(
            "LICENSE", "config.json", "weights/model.safetensors");
    }

    [Fact]
    public async Task ImportBundleAsync_of_the_same_folder_twice_keeps_one_model_and_the_same_blobs()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string source = await CreateSourceFolderAsync(directory);
        await store.ImportBundleAsync(ImportedName, source, null, TestContext.Current.CancellationToken);
        ModelInfo first = await store.ShowAsync(ImportedName, TestContext.Current.CancellationToken);

        //Act
        await store.ImportBundleAsync(ImportedName, source, null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo second = await store.ShowAsync(ImportedName, TestContext.Current.CancellationToken);
        second.Manifest.Layers.Select(layer => layer.Digest)
            .Should().Equal(first.Manifest.Layers.Select(layer => layer.Digest).ToArray());
        (await store.ListAsync(TestContext.Current.CancellationToken)).Should().HaveCount(1);
        foreach (ModelLayer layer in second.Manifest.Layers)
        {
            File.Exists(directory.Paths.GetBlobPath(layer.Digest)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ImportBundleAsync_of_an_empty_folder_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string empty = Path.Combine(directory.DirectoryPath, "empty");
        Directory.CreateDirectory(empty);

        //Act
        Func<Task> act = () => store.ImportBundleAsync(
            ImportedName, empty, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("no file to import");
    }

    [Fact]
    public async Task ImportBundleAsync_of_a_folder_that_is_not_there_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string missing = Path.Combine(directory.DirectoryPath, "not-there");

        //Act
        Func<Task> act = () => store.ImportBundleAsync(
            ImportedName, missing, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("does not exist");
    }

    [Fact]
    public async Task ImportBundleAsync_without_a_directory_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));

        //Act
        Func<Task> act = () => store.ImportBundleAsync(
            ImportedName, "  ", null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("local/microsoft/museformer:lmd6remi-1", "local", "microsoft", "museformer", "lmd6remi-1")]
    [InlineData(
        "storage.googleapis.com/magentadata/music-transformer:unconditional-16",
        "storage.googleapis.com",
        "magentadata",
        "music-transformer",
        "unconditional-16")]
    [InlineData("hf.co/m-a-p/MuPT-v1-8192-190M:main", "hf.co", "m-a-p", "MuPT-v1-8192-190M", "main")]
    public void ModelName_parses_the_three_bundle_name_shapes(
        string text, string host, string @namespace, string model, string tag)
    {
        //Arrange
        ModelName.TryParse(text, out ModelName name).Should().BeTrue();

        //Act and assert
        name.Host.Should().Be(host);
        name.Namespace.Should().Be(@namespace);
        name.Model.Should().Be(model);
        name.Tag.Should().Be(tag);
    }

    /// <summary>
    /// The bytes of the weights file the folder holds.
    /// </summary>
    private static byte[] Weights
    {
        get { return ModelStoreBundlePullTests.Bytes("safetensors bytes, near enough for a test"); }
    }

    /// <summary>
    /// The options every import test uses: a temporary store directory and nothing else, because an
    /// import reaches no service at all.
    /// </summary>
    /// <param name="directory">The temporary store directory.</param>
    /// <returns>The options.</returns>
    private static ModelStoreOptions CreateOptions(TempStoreDirectory directory)
    {
        return new ModelStoreOptions
        {
            StoreDirectory = directory.DirectoryPath,
            DefaultRegistryHost = "registry.test"
        };
    }

    /// <summary>
    /// Writes the folder an import is given: a configuration file, a licence, a weights file one level
    /// down and a training log one level down, which is the shape of a publisher's own download.
    /// </summary>
    /// <param name="directory">The temporary directory the folder is written inside.</param>
    /// <returns>The absolute path of the folder.</returns>
    private static async Task<string> CreateSourceFolderAsync(TempStoreDirectory directory)
    {
        string root = Path.Combine(directory.DirectoryPath, "source");
        Directory.CreateDirectory(Path.Combine(root, "weights"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));

        await File.WriteAllTextAsync(
            Path.Combine(root, "config.json"), "{\"model_type\":\"museformer\"}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "LICENSE"), LicenseText, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(root, "weights", "model.safetensors"), Weights, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "logs", "train.log"), "epoch 1 loss 4.2", TestContext.Current.CancellationToken);

        return root;
    }
}
