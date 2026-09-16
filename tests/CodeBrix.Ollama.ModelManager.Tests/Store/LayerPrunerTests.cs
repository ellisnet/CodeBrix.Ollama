using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Tests for <see cref="LayerPruner"/>: a blob is only deleted once no manifest names it, and a file
/// in the blobs directory that this library did not write is left alone.
/// </summary>
public sealed class LayerPrunerTests
{
    /// <summary>A blob a manifest still names survives; one nothing names does not.</summary>
    [Fact]
    public async Task RemoveUnreferencedAsync_keeps_referenced_and_deletes_the_rest()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer config = await CreateBlobAsync(store, "config bytes", MediaTypes.Config);
        ModelLayer referenced = await CreateBlobAsync(store, "model bytes", MediaTypes.Model);
        ModelLayer orphan = await CreateBlobAsync(store, "orphan bytes", MediaTypes.Model);
        await WriteManifestAsync(store, "llama3", config, referenced);

        //Act
        IReadOnlyList<string> deleted = await LayerPruner.RemoveUnreferencedAsync(
            store.Paths,
            new[] { config.Digest, referenced.Digest, orphan.Digest },
            TestContext.Current.CancellationToken);

        //Assert
        deleted.Should().Equal(new[] { orphan.Digest });
        File.Exists(store.Paths.GetBlobPath(config.Digest)).Should().BeTrue();
        File.Exists(store.Paths.GetBlobPath(referenced.Digest)).Should().BeTrue();
        File.Exists(store.Paths.GetBlobPath(orphan.Digest)).Should().BeFalse();
    }

    /// <summary>A blob only the config layer names is still in use.</summary>
    [Fact]
    public async Task RemoveUnreferencedAsync_treats_the_config_layer_as_a_reference()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer config = await CreateBlobAsync(store, "config bytes", MediaTypes.Config);
        ModelLayer model = await CreateBlobAsync(store, "model bytes", MediaTypes.Model);
        await WriteManifestAsync(store, "llama3", config, model);

        //Act
        IReadOnlyList<string> deleted = await LayerPruner.RemoveUnreferencedAsync(
            store.Paths,
            new[] { config.Digest },
            TestContext.Current.CancellationToken);

        //Assert
        deleted.Should().BeEmpty();
        File.Exists(store.Paths.GetBlobPath(config.Digest)).Should().BeTrue();
    }

    /// <summary>A blob another model shares stays even when one of the models is gone.</summary>
    [Fact]
    public async Task RemoveUnreferencedAsync_keeps_blobs_shared_with_another_model()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer config = await CreateBlobAsync(store, "config bytes", MediaTypes.Config);
        ModelLayer shared = await CreateBlobAsync(store, "shared weights", MediaTypes.Model);
        await WriteManifestAsync(store, "other-model", config, shared);

        //Act
        IReadOnlyList<string> deleted = await LayerPruner.RemoveUnreferencedAsync(
            store.Paths,
            new[] { shared.Digest },
            TestContext.Current.CancellationToken);

        //Assert
        deleted.Should().BeEmpty();
        File.Exists(store.Paths.GetBlobPath(shared.Digest)).Should().BeTrue();
    }

    /// <summary>With nothing to consider there is nothing to do.</summary>
    [Fact]
    public async Task RemoveUnreferencedAsync_with_no_candidates_returns_empty()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        IReadOnlyList<string> deleted = await LayerPruner.RemoveUnreferencedAsync(
            store.Paths,
            Array.Empty<string>(),
            TestContext.Current.CancellationToken);

        //Assert
        deleted.Should().BeEmpty();
    }

    /// <summary>A corrupt manifest does not block the collection of blobs nothing else names.</summary>
    [Fact]
    public async Task RemoveUnreferencedAsync_ignores_corrupt_manifests()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer orphan = await CreateBlobAsync(store, "orphan bytes", MediaTypes.Model);
        await ManifestFiles.WriteAsync(
            store.Paths,
            ModelName.Parse("broken"),
            new byte[] { (byte)'{', (byte)'x' },
            TestContext.Current.CancellationToken);

        //Act
        IReadOnlyList<string> deleted = await LayerPruner.RemoveUnreferencedAsync(
            store.Paths,
            new[] { orphan.Digest },
            TestContext.Current.CancellationToken);

        //Assert
        deleted.Should().Equal(new[] { orphan.Digest });
        File.Exists(store.Paths.GetBlobPath(orphan.Digest)).Should().BeFalse();
    }

    /// <summary>A blob written more recently than the grace period is left alone.</summary>
    [Fact]
    public async Task PruneAllAsync_honours_the_grace_period()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer old = await CreateBlobAsync(store, "old orphan", MediaTypes.Model);
        ModelLayer fresh = await CreateBlobAsync(store, "fresh orphan", MediaTypes.Model);
        Age(store.Paths.GetBlobPath(old.Digest), TimeSpan.FromHours(2));

        //Act
        IReadOnlyList<string> deleted = await LayerPruner.PruneAllAsync(
            store.Paths,
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken);

        //Assert
        deleted.Should().Equal(new[] { old.Digest });
        File.Exists(store.Paths.GetBlobPath(old.Digest)).Should().BeFalse();
        File.Exists(store.Paths.GetBlobPath(fresh.Digest)).Should().BeTrue();
    }

    /// <summary>An old blob a manifest still names is not collected.</summary>
    [Fact]
    public async Task PruneAllAsync_keeps_old_but_referenced_blobs()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer config = await CreateBlobAsync(store, "config bytes", MediaTypes.Config);
        ModelLayer model = await CreateBlobAsync(store, "model bytes", MediaTypes.Model);
        await WriteManifestAsync(store, "llama3", config, model);
        Age(store.Paths.GetBlobPath(config.Digest), TimeSpan.FromDays(7));
        Age(store.Paths.GetBlobPath(model.Digest), TimeSpan.FromDays(7));

        //Act
        IReadOnlyList<string> deleted = await LayerPruner.PruneAllAsync(
            store.Paths,
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken);

        //Assert
        deleted.Should().BeEmpty();
        File.Exists(store.Paths.GetBlobPath(config.Digest)).Should().BeTrue();
        File.Exists(store.Paths.GetBlobPath(model.Digest)).Should().BeTrue();
    }

    /// <summary>This library's own partial-download leftovers are cleaned up once they are old enough.</summary>
    [Fact]
    public async Task PruneAllAsync_deletes_this_librarys_own_sidecars()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer blob = await CreateBlobAsync(store, "some bytes", MediaTypes.Model);
        string dataPath = store.Paths.GetPartialDataPath(blob.Digest);
        string statePath = store.Paths.GetPartialStatePath(blob.Digest);
        await File.WriteAllTextAsync(dataPath, "partial", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(statePath, "{}", TestContext.Current.CancellationToken);
        Age(dataPath, TimeSpan.FromHours(2));
        Age(statePath, TimeSpan.FromHours(2));

        //Act
        await LayerPruner.PruneAllAsync(
            store.Paths,
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken);

        //Assert
        File.Exists(dataPath).Should().BeFalse();
        File.Exists(statePath).Should().BeFalse();
    }

    /// <summary>A sidecar that is still within the grace period is a download in progress and stays.</summary>
    [Fact]
    public async Task PruneAllAsync_keeps_fresh_sidecars()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer blob = await CreateBlobAsync(store, "some bytes", MediaTypes.Model);
        string dataPath = store.Paths.GetPartialDataPath(blob.Digest);
        await File.WriteAllTextAsync(dataPath, "partial", TestContext.Current.CancellationToken);

        //Act
        await LayerPruner.PruneAllAsync(
            store.Paths,
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken);

        //Assert
        File.Exists(dataPath).Should().BeTrue();
    }

    /// <summary>
    /// A file in the blobs directory that this library did not write is never deleted, however old it
    /// is, because the same directory may belong to a real Ollama install.
    /// </summary>
    [Fact]
    public async Task PruneAllAsync_never_deletes_foreign_files()
    {
        //Arrange
        using var store = new TempStoreDirectory();
        ModelLayer blob = await CreateBlobAsync(store, "some bytes", MediaTypes.Model);
        string ollamaPartial = store.Paths.GetBlobPath(blob.Digest) + "-partial";
        string ollamaPart = store.Paths.GetBlobPath(blob.Digest) + "-partial-3";
        string unknown = Path.Combine(store.Paths.BlobsDirectory, "something-else.txt");
        await File.WriteAllTextAsync(ollamaPartial, "x", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(ollamaPart, "x", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(unknown, "x", TestContext.Current.CancellationToken);
        Age(ollamaPartial, TimeSpan.FromDays(30));
        Age(ollamaPart, TimeSpan.FromDays(30));
        Age(unknown, TimeSpan.FromDays(30));

        //Act
        await LayerPruner.PruneAllAsync(
            store.Paths,
            TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken);

        //Assert
        File.Exists(ollamaPartial).Should().BeTrue();
        File.Exists(ollamaPart).Should().BeTrue();
        File.Exists(unknown).Should().BeTrue();
    }

    /// <summary>An empty store has nothing to prune.</summary>
    [Fact]
    public async Task PruneAllAsync_for_missing_blobs_directory_returns_empty()
    {
        //Arrange
        using var store = new TempStoreDirectory();

        //Act
        IReadOnlyList<string> deleted = await LayerPruner.PruneAllAsync(
            store.Paths,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken);

        //Assert
        deleted.Should().BeEmpty();
    }

    /// <summary>Stores text as a blob and returns the layer that names it.</summary>
    /// <param name="store">The store to write into.</param>
    /// <param name="text">The content.</param>
    /// <param name="mediaType">The media type of the layer.</param>
    /// <returns>The layer.</returns>
    private static Task<ModelLayer> CreateBlobAsync(TempStoreDirectory store, string text, string mediaType)
    {
        return LayerFactory.CreateFromTextAsync(
            store.Paths,
            text,
            mediaType,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Writes a manifest naming one config layer and one content layer.</summary>
    /// <param name="store">The store to write into.</param>
    /// <param name="name">The model name.</param>
    /// <param name="config">The config layer.</param>
    /// <param name="layer">The content layer.</param>
    /// <returns>A task that completes when the manifest is written.</returns>
    private static Task WriteManifestAsync(
        TempStoreDirectory store,
        string name,
        ModelLayer config,
        ModelLayer layer)
    {
        var manifest = new ModelManifest
        {
            Config = config,
            Layers = new List<ModelLayer> { layer }
        };
        return ManifestFiles.WriteAsync(
            store.Paths,
            ModelName.Parse(name),
            manifest,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Backdates a file so that pruning considers it old.</summary>
    /// <param name="path">The file to backdate.</param>
    /// <param name="age">How far into the past to move its last write time.</param>
    private static void Age(string path, TimeSpan age)
    {
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
    }
}
