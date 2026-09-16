using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the operations a bundle was meant to leave alone: it is listed beside a GGUF model and told
/// apart from one by its format, it is described without anything trying to read weights it does not
/// have, it is copied, deleted and pruned by the same code paths as every other model, and its licence
/// is reported both as what the source stated and as the text the publisher shipped.
/// </summary>
public sealed class ModelStoreBundleListShowDeletePruneTests
{
    private const string BundleName = "hf.co/m-a-p/MuPT-v1-8192-190M:main";

    [Fact]
    public async Task ListAsync_shows_a_bundle_beside_a_gguf_model_with_its_own_format()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler hub = ModelStoreBundlePullTests.CreateHub();
        using var registry = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(registry);
        using var registryStore = new ModelStore(ModelStorePullTests.CreateOptions(registry, directory));
        using var bundleStore = new ModelStore(ModelStoreBundlePullTests.CreateOptions(hub, directory));
        await ModelStorePullTests.CollectStatusesAsync(registryStore, builder.Reference);
        await PullBundleAsync(bundleStore);

        //Act
        IReadOnlyList<ModelSummary> models = await bundleStore.ListAsync(TestContext.Current.CancellationToken);

        //Assert
        models.Should().HaveCount(2);
        ModelSummary bundle = models.First(model => model.DisplayName == BundleName);
        ModelSummary gguf = models.First(model => model.DisplayName == "registry.test/library/test:latest");
        bundle.Config.ModelFormat.Should().Be("pytorch");
        gguf.Config.ModelFormat.Should().Be("gguf");
        bundle.Size.Should().BeGreaterThan(0L);
        bundle.Digest.Should().NotBeNull();
    }

    [Fact]
    public async Task ShowAsync_with_a_bundle_reports_the_format_the_licence_and_the_licence_text()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);

        //Act
        ModelInfo info = await store.ShowAsync(BundleName, TestContext.Current.CancellationToken);

        //Assert
        info.Format.Should().Be("pytorch");
        info.License.LicenseId.Should().Be("apache-2.0");
        info.License.LicenseSource.Should().Be("https://huggingface.co/m-a-p/MuPT-v1-8192-190M");
        info.Licenses.Should().Equal(ModelStoreBundlePullTests.LicenseText);
    }

    [Fact]
    public async Task ShowAsync_with_a_bundle_reads_no_weights_metadata()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);

        //Act
        ModelInfo info = await store.ShowAsync(BundleName, TestContext.Current.CancellationToken);

        //Assert
        info.Metadata.Should().BeNull();
        info.ProjectorMetadata.Should().BeEmpty();
        info.Template.Should().BeNull();
        info.Capabilities.Should().BeEmpty();
        info.Size.Should().Be(info.Manifest.GetTotalSize());
    }

    [Fact]
    public async Task ShowAsync_with_a_gguf_model_still_reports_no_licence_record_and_the_gguf_format()
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
        info.Format.Should().Be("gguf");
        info.License.IsStated.Should().BeFalse();
        info.Licenses.Should().Equal(new List<string> { "MIT" });
    }

    [Fact]
    public async Task ExistsAsync_reports_a_bundle_the_way_it_reports_a_model()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));

        //Act
        bool before = await store.ExistsAsync(BundleName, TestContext.Current.CancellationToken);
        await PullBundleAsync(store);
        bool after = await store.ExistsAsync(BundleName, TestContext.Current.CancellationToken);

        //Assert
        before.Should().BeFalse();
        after.Should().BeTrue();
    }

    [Fact]
    public async Task CopyAsync_gives_a_bundle_a_second_name_that_shares_every_blob()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);
        int blobsBefore = Directory.GetFiles(directory.Paths.BlobsDirectory).Length;

        //Act
        await store.CopyAsync(BundleName, "local/mirror/mupt:copy", TestContext.Current.CancellationToken);

        //Assert
        (await store.ListAsync(TestContext.Current.CancellationToken)).Should().HaveCount(2);
        Directory.GetFiles(directory.Paths.BlobsDirectory).Length.Should().Be(blobsBefore);
        ResolvedModel copy = await store.ResolveAsync(
            "local/mirror/mupt:copy", TestContext.Current.CancellationToken);
        ResolvedModel original = await store.ResolveAsync(BundleName, TestContext.Current.CancellationToken);
        copy.Files.Select(file => file.BlobPath)
            .Should().Equal(original.Files.Select(file => file.BlobPath).ToArray());
    }

    [Fact]
    public async Task DeleteAsync_of_a_bundle_keeps_the_blobs_a_copy_still_names()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);
        await store.CopyAsync(BundleName, "local/mirror/mupt:copy", TestContext.Current.CancellationToken);
        ResolvedModel resolved = await store.ResolveAsync(BundleName, TestContext.Current.CancellationToken);

        //Act
        await store.DeleteAsync(BundleName, TestContext.Current.CancellationToken);

        //Assert
        (await store.ExistsAsync(BundleName, TestContext.Current.CancellationToken)).Should().BeFalse();
        File.Exists(resolved.Files[1].BlobPath).Should().BeTrue();
        await store.DeleteAsync("local/mirror/mupt:copy", TestContext.Current.CancellationToken);
        (await store.ListAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
        Directory.GetFiles(directory.Paths.BlobsDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task PruneAsync_keeps_every_blob_a_bundle_manifest_still_names()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using FakeHubHandler handler = ModelStoreBundlePullTests.CreateHub();
        using var store = new ModelStore(ModelStoreBundlePullTests.CreateOptions(handler, directory));
        await PullBundleAsync(store);
        ResolvedModel resolved = await store.ResolveAsync(BundleName, TestContext.Current.CancellationToken);
        string orphanDigest = Sha256Digest.Compute(new byte[] { 9, 8, 7 });
        string orphanPath = directory.Paths.GetBlobPath(orphanDigest);
        await File.WriteAllBytesAsync(orphanPath, new byte[] { 9, 8, 7 }, TestContext.Current.CancellationToken);

        // Every blob is backdated, so that the prune considers all of them and only the manifest keeps
        // the bundle's own files alive.
        DateTime old = DateTime.UtcNow.AddHours(-3);
        foreach (string file in Directory.GetFiles(directory.Paths.BlobsDirectory))
        {
            File.SetLastWriteTimeUtc(file, old);
        }

        //Act
        IReadOnlyList<string> removed = await store.PruneAsync(
            TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        //Assert
        removed.Should().HaveCount(1);
        removed[0].Should().Be(orphanDigest);
        File.Exists(orphanPath).Should().BeFalse();
        foreach (ResolvedFile file in resolved.Files)
        {
            File.Exists(file.BlobPath).Should().BeTrue();
        }
        (await store.ExistsAsync(BundleName, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    /// <summary>
    /// Pulls the fake Hugging Face bundle every test here works with.
    /// </summary>
    /// <param name="store">The store to pull into.</param>
    /// <returns>A task that completes when the bundle is in the store.</returns>
    private static Task<IReadOnlyList<PullProgress>> PullBundleAsync(IModelStore store)
    {
        return ModelStoreBundlePullTests.RunPullAsync(
            store, BundleName, PullOptions.ForHuggingFace(null, null, null));
    }
}
