using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the store-wide garbage collection: unreferenced blobs and this library's own download
/// sidecars go once they are older than the grace period, and everything else stays.
/// </summary>
public sealed class ModelStorePruneTests
{
    [Fact]
    public async Task PruneAsync_removes_old_unreferenced_blobs_and_own_sidecars_only()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        string orphanDigest = Sha256Digest.Compute(new byte[] { 1, 2, 3 });
        string orphanPath = directory.Paths.GetBlobPath(orphanDigest);
        string sidecarPath = directory.Paths.GetPartialStatePath(orphanDigest);
        string foreignPath = Path.Combine(directory.Paths.BlobsDirectory, "sha256-0000-partial");
        string youngOrphanDigest = Sha256Digest.Compute(new byte[] { 4, 5, 6 });
        string youngOrphanPath = directory.Paths.GetBlobPath(youngOrphanDigest);
        await File.WriteAllBytesAsync(orphanPath, new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(sidecarPath, "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(foreignPath, "not ours", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(youngOrphanPath, new byte[] { 4, 5, 6 }, TestContext.Current.CancellationToken);
        DateTime old = DateTime.UtcNow.AddHours(-3);
        File.SetLastWriteTimeUtc(orphanPath, old);
        File.SetLastWriteTimeUtc(sidecarPath, old);
        File.SetLastWriteTimeUtc(foreignPath, old);

        //Act
        IReadOnlyList<string> removed = await store.PruneAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        //Assert
        removed.Should().HaveCount(1);
        removed[0].Should().Be(orphanDigest);
        File.Exists(orphanPath).Should().BeFalse();
        File.Exists(sidecarPath).Should().BeFalse();
        File.Exists(foreignPath).Should().BeTrue();
        File.Exists(youngOrphanPath).Should().BeTrue();
        File.Exists(directory.Paths.GetBlobPath(builder.ModelDigest)).Should().BeTrue();
        (await store.ExistsAsync(builder.Reference, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task PruneAsync_with_a_negative_grace_period_throws()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(new ModelStoreOptions { StoreDirectory = directory.DirectoryPath });

        //Act
        Func<Task> act = () => store.PruneAsync(TimeSpan.FromSeconds(-1), TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
