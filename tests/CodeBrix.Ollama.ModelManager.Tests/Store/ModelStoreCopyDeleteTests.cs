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
    public async Task CopyAsync_CreatesASecondManifestThatSharesEveryBlob()
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
    public async Task CopyAsync_WithTheSameName_DoesNothing()
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
    public async Task CopyAsync_WithAnAbsentSource_ThrowsModelNotFound()
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
    public async Task DeleteAsync_WithACopyStillPresent_KeepsTheSharedBlobs()
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
    public async Task DeleteAsync_OfTheLastName_RemovesEveryBlob()
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
    public async Task DeleteAsync_WithAnAbsentModel_ThrowsModelNotFound()
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
