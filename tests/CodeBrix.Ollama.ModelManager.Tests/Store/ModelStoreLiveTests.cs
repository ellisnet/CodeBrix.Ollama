using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// The one test that really reaches registry.ollama.ai. It is skipped unless
/// CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 is set, because every other test in this project is offline and
/// self-contained. It downloads a small model (about 92 MB), reads it back and removes it again, all
/// inside a temporary directory.
/// </summary>
public sealed class ModelStoreLiveTests
{
    private const string ModelName = "smollm:135m";

    [EnvGatedFact("CODEBRIX_OLLAMA_RUN_LIVE_TESTS")]
    public async Task PullAsync_from_the_real_registry_downloads_resolves_lists_and_deletes_the_model()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(new ModelStoreOptions { StoreDirectory = directory.DirectoryPath });

        //Act
        var statuses = new List<string>();
        await foreach (PullProgress report in store.PullAsync(ModelName, TestContext.Current.CancellationToken))
        {
            statuses.Add(report.Status);
        }

        //Assert
        statuses[0].Should().Be("pulling manifest");
        statuses[statuses.Count - 1].Should().Be("success");

        ResolvedModel resolved = await store.ResolveAsync(ModelName, TestContext.Current.CancellationToken);
        File.Exists(resolved.ModelPath).Should().BeTrue();

        GgufMetadata metadata = await GgufMetadata.ReadAsync(
            resolved.ModelPath, null, TestContext.Current.CancellationToken);
        metadata.Architecture.Should().Be("llama");

        ModelInfo info = await store.ShowAsync(ModelName, TestContext.Current.CancellationToken);
        ModelLayer modelLayer = info.Manifest.Layers.First(layer => layer.MediaType == MediaTypes.Model);
        new FileInfo(resolved.ModelPath).Length.Should().Be(modelLayer.Size);

        IReadOnlyList<ModelSummary> models = await store.ListAsync(TestContext.Current.CancellationToken);
        models.Should().HaveCount(1);
        models[0].DisplayName.Should().Be(ModelName);

        await store.DeleteAsync(ModelName, TestContext.Current.CancellationToken);
        (await store.ListAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
        Directory.GetFiles(directory.Paths.BlobsDirectory)
            .Where(path => Path.GetFileName(path).StartsWith("sha256-", StringComparison.Ordinal))
            .Should().BeEmpty();
    }
}
