using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers giving a model a second name and removing one: a copy shares every blob, and a delete only
/// removes the blobs no remaining manifest still names.
/// </summary>
public sealed class ModelStoreCopyDeleteTests
{
    [Fact]
    public async Task CopyAsync_creates_a_second_manifest_that_shares_every_blob()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        await store.CopyAsync(builder.Reference, "copy:latest", TestContext.Current.CancellationToken);

        //Assert
        IReadOnlyList<ModelSummary> models = await store.ListAsync(TestContext.Current.CancellationToken);
        models.Should().HaveCount(2);
        string copyManifest = Path.Combine(
            directory.DirectoryPath, "manifests", "registry.test", "library", "copy", "latest");
        (await File.ReadAllBytesAsync(copyManifest, TestContext.Current.CancellationToken))
            .Should().Equal(builder.ManifestBytes);
        File.Exists(directory.Paths.GetBlobPath(builder.ModelDigest)).Should().BeTrue();
    }

    [Fact]
    public async Task CopyAsync_with_the_same_name_does_nothing()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        await store.CopyAsync(builder.Reference, builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        IReadOnlyList<ModelSummary> models = await store.ListAsync(TestContext.Current.CancellationToken);
        models.Should().HaveCount(1);
    }

    [Fact]
    public async Task CopyAsync_with_an_absent_source_throws_model_not_found()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => store.CopyAsync("missing:latest", "copy:latest", TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_with_a_copy_still_present_keeps_the_shared_blobs()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);
        await store.CopyAsync(builder.Reference, "copy:latest", TestContext.Current.CancellationToken);

        //Act
        await store.DeleteAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        (await store.ExistsAsync(builder.Reference, TestContext.Current.CancellationToken)).Should().BeFalse();
        (await store.ExistsAsync("copy:latest", TestContext.Current.CancellationToken)).Should().BeTrue();
        File.Exists(directory.Paths.GetBlobPath(builder.ModelDigest)).Should().BeTrue();
        File.Exists(directory.Paths.GetBlobPath(builder.ConfigDigest)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_of_the_last_name_removes_every_blob()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);
        await store.CopyAsync(builder.Reference, "copy:latest", TestContext.Current.CancellationToken);
        await store.DeleteAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Act
        await store.DeleteAsync("copy:latest", TestContext.Current.CancellationToken);

        //Assert
        (await store.ListAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
        Directory.GetFiles(directory.Paths.BlobsDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_with_an_absent_model_throws_model_not_found()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => store.DeleteAsync("missing:latest", TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }
}
