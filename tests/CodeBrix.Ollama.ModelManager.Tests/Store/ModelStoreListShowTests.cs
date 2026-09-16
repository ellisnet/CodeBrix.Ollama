using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers listing the models a store holds and describing one in full: the summaries, the decoded text
/// and JSON layers, the GGUF metadata, the inferred capabilities and the reconstructed Modelfile.
/// </summary>
public sealed class ModelStoreListShowTests
{
    [Fact]
    public async Task ListAsync_AfterPullingTwoModels_ReturnsBothNewestFirst()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var older = new FakeModelBuilder("library", "alpha");
        var newer = new FakeModelBuilder("library", "beta");
        older.Build(handler);
        newer.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, older.Reference);
        await ModelStorePullTests.CollectStatusesAsync(store, newer.Reference);
        AgeManifest(directory, "alpha", TimeSpan.FromHours(1));

        //Act
        IReadOnlyList<ModelSummary> models = await store.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        models.Should().HaveCount(2);
        models[0].DisplayName.Should().Be("registry.test/library/beta:latest");
        models[1].DisplayName.Should().Be("registry.test/library/alpha:latest");
        models[0].Config.ModelFormat.Should().Be("gguf");
        models[0].Config.ModelFamily.Should().Be("llama");
        models[0].Config.FileType.Should().Be("Q8_0");
        models[0].Size.Should().BeGreaterThan(0L);
        models[0].Digest.Should().NotBeNull();
    }

    [Fact]
    public async Task ListAsync_WithAMissingConfigBlob_StillListsTheModel()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);
        File.Delete(directory.Paths.GetBlobPath(builder.ConfigDigest));

        //Act
        IReadOnlyList<ModelSummary> models = await store.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        models.Should().HaveCount(1);
        models[0].DisplayName.Should().Be("registry.test/library/test:latest");
        models[0].Config.Should().NotBeNull();
        models[0].Config.ModelFormat.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_WithAnEmptyStore_ReturnsNothing()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        IReadOnlyList<ModelSummary> models = await store.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        models.Should().BeEmpty();
    }

    [Fact]
    public async Task ExistsAsync_ReportsWhetherTheManifestIsThere()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        bool before = await store.ExistsAsync(builder.Reference, TestContext.Current.CancellationToken);
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);
        bool after = await store.ExistsAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        before.Should().BeFalse();
        after.Should().BeTrue();
    }

    [Fact]
    public async Task ShowAsync_WithAPulledModel_DecodesEveryLayer()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        ModelInfo info = await store.ShowAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        info.DisplayName.Should().Be("registry.test/library/test:latest");
        info.Template.Should().Be(builder.Template);
        info.System.Should().Be(builder.SystemPrompt);
        info.Parameters.Temperature.Should().Be(0.5f);
        info.Parameters.Stop.Should().Equal(new List<string> { "</s>" });
        info.Licenses.Should().Equal(new List<string> { "MIT" });
        info.Messages.Should().HaveCount(2);
        info.Messages[0].Role.Should().Be("user");
        info.Messages[0].Content.Should().Be("hi");
    }

    [Fact]
    public async Task ShowAsync_WithAPulledModel_ReadsTheGgufMetadata()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        ModelInfo info = await store.ShowAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        info.Metadata.Should().NotBeNull();
        info.Metadata.Architecture.Should().Be("llama");
        info.Metadata.ContextLength.Should().Be(2048UL);
        info.Metadata.EmbeddingLength.Should().Be(64UL);
        info.Metadata.ChatTemplate.Should().Be(builder.ChatTemplate);
        info.ProjectorMetadata.Should().BeEmpty();
    }

    [Fact]
    public async Task ShowAsync_WithAToolChatTemplate_InfersCompletionAndTools()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        ModelInfo info = await store.ShowAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        info.Capabilities.Should().Contain(ModelCapability.Completion);
        info.Capabilities.Should().Contain(ModelCapability.Tools);
        info.Capabilities.Should().NotContain(ModelCapability.Vision);
        info.Capabilities.Should().NotContain(ModelCapability.Embedding);
    }

    [Fact]
    public async Task ShowAsync_WithAProjector_InfersVision()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder { IncludeProjector = true };
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        ModelInfo info = await store.ShowAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        info.ProjectorMetadata.Should().HaveCount(1);
        info.ProjectorMetadata[0].Architecture.Should().Be("clip");
        info.Capabilities.Should().Contain(ModelCapability.Vision);
        info.Capabilities.Should().NotContain(ModelCapability.Audio);
    }

    [Fact]
    public async Task ShowAsync_WithAPulledModel_RendersTheModelfileText()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        ModelInfo info = await store.ShowAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        info.ModelfileText.Should().StartWith("FROM " + directory.Paths.GetBlobPath(builder.ModelDigest));
        info.ModelfileText.Should().Contain("TEMPLATE ");
        info.ModelfileText.Should().Contain("SYSTEM ");
        info.ModelfileText.Should().Contain("PARAMETER stop </s>");
        info.ModelfileText.Should().Contain("PARAMETER temperature 0.5");
        info.ModelfileText.Should().Contain("LICENSE MIT");
        info.ModelfileText.Should().Contain("MESSAGE user hi");
    }

    [Fact]
    public async Task ShowAsync_WithAnAbsentModel_ThrowsModelNotFound()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => store.ShowAsync("missing:latest", TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }

    [Fact]
    public async Task ShowAsync_WithABlankName_ThrowsArgumentException()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => store.ShowAsync("  ", TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ShowAsync_WithAnUnparsableName_ThrowsInvalidModelName()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => store.ShowAsync("a/b/c/d/e", TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<InvalidModelNameException>();
    }

    /// <summary>
    /// Backdates a model's manifest file so that the list order is decided rather than a coin toss
    /// between two files written in the same second.
    /// </summary>
    /// <param name="directory">The store directory.</param>
    /// <param name="model">The model part of the name.</param>
    /// <param name="age">How far back to set the last write time.</param>
    private static void AgeManifest(TempStoreDirectory directory, string model, TimeSpan age)
    {
        string path = Path.Combine(
            directory.DirectoryPath, "manifests", "registry.test", "library", model, "latest");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
    }
}
