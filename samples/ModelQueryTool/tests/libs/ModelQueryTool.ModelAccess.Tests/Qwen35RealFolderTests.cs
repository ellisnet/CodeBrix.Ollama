using System;
using System.IO;
using System.Threading.Tasks;
using ModelQueryTool.ModelAccess.Models;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// The two tests that act on the folder the application really uses and on the publisher's real
/// registry, with the library's own default options and nothing standing in for anything.
/// </summary>
/// <remarks>
/// <para>
/// THE ENVIRONMENT VARIABLE IS THE GUARD. Each test is opened by one variable of its own and by nothing
/// else: there is no second check inside the test, because a test that decided for itself whether to act
/// would be deciding something the person who opened the gate has already decided.
/// </para>
/// <para>
/// One of them downloads about twenty-one gibibytes, which takes something like twenty minutes on a
/// domestic connection, and the other deletes what it downloaded. They run on their own, never beside
/// each other and never beside the application.
/// </para>
/// </remarks>
[Collection(RealFolderCollection.Name)]
public class Qwen35RealFolderTests
{
    /// <summary>The variable that opens the download.</summary>
    public const string StageGate = "MODELQUERYTOOL_TEST_STAGE_QWEN35";

    /// <summary>The variable that opens the removal.</summary>
    public const string RemoveGate = "MODELQUERYTOOL_TEST_REMOVE_QWEN35";

    /// <summary>The size of the weights file the publisher serves.</summary>
    private const long WeightsBytes = 22_016_023_168L;

    /// <summary>The size of the vision projector the publisher serves.</summary>
    private const long ProjectorBytes = 899_283_648L;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class with the output the download's progress is written to.</summary>
    /// <param name="output">Where each line of progress goes.</param>
    public Qwen35RealFolderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Downloads the whole model into the application's own folder and checks every part of what it
    /// staged. It removes nothing.
    /// </summary>
    /// <returns>A task that completes when the model is staged and checked.</returns>
    [EnvGatedFact(StageGate)]
    public async Task StageAsync_downloads_and_stages_the_real_model()
    {
        //Arrange
        using var stager = new ModelStager();
        var progress = new ThrottledProgressWriter(_output, TimeSpan.FromSeconds(10d));
        _output.WriteLine("Staging " + stager.Model.DisplayName + " into " + stager.StoreDirectory);

        //Act
        ModelStagingStatus staged = await stager.StageAsync(progress, TestContext.Current.CancellationToken);

        //Assert
        staged.State.Should().Be(ModelStagingState.Ready);
        staged.IsReady.Should().BeTrue();
        staged.Model.Should().BeSameAs(KnownModels.Qwen35);

        File.Exists(staged.ModelPath).Should().BeTrue();
        new FileInfo(staged.ModelPath).Length.Should().Be(WeightsBytes);

        staged.ProjectorPaths.Should().HaveCount(1);
        File.Exists(staged.ProjectorPaths[0]).Should().BeTrue();
        new FileInfo(staged.ProjectorPaths[0]).Length.Should().Be(ProjectorBytes);

        staged.WeightsDigest.Should().Be("sha256:" + KnownModels.Qwen35WeightsSha256);
        staged.MatchesPinnedWeights.Should().Be(true);

        ModelStagingStatus checkedAgain = await stager.CheckAsync(TestContext.Current.CancellationToken);
        checkedAgain.State.Should().Be(ModelStagingState.Ready);
        checkedAgain.ModelPath.Should().Be(staged.ModelPath);
        checkedAgain.WeightsDigest.Should().Be(staged.WeightsDigest);
        checkedAgain.MatchesPinnedWeights.Should().Be(true);

        // Staging a model that is already staged reports one hundred percent once and fetches nothing.
        var secondRun = new RecordingProgress();
        ModelStagingStatus again = await stager.StageAsync(secondRun, TestContext.Current.CancellationToken);
        again.State.Should().Be(ModelStagingState.Ready);
        secondRun.Reports.Should().HaveCount(1);
        secondRun.Reports[0].OverallPercent.Should().Be(100d);
    }

    /// <summary>
    /// Removes the model from the application's own folder and checks that the folder itself is gone.
    /// </summary>
    /// <returns>A task that completes when nothing of the model is left.</returns>
    [EnvGatedFact(RemoveGate)]
    public async Task RemoveAsync_returns_the_real_folder_to_zero_presence()
    {
        //Arrange
        using var stager = new ModelStager();
        _output.WriteLine("Removing " + stager.Model.DisplayName + " from " + stager.RootDirectory);

        //Act
        ModelStagingStatus removed = await stager.RemoveAsync(TestContext.Current.CancellationToken);

        //Assert
        removed.State.Should().Be(ModelStagingState.NotPresent);
        removed.IsReady.Should().BeFalse();
        Directory.Exists(stager.RootDirectory).Should().BeFalse();
    }
}
