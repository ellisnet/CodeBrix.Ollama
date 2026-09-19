using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.ModelAccess.Models;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// The whole life of a staged model, run against a registry that exists only in memory and a folder
/// under the system temporary directory that is thrown away afterwards. Nothing here touches the folder
/// the application really uses and nothing here reaches the network.
/// </summary>
public class ModelStagerTests
{
    /// <summary>A digest of the right shape that is not the one the tiny model's weights carry.</summary>
    private const string OtherDigest =
        "0000000000000000000000000000000000000000000000000000000000000001";

    /// <summary>A brand-new workstation reports nothing, and the check itself creates nothing.</summary>
    [Fact]
    public async Task CheckAsync_reports_nothing_present_and_creates_nothing()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();

        //Act
        ModelStagingStatus status = await stager.CheckAsync(TestContext.Current.CancellationToken);

        //Assert
        status.State.Should().Be(ModelStagingState.NotPresent);
        status.IsReady.Should().BeFalse();
        status.ModelPath.Should().BeNull();
        status.ProjectorPaths.Should().BeEmpty();
        status.WeightsDigest.Should().BeNull();
        status.MatchesPinnedWeights.Should().BeNull();
        status.BytesOnDisk.Should().Be(0L);
        status.ExpectedTotalBytes.Should().Be(harness.TotalBytes);
        status.StoreDirectory.Should().Be(harness.StoreDirectory);
        status.Detail.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(harness.RootDirectory).Should().BeFalse();
        Directory.Exists(harness.StoreDirectory).Should().BeFalse();
        harness.Registry.Requests.Should().BeEmpty();
    }

    /// <summary>Zero presence, then a staged and ready model, then zero presence again.</summary>
    [Fact]
    public async Task StageAsync_and_RemoveAsync_go_from_zero_presence_to_ready_and_back()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();

        //Act
        ModelStagingStatus staged = await stager.StageAsync(null, TestContext.Current.CancellationToken);
        ModelStagingStatus removed = await stager.RemoveAsync(TestContext.Current.CancellationToken);

        //Assert
        staged.State.Should().Be(ModelStagingState.Ready);
        staged.IsReady.Should().BeTrue();
        removed.State.Should().Be(ModelStagingState.NotPresent);
        removed.BytesOnDisk.Should().Be(0L);
        Directory.Exists(harness.RootDirectory).Should().BeFalse();
    }

    /// <summary>A staged model's files hold exactly the bytes the registry served.</summary>
    [Fact]
    public async Task StageAsync_writes_the_files_the_registry_served()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();

        //Act
        ModelStagingStatus status = await stager.StageAsync(null, TestContext.Current.CancellationToken);

        //Assert
        status.ModelPath.Should().Be(harness.WeightsBlobPath);
        File.ReadAllBytes(status.ModelPath).Should().Equal(harness.WeightsBytes);
        status.ProjectorPaths.Should().HaveCount(1);
        status.ProjectorPaths[0].Should().Be(harness.ProjectorBlobPath);
        File.ReadAllBytes(status.ProjectorPaths[0]).Should().Equal(harness.ProjectorBytes);
        status.WeightsDigest.Should().Be(harness.WeightsDigest);
        status.ExpectedTotalBytes.Should().Be(harness.TotalBytes);
        status.BytesOnDisk.Should().BeGreaterThanOrEqualTo(harness.TotalBytes);
    }

    /// <summary>The one fraction runs forwards only and finishes at one hundred.</summary>
    [Fact]
    public async Task StageAsync_reports_progress_that_never_decreases_and_ends_at_one_hundred()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        var progress = new RecordingProgress();

        //Act
        await stager.StageAsync(progress, TestContext.Current.CancellationToken);

        //Assert
        IReadOnlyList<ModelStagingProgress> reports = progress.Reports;
        reports.Should().NotBeEmpty();
        double previous = -1d;
        foreach (ModelStagingProgress report in reports)
        {
            report.OverallPercent.Should().BeGreaterThanOrEqualTo(previous);
            report.OverallPercent.Should().BeLessThanOrEqualTo(100d);
            previous = report.OverallPercent;
        }
        reports[reports.Count - 1].OverallPercent.Should().Be(100d);
        reports[reports.Count - 1].OverallCompletedBytes
            .Should().Be(reports[reports.Count - 1].OverallTotalBytes);
    }

    /// <summary>A model that is already staged costs no request at all.</summary>
    [Fact]
    public async Task StageAsync_makes_no_request_when_the_model_is_already_ready()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        await stager.StageAsync(null, TestContext.Current.CancellationToken);
        harness.Registry.ClearRequests();
        var progress = new RecordingProgress();

        //Act
        ModelStagingStatus status = await stager.StageAsync(progress, TestContext.Current.CancellationToken);

        //Assert
        status.IsReady.Should().BeTrue();
        harness.Registry.Requests.Should().BeEmpty();
        progress.Reports.Should().HaveCount(1);
        progress.Reports[0].OverallPercent.Should().Be(100d);
    }

    /// <summary>A weights file that was deleted is damage, and staging again puts it back.</summary>
    [Fact]
    public async Task StageAsync_repairs_a_model_whose_weights_file_was_deleted()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        await stager.StageAsync(null, TestContext.Current.CancellationToken);
        File.Delete(harness.WeightsBlobPath);

        //Act
        ModelStagingStatus damaged = await stager.CheckAsync(TestContext.Current.CancellationToken);
        ModelStagingStatus repaired = await stager.StageAsync(null, TestContext.Current.CancellationToken);

        //Assert
        damaged.State.Should().Be(ModelStagingState.Damaged);
        damaged.IsReady.Should().BeFalse();
        damaged.ModelPath.Should().BeNull();
        damaged.Detail.Should().Contain("missing");
        repaired.State.Should().Be(ModelStagingState.Ready);
        File.ReadAllBytes(repaired.ModelPath).Should().Equal(harness.WeightsBytes);
    }

    /// <summary>A weights file that was shortened is damage, and staging again puts it back.</summary>
    [Fact]
    public async Task StageAsync_repairs_a_model_whose_weights_file_was_shortened()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        await stager.StageAsync(null, TestContext.Current.CancellationToken);
        using (var file = new FileStream(harness.WeightsBlobPath, FileMode.Open, FileAccess.Write))
        {
            file.SetLength(harness.WeightsBytes.Length - 512L);
        }

        //Act
        ModelStagingStatus damaged = await stager.CheckAsync(TestContext.Current.CancellationToken);
        ModelStagingStatus repaired = await stager.StageAsync(null, TestContext.Current.CancellationToken);

        //Assert
        damaged.State.Should().Be(ModelStagingState.Damaged);
        damaged.Detail.Should().Contain("bytes on disk");
        repaired.State.Should().Be(ModelStagingState.Ready);
        new FileInfo(repaired.ModelPath).Length.Should().Be(harness.WeightsBytes.Length);
        File.ReadAllBytes(repaired.ModelPath).Should().Equal(harness.WeightsBytes);
    }

    /// <summary>Files with no manifest are an interrupted download, and removal still reaches zero.</summary>
    [Fact]
    public async Task CheckAsync_reports_an_interrupted_download_and_RemoveAsync_sweeps_it_away()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        Directory.CreateDirectory(Path.Combine(harness.StoreDirectory, "blobs"));
        File.WriteAllBytes(
            harness.WeightsBlobPath + ".codebrix-partial",
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        //Act
        ModelStagingStatus interrupted = await stager.CheckAsync(TestContext.Current.CancellationToken);
        ModelStagingStatus removed = await stager.RemoveAsync(TestContext.Current.CancellationToken);

        //Assert
        interrupted.State.Should().Be(ModelStagingState.Incomplete);
        interrupted.BytesOnDisk.Should().Be(8L);
        removed.State.Should().Be(ModelStagingState.NotPresent);
        Directory.Exists(harness.RootDirectory).Should().BeFalse();
    }

    /// <summary>A cancelled download leaves what it had, and the next one carries on from there.</summary>
    [Fact]
    public async Task StageAsync_leaves_an_interrupted_download_that_a_later_call_completes()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        using var cancellation = new CancellationTokenSource();
        harness.Registry.BeforeFileServed = (digest, token) =>
        {
            if (string.Equals(digest, harness.ProjectorDigest, StringComparison.OrdinalIgnoreCase))
            {
                cancellation.Cancel();
            }
            return Task.CompletedTask;
        };

        //Act
        Func<Task> act = () => stager.StageAsync(null, cancellation.Token);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Registry.BeforeFileServed = null;
        File.Exists(harness.ManifestPath).Should().BeFalse();
        File.Exists(harness.WeightsBlobPath).Should().BeTrue();
        ModelStagingStatus afterCancelling = await stager.CheckAsync(TestContext.Current.CancellationToken);
        afterCancelling.State.Should().Be(ModelStagingState.Incomplete);
        ModelStagingStatus completed = await stager.StageAsync(null, TestContext.Current.CancellationToken);
        completed.State.Should().Be(ModelStagingState.Ready);
    }

    /// <summary>A registry with no such model fails plainly and leaves nothing on the disk.</summary>
    [Fact]
    public async Task StageAsync_reports_a_missing_model_and_leaves_zero_presence()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        harness.Registry.ManifestStatusCodeOverride = HttpStatusCode.NotFound;

        //Act
        Func<Task> act = () => stager.StageAsync(null, TestContext.Current.CancellationToken);

        //Assert
        ModelAccessException failure = (await act.Should().ThrowAsync<ModelAccessException>()).Which;
        failure.Failure.Should().Be(ModelAccessFailure.ModelNotFound);
        Directory.Exists(harness.RootDirectory).Should().BeFalse();
    }

    /// <summary>The expected digest is reported as met when the staged weights carry it.</summary>
    [Fact]
    public async Task CheckAsync_reports_that_the_staged_weights_carry_the_expected_digest()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();

        //Act
        ModelStagingStatus status = await stager.StageAsync(null, TestContext.Current.CancellationToken);

        //Assert
        status.WeightsDigest.Should().Be(harness.WeightsDigest);
        status.MatchesPinnedWeights.Should().Be(true);
    }

    /// <summary>The expected digest is reported as unmet when the staged weights carry another.</summary>
    [Fact]
    public async Task CheckAsync_reports_that_the_staged_weights_carry_another_digest()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        await harness.CreateStager().StageAsync(null, TestContext.Current.CancellationToken);
        ModelStager expectingSomethingElse = harness.CreateStager(
            harness.CreateDescriptor(weightsSha256: OtherDigest));

        //Act
        ModelStagingStatus status = await expectingSomethingElse.CheckAsync(
            TestContext.Current.CancellationToken);

        //Assert
        status.State.Should().Be(ModelStagingState.Ready);
        status.MatchesPinnedWeights.Should().Be(false);
    }

    /// <summary>Nothing is reported about a digest when the descriptor expects none.</summary>
    [Fact]
    public async Task CheckAsync_reports_nothing_about_a_digest_when_none_is_expected()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        await harness.CreateStager().StageAsync(null, TestContext.Current.CancellationToken);
        ModelStager expectingNothing = harness.CreateStager(harness.CreateUnpinnedDescriptor());

        //Act
        ModelStagingStatus status = await expectingNothing.CheckAsync(TestContext.Current.CancellationToken);

        //Assert
        status.State.Should().Be(ModelStagingState.Ready);
        status.WeightsDigest.Should().Be(harness.WeightsDigest);
        status.MatchesPinnedWeights.Should().BeNull();
    }

    /// <summary>Removing what is not there is a quiet success that creates nothing.</summary>
    [Fact]
    public async Task RemoveAsync_creates_nothing_when_there_is_nothing_to_remove()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();

        //Act
        ModelStagingStatus status = await stager.RemoveAsync(TestContext.Current.CancellationToken);

        //Assert
        status.State.Should().Be(ModelStagingState.NotPresent);
        Directory.Exists(harness.RootDirectory).Should().BeFalse();
        harness.Registry.Requests.Should().BeEmpty();
    }

    /// <summary>A file that is nothing to do with the model keeps the folder it sits in.</summary>
    [Fact]
    public async Task RemoveAsync_leaves_a_folder_that_holds_a_file_of_someone_elses()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        await stager.StageAsync(null, TestContext.Current.CancellationToken);
        string foreignFile = Path.Combine(harness.RootDirectory, "notes-of-my-own.txt");
        File.WriteAllText(foreignFile, "not part of the model");

        //Act
        ModelStagingStatus status = await stager.RemoveAsync(TestContext.Current.CancellationToken);

        //Assert
        status.State.Should().Be(ModelStagingState.NotPresent);
        Directory.Exists(harness.StoreDirectory).Should().BeFalse();
        Directory.Exists(harness.RootDirectory).Should().BeTrue();
        File.Exists(foreignFile).Should().BeTrue();
        status.Detail.Should().Contain("still there");
    }

    /// <summary>A manifest nothing can read still does not make a model impossible to get rid of.</summary>
    [Fact]
    public async Task RemoveAsync_copes_with_a_manifest_that_cannot_be_read()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        await stager.StageAsync(null, TestContext.Current.CancellationToken);
        File.WriteAllText(harness.ManifestPath, "this was a manifest once");

        //Act
        ModelStagingStatus status = await stager.RemoveAsync(TestContext.Current.CancellationToken);

        //Assert
        status.State.Should().Be(ModelStagingState.NotPresent);
        Directory.Exists(harness.RootDirectory).Should().BeFalse();
    }

    /// <summary>A manifest nothing can read is damage rather than an exception out of a check.</summary>
    [Fact]
    public async Task CheckAsync_reports_damage_rather_than_throwing_when_the_manifest_is_unreadable()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        await stager.StageAsync(null, TestContext.Current.CancellationToken);
        File.WriteAllText(harness.ManifestPath, "this was a manifest once");

        //Act
        ModelStagingStatus status = await stager.CheckAsync(TestContext.Current.CancellationToken);

        //Assert
        status.State.Should().Be(ModelStagingState.Damaged);
        status.Detail.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>The store folder is the models folder inside the folder the application owns.</summary>
    [Fact]
    public void StoreDirectory_is_the_models_folder_inside_the_root()
    {
        //Arrange
        using var harness = new TinyModelHarness();

        //Act
        ModelStager stager = harness.CreateStager();

        //Assert
        stager.RootDirectory.Should().Be(harness.RootDirectory);
        stager.StoreDirectory.Should().Be(
            Path.Combine(harness.RootDirectory, ModelStagerOptions.StoreFolderName));
        stager.Model.Name.Should().Be(TinyModelHarness.ModelName);
    }

    /// <summary>Nothing works after the stager has been disposed.</summary>
    [Fact]
    public async Task CheckAsync_throws_after_the_stager_has_been_disposed()
    {
        //Arrange
        using var harness = new TinyModelHarness();
        ModelStager stager = harness.CreateStager();
        stager.Dispose();

        //Act
        Func<Task> act = () => stager.CheckAsync(TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
