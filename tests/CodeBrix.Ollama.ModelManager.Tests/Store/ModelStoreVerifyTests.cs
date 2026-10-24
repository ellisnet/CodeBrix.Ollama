using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers re-hashing a stored model at rest: an intact model, a blob whose content changed, a blob that
/// went missing, and a model that is not there at all.
/// </summary>
public sealed class ModelStoreVerifyTests
{
    [Fact]
    public async Task VerifyAsync_with_an_untouched_model_reports_it_intact()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        VerifyResult result = await store.VerifyAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        result.IsIntact.Should().BeTrue();
        result.Failures.Should().BeEmpty();
        result.BlobsChecked.Should().BeGreaterThan(1);
        result.BytesHashed.Should().BeGreaterThan(0L);
    }

    [Fact]
    public async Task VerifyAsync_with_a_changed_blob_reports_it_and_leaves_it_in_place()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);
        string blobPath = directory.Paths.GetBlobPath(builder.ModelDigest);
        byte[] bytes = await File.ReadAllBytesAsync(blobPath, TestContext.Current.CancellationToken);
        bytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(blobPath, bytes, TestContext.Current.CancellationToken);

        //Act
        VerifyResult result = await store.VerifyAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        result.IsIntact.Should().BeFalse();
        result.Failures.Should().HaveCount(1);
        VerifyFailure failure = result.Failures[0];
        failure.Problem.Should().Be(VerifyProblem.DigestMismatch);
        failure.Digest.Should().Be(builder.ModelDigest);
        failure.MediaType.Should().Be(MediaTypes.Model);
        failure.ActualDigest.Should().Be(FakeHubHandler.ComputeDigest(bytes));
        failure.ActualSize.Should().Be(bytes.Length);
        File.Exists(blobPath).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_with_a_missing_blob_reports_it_missing_and_checks_the_rest()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);
        File.Delete(directory.Paths.GetBlobPath(builder.TemplateDigest));

        //Act
        VerifyResult result = await store.VerifyAsync(builder.Reference, TestContext.Current.CancellationToken);

        //Assert
        result.Failures.Should().HaveCount(1);
        result.Failures[0].Problem.Should().Be(VerifyProblem.Missing);
        result.Failures[0].Digest.Should().Be(builder.TemplateDigest);
        result.Failures[0].ActualDigest.Should().BeNull();
        result.Failures[0].ActualSize.Should().Be(-1L);
    }

    [Fact]
    public async Task VerifyAsync_of_a_hugging_face_bundle_names_the_bad_file_by_its_path()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        const string name = "hf.co/m-a-p/MuPT-v1-8192-190M:main";
        await ModelStoreBundlePullTests.RunPullAsync(store, name, PullOptions.ForHuggingFace(null, null, null));
        ResolvedModel resolved = await store.ResolveAsync(name, TestContext.Current.CancellationToken);
        ResolvedFile config = resolved.Files.First(file => file.Name == "config.json");
        await File.WriteAllTextAsync(config.BlobPath, "{\"tampered\":true}", TestContext.Current.CancellationToken);

        //Act
        VerifyResult result = await store.VerifyAsync(name, TestContext.Current.CancellationToken);

        //Assert
        result.Failures.Should().HaveCount(1);
        result.Failures[0].LayerName.Should().Be("config.json");
        result.Failures[0].Problem.Should().Be(VerifyProblem.DigestMismatch);
    }

    [Fact]
    public async Task VerifyAsync_of_a_model_that_is_not_in_the_store_throws()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => store.VerifyAsync("nothing-here:latest", TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }
}
