using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers resolving a model name to the files on disk a runner loads, and to the text and parameter
/// layers that shape a conversation with it.
/// </summary>
public sealed class ModelStoreResolveTests
{
    [Fact]
    public async Task ResolveAsync_WithAPulledModel_ReturnsPathsThatExist()
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
        resolved.ModelPath.Should().Be(directory.Paths.GetBlobPath(builder.ModelDigest));
        File.Exists(resolved.ModelPath).Should().BeTrue();
        File.Exists(resolved.ManifestPath).Should().BeTrue();
        resolved.Template.Should().Be(builder.Template);
        resolved.System.Should().Be(builder.SystemPrompt);
        resolved.Parameters.Temperature.Should().Be(0.5f);
        resolved.Licenses.Should().Equal(new List<string> { "MIT" });
        resolved.Messages.Should().HaveCount(2);
        resolved.Config.ModelFamily.Should().Be("llama");
    }

    [Fact]
    public async Task ResolveAsync_WithoutExtraLayers_ReturnsEmptyListsAndNoDraft()
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
        resolved.ModelShardPaths.Should().BeEmpty();
        resolved.ProjectorPaths.Should().BeEmpty();
        resolved.AdapterPaths.Should().BeEmpty();
        resolved.DraftPath.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_WithAProjector_ReturnsTheProjectorPath()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder { IncludeProjector = true };
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        ResolvedModel resolved = await store.ResolveAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        resolved.ProjectorPaths.Should().HaveCount(1);
        resolved.ProjectorPaths[0].Should().Be(directory.Paths.GetBlobPath(builder.ProjectorDigest));
        File.Exists(resolved.ProjectorPaths[0]).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_WithAnAbsentModel_ThrowsModelNotFound()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => store.ResolveAsync("missing:latest", TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }
}
