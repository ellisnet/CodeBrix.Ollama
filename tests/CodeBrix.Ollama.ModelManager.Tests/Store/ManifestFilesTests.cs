using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Tests for <see cref="ManifestFiles"/>: the manifests directory is walked exactly four levels deep,
/// a manifest round-trips byte for byte, and deleting one leaves the directory tree tidy.
/// </summary>
public sealed class ManifestFilesTests
{
    private const string ConfigDigest = "sha256:1111111111111111111111111111111111111111111111111111111111111111";

    private const string ModelDigest = "sha256:2222222222222222222222222222222222222222222222222222222222222222";

    /// <summary>A manifest that has not been written has no file and reads back as nothing.</summary>
    [Fact]
    public async Task ReadAsync_for_missing_manifest_returns_null()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        StoredManifest stored = await ManifestFiles.ReadAsync(
            store.Paths,
            ModelName.Parse("llama3"),
            TestContext.Current.CancellationToken);

        //Assert
        stored.Should().BeNull();
    }

    /// <summary>Existence follows the file.</summary>
    [Fact]
    public async Task ExistsAsync_follows_the_file()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");

        //Act
        bool before = await ManifestFiles.ExistsAsync(store.Paths, name, TestContext.Current.CancellationToken);
        await ManifestFiles.WriteAsync(store.Paths, name, BuildManifest(), TestContext.Current.CancellationToken);
        bool after = await ManifestFiles.ExistsAsync(store.Paths, name, TestContext.Current.CancellationToken);

        //Assert
        before.Should().BeFalse();
        after.Should().BeTrue();
    }

    /// <summary>
    /// A manifest written and read back returns the exact bytes on disk, and its digest is the sha256
    /// of those bytes with no prefix, which is what Ollama's Manifest.Digest() reports.
    /// </summary>
    [Fact]
    public async Task WriteAsync_then_read_async_round_trips_bytes_and_digest()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");

        //Act
        await ManifestFiles.WriteAsync(store.Paths, name, BuildManifest(), TestContext.Current.CancellationToken);
        StoredManifest stored = await ManifestFiles.ReadAsync(store.Paths, name, TestContext.Current.CancellationToken);

        //Assert
        stored.Should().NotBeNull();
        byte[] onDisk = await File.ReadAllBytesAsync(stored.Path, TestContext.Current.CancellationToken);
        stored.RawBytes.Should().Equal(onDisk);
        stored.Digest.Should().Be(Convert.ToHexStringLower(SHA256.HashData(onDisk)));
        stored.Name.Should().Be(name);
        stored.Path.Should().Be(store.Paths.GetManifestPath(name));
    }

    /// <summary>The written document carries schema version 2 and the manifest media type.</summary>
    [Fact]
    public async Task WriteAsync_for_manifest_writes_schema_version_and_media_type()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");
        ModelManifest manifest = BuildManifest();
        manifest.SchemaVersion = 0;
        manifest.MediaType = "wrong";

        //Act
        await ManifestFiles.WriteAsync(store.Paths, name, manifest, TestContext.Current.CancellationToken);
        StoredManifest stored = await ManifestFiles.ReadAsync(store.Paths, name, TestContext.Current.CancellationToken);

        //Assert
        stored.Manifest.SchemaVersion.Should().Be(2);
        stored.Manifest.MediaType.Should().Be(MediaTypes.Manifest);
        stored.Manifest.Config.Digest.Should().Be(ConfigDigest);
        stored.Manifest.Layers.Should().HaveCount(1);
        stored.Manifest.Layers[0].Digest.Should().Be(ModelDigest);
    }

    /// <summary>The bytes handed to the raw overload are the bytes that end up on disk.</summary>
    [Fact]
    public async Task WriteAsync_for_raw_bytes_writes_them_verbatim()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");
        byte[] bytes = ModelManagerJson.SerializeLikeGo(BuildManifest());

        //Act
        await ManifestFiles.WriteAsync(store.Paths, name, bytes, TestContext.Current.CancellationToken);

        //Assert
        byte[] onDisk = await File.ReadAllBytesAsync(
            store.Paths.GetManifestPath(name),
            TestContext.Current.CancellationToken);
        onDisk.Should().Equal(bytes);
    }

    /// <summary>Writing over an existing manifest replaces it and leaves no temporary file behind.</summary>
    [Fact]
    public async Task WriteAsync_over_existing_manifest_replaces_it_and_leaves_no_temp_file()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");
        await ManifestFiles.WriteAsync(store.Paths, name, BuildManifest(), TestContext.Current.CancellationToken);

        ModelManifest replacement = BuildManifest();
        replacement.Layers[0].Size = 999;

        //Act
        await ManifestFiles.WriteAsync(store.Paths, name, replacement, TestContext.Current.CancellationToken);
        StoredManifest stored = await ManifestFiles.ReadAsync(store.Paths, name, TestContext.Current.CancellationToken);

        //Assert
        stored.Manifest.Layers[0].Size.Should().Be(999);
        string directory = Path.GetDirectoryName(store.Paths.GetManifestPath(name));
        Directory.GetFiles(directory).Should().HaveCount(1);
    }

    /// <summary>A file that is not JSON is reported as a manifest problem, not a JSON problem.</summary>
    [Fact]
    public async Task ReadAsync_for_corrupt_manifest_throws_model_manager_exception()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");
        await WriteCorruptAsync(store, name);

        //Act
        Func<Task> act = async () =>
            await ManifestFiles.ReadAsync(store.Paths, name, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelManagerException>();
    }

    /// <summary>The message names the file and says it is not valid JSON.</summary>
    [Fact]
    public async Task ReadAsync_for_corrupt_manifest_message_names_the_file()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");
        await WriteCorruptAsync(store, name);
        ModelManagerException caught = null;

        //Act
        try
        {
            await ManifestFiles.ReadAsync(store.Paths, name, TestContext.Current.CancellationToken);
        }
        catch (ModelManagerException exception)
        {
            caught = exception;
        }

        //Assert
        caught.Should().NotBeNull();
        caught.Message.Should().StartWith("manifest ");
        caught.Message.Should().Contain("is not valid JSON:");
        caught.Message.Should().Contain(store.Paths.GetManifestPath(name));
    }

    /// <summary>An empty store has nothing to list, even before the manifests directory exists.</summary>
    [Fact]
    public async Task EnumerateAsync_for_missing_manifests_directory_returns_empty()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        IReadOnlyList<StoredManifest> manifests = await ManifestFiles.EnumerateAsync(
            store.Paths,
            true,
            TestContext.Current.CancellationToken);

        //Assert
        manifests.Should().BeEmpty();
    }

    /// <summary>Every written manifest is listed and each one keeps the name it was filed under.</summary>
    [Fact]
    public async Task EnumerateAsync_round_trips_every_name()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName first = ModelName.Parse("llama3");
        ModelName second = ModelName.Parse("example.com/team/model:v1");
        await ManifestFiles.WriteAsync(store.Paths, first, BuildManifest(), TestContext.Current.CancellationToken);
        await ManifestFiles.WriteAsync(store.Paths, second, BuildManifest(), TestContext.Current.CancellationToken);

        //Act
        IReadOnlyList<StoredManifest> manifests = await ManifestFiles.EnumerateAsync(
            store.Paths,
            false,
            TestContext.Current.CancellationToken);

        //Assert
        manifests.Should().HaveCount(2);
        var names = new List<string>();
        foreach (StoredManifest stored in manifests)
        {
            names.Add(stored.Name.ToString());
        }
        names.Should().Contain(first.ToString());
        names.Should().Contain(second.ToString());
    }

    /// <summary>Only files exactly four levels below the manifests directory count as manifests.</summary>
    [Fact]
    public async Task EnumerateAsync_ignores_entries_at_the_wrong_depth()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");
        await ManifestFiles.WriteAsync(store.Paths, name, BuildManifest(), TestContext.Current.CancellationToken);

        string tooShallow = Path.Combine(store.Paths.ManifestsDirectory, "shallow-host", "namespace");
        Directory.CreateDirectory(tooShallow);
        await File.WriteAllTextAsync(
            Path.Combine(tooShallow, "model"),
            "{}",
            TestContext.Current.CancellationToken);

        string tooDeep = Path.Combine(store.Paths.ManifestsDirectory, "deep-host", "namespace", "model", "tag");
        Directory.CreateDirectory(tooDeep);
        await File.WriteAllTextAsync(
            Path.Combine(tooDeep, "extra"),
            "{}",
            TestContext.Current.CancellationToken);

        //Act
        IReadOnlyList<StoredManifest> manifests = await ManifestFiles.EnumerateAsync(
            store.Paths,
            false,
            TestContext.Current.CancellationToken);

        //Assert
        manifests.Should().HaveCount(1);
        manifests[0].Name.Should().Be(name);
    }

    /// <summary>A path that is not a valid model name is skipped when errors are tolerated.</summary>
    [Fact]
    public async Task EnumerateAsync_with_continue_on_error_skips_invalid_names()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        await ManifestFiles.WriteAsync(
            store.Paths,
            ModelName.Parse("llama3"),
            BuildManifest(),
            TestContext.Current.CancellationToken);
        await WriteFourLevelFileAsync(store, "host", "namespace", "model", ".bad-tag", "{}");

        //Act
        IReadOnlyList<StoredManifest> manifests = await ManifestFiles.EnumerateAsync(
            store.Paths,
            true,
            TestContext.Current.CancellationToken);

        //Assert
        manifests.Should().HaveCount(1);
    }

    /// <summary>A path that is not a valid model name stops the walk when errors are not tolerated.</summary>
    [Fact]
    public async Task EnumerateAsync_without_continue_on_error_throws_for_invalid_name()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        await WriteFourLevelFileAsync(store, "host", "namespace", "model", ".bad-tag", "{}");

        //Act
        Func<Task> act = async () =>
            await ManifestFiles.EnumerateAsync(store.Paths, false, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelManagerException>();
    }

    /// <summary>A corrupt manifest is skipped when errors are tolerated, so the good ones still list.</summary>
    [Fact]
    public async Task EnumerateAsync_with_continue_on_error_skips_corrupt_manifest()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        await ManifestFiles.WriteAsync(
            store.Paths,
            ModelName.Parse("llama3"),
            BuildManifest(),
            TestContext.Current.CancellationToken);
        await WriteCorruptAsync(store, ModelName.Parse("broken"));

        //Act
        IReadOnlyList<StoredManifest> manifests = await ManifestFiles.EnumerateAsync(
            store.Paths,
            true,
            TestContext.Current.CancellationToken);

        //Assert
        manifests.Should().HaveCount(1);
        manifests[0].Name.Should().Be(ModelName.Parse("llama3"));
    }

    /// <summary>A corrupt manifest stops the walk when errors are not tolerated.</summary>
    [Fact]
    public async Task EnumerateAsync_without_continue_on_error_throws_for_corrupt_manifest()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        await WriteCorruptAsync(store, ModelName.Parse("broken"));

        //Act
        Func<Task> act = async () =>
            await ManifestFiles.EnumerateAsync(store.Paths, false, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelManagerException>();
    }

    /// <summary>Deleting the only manifest removes the directories it left behind.</summary>
    [Fact]
    public async Task DeleteAsync_prunes_empty_parent_directories()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");
        await ManifestFiles.WriteAsync(store.Paths, name, BuildManifest(), TestContext.Current.CancellationToken);

        //Act
        await ManifestFiles.DeleteAsync(store.Paths, name, TestContext.Current.CancellationToken);

        //Assert
        File.Exists(store.Paths.GetManifestPath(name)).Should().BeFalse();
        Directory.Exists(Path.Combine(store.Paths.ManifestsDirectory, "registry.ollama.ai")).Should().BeFalse();
    }

    /// <summary>The manifests directory itself is never pruned, however empty it gets.</summary>
    [Fact]
    public async Task DeleteAsync_never_removes_the_manifests_directory()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName name = ModelName.Parse("llama3");
        await ManifestFiles.WriteAsync(store.Paths, name, BuildManifest(), TestContext.Current.CancellationToken);

        //Act
        await ManifestFiles.DeleteAsync(store.Paths, name, TestContext.Current.CancellationToken);

        //Assert
        Directory.Exists(store.Paths.ManifestsDirectory).Should().BeTrue();
    }

    /// <summary>Pruning stops as soon as a directory still holds something.</summary>
    [Fact]
    public async Task DeleteAsync_keeps_directories_that_still_hold_another_tag()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelName first = ModelName.Parse("llama3:latest");
        ModelName second = ModelName.Parse("llama3:8b");
        await ManifestFiles.WriteAsync(store.Paths, first, BuildManifest(), TestContext.Current.CancellationToken);
        await ManifestFiles.WriteAsync(store.Paths, second, BuildManifest(), TestContext.Current.CancellationToken);

        //Act
        await ManifestFiles.DeleteAsync(store.Paths, first, TestContext.Current.CancellationToken);

        //Assert
        File.Exists(store.Paths.GetManifestPath(first)).Should().BeFalse();
        File.Exists(store.Paths.GetManifestPath(second)).Should().BeTrue();
        Directory.Exists(Path.Combine(
            store.Paths.ManifestsDirectory,
            "registry.ollama.ai",
            "library",
            "llama3")).Should().BeTrue();
    }

    /// <summary>Deleting a manifest that is not there is not an error.</summary>
    [Fact]
    public async Task DeleteAsync_for_missing_manifest_does_nothing()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        await ManifestFiles.DeleteAsync(
            store.Paths,
            ModelName.Parse("llama3"),
            TestContext.Current.CancellationToken);

        //Assert
        Directory.Exists(store.Paths.ManifestsDirectory).Should().BeFalse();
    }

    /// <summary>Builds a small but complete manifest.</summary>
    /// <returns>The manifest.</returns>
    private static ModelManifest BuildManifest()
    {
        return new ModelManifest
        {
            Config = new ModelLayer(MediaTypes.Config, ConfigDigest, 42),
            Layers = new List<ModelLayer>
            {
                new ModelLayer(MediaTypes.Model, ModelDigest, 1024)
            }
        };
    }

    /// <summary>Writes a manifest file whose contents are not JSON.</summary>
    /// <param name="store">The store to write into.</param>
    /// <param name="name">The name to file the broken manifest under.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private static Task WriteCorruptAsync(TempStoreDirectory store, ModelName name)
    {
        return ManifestFiles.WriteAsync(
            store.Paths,
            name,
            Encoding.UTF8.GetBytes("{ this is not json"),
            TestContext.Current.CancellationToken);
    }

    /// <summary>Writes a file exactly four levels below the manifests directory.</summary>
    /// <param name="store">The store to write into.</param>
    /// <param name="host">The first level.</param>
    /// <param name="namespacePart">The second level.</param>
    /// <param name="model">The third level.</param>
    /// <param name="tag">The file name.</param>
    /// <param name="contents">The file contents.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private static async Task WriteFourLevelFileAsync(
        TempStoreDirectory store,
        string host,
        string namespacePart,
        string model,
        string tag,
        string contents)
    {
        string directory = Path.Combine(store.Paths.ManifestsDirectory, host, namespacePart, model);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, tag),
            contents,
            TestContext.Current.CancellationToken);
    }
}
