using ModelQueryTool.Core.Tests.Infrastructure;
using ModelQueryTool.ModelAccess.Models;
using ModelQueryTool.ModelRunning;
using ModelQueryTool.Services;
using SilverAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace ModelQueryTool.Core.Tests;

/// <summary>
/// Tests for the commands that touch the model: obtaining it, taking it away again, and loading
/// it at another context size. Every one of them changes nothing at all without its
/// confirmation - it may LOOK, because a check of the store is read-only, but it may not act.
/// </summary>
public class ChatShellModelCommandTests
{
    private const string ModelPath = "/tmp/fake-model-query-tool/models/blobs/sha256-weights";

    /// <summary>
    /// With nothing on disk, a download describes the whole fetch and takes no step towards it.
    /// It DOES look at the store first - that is a read-only call, and it is how the other cases
    /// below can say something true instead of the same paragraph every time.
    /// </summary>
    [Fact]
    public async Task download_without_the_confirmation_fetches_nothing()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.ResetCounts();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/download", TestContext.Current.CancellationToken);

        //Assert - nothing that CHANGES anything was called; the read-only check was
        AssertNothingWasChanged(harness);
        harness.Stager.CheckCalls.Should().Be(1);

        //Assert
        harness.Text.Should().Contain("This would fetch Test Model - about 3.7 GiB");
        harness.Text.Should().Contain(harness.Stager.StoreDirectory);
        harness.Text.Should().Contain("Nothing has been fetched.");
        harness.Text.Should().Contain("/download -y");

        //Assert - the command to type stands out, and the folder it names does not
        harness.Text.Should().Contain("\x1b[0m\x1b[1;33m/download -y\x1b[0m");
        harness.Text.Should().NotContain("\x1b[1;33m" + harness.Stager.StoreDirectory);
    }

    /// <summary>A model that is there and loaded is reported as exactly that: nothing to do.</summary>
    [Fact]
    public async Task download_without_the_confirmation_says_when_the_model_is_there_and_loaded()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ResetCounts();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/download", TestContext.Current.CancellationToken);

        //Assert
        AssertNothingWasChanged(harness);
        harness.PlainText.Should().Contain(
            "Test Model is already downloaded and loaded - there is nothing to do.");
        harness.PlainText.Should().Contain("3.7 GiB is in " + harness.Stager.StoreDirectory + ".");
        harness.PlainText.Should().NotContain("This would fetch");
    }

    /// <summary>A model that is there but not loaded is told that only a load is left to do.</summary>
    [Fact]
    public async Task download_without_the_confirmation_says_only_a_load_is_left()
    {
        //Arrange - the model is on disk, and the load at start-up was stopped
        using var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(
            ModelStagingState.Ready, ModelPath, 4_000_000_000L, "Every file the manifest names is present.");
        harness.Host.StartFailure = new ModelRunningException("The engine would not start.");
        await harness.StartAsync(TestContext.Current.CancellationToken);
        harness.ResetCounts();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/download", TestContext.Current.CancellationToken);

        //Assert
        AssertNothingWasChanged(harness);
        harness.PlainText.Should().Contain("Test Model is already downloaded; nothing would be fetched.");
        harness.PlainText.Should().Contain("/download -y only loads it.");
    }

    /// <summary>An interrupted download is told how far it got and that it would carry on.</summary>
    [Fact]
    public async Task download_without_the_confirmation_says_how_far_an_interrupted_download_got()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(
            ModelStagingState.Incomplete, bytesOnDisk: 1_073_741_824L, detail: "a partly fetched file is there.");
        harness.ResetCounts();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/download", TestContext.Current.CancellationToken);

        //Assert
        AssertNothingWasChanged(harness);
        harness.PlainText.Should().Contain(
            "A download of Test Model was interrupted: a partly fetched file is there.");
        harness.PlainText.Should().Contain(
            "1.0 GiB of 3.7 GiB is already in " + harness.Stager.StoreDirectory + ".");
        harness.PlainText.Should().Contain("Nothing has been fetched. /download -y carries on from there.");
    }

    /// <summary>A damaged model is told what is wrong with it, and offered the two remedies.</summary>
    [Fact]
    public async Task download_without_the_confirmation_offers_the_two_remedies_for_a_damaged_model()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(
            ModelStagingState.Damaged, bytesOnDisk: 12L, detail: "the weights file is the wrong size.");
        harness.ResetCounts();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/download", TestContext.Current.CancellationToken);

        //Assert
        AssertNothingWasChanged(harness);
        harness.PlainText.Should().Contain(
            "Test Model is on disk but is not usable: the weights file is the wrong size.");
        harness.PlainText.Should().Contain("Nothing has been fetched. /download -y repairs it.");
        harness.PlainText.Should().Contain("If that does not help, /remove -y and then /download -y.");
    }

    /// <summary>A store that cannot be read falls back to the old answer, and says why.</summary>
    [Fact]
    public async Task download_without_the_confirmation_says_when_the_folder_cannot_be_read()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.Stager.CheckFailure = new InvalidOperationException("The store folder is not readable.");
        harness.ResetCounts();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/download", TestContext.Current.CancellationToken);

        //Assert
        AssertNothingWasChanged(harness);
        harness.Text.Should().Contain("This would fetch Test Model - about 3.7 GiB");
        harness.PlainText.Should().Contain(
            "The model folder could not be read: The store folder is not readable.");
    }

    /// <summary>With the confirmation the model is fetched and then loaded.</summary>
    [Fact]
    public async Task download_with_the_confirmation_fetches_and_then_loads()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.Stager.StageStatus = harness.Stager.CreateStatus(
            ModelStagingState.Ready, ModelPath, 4_000_000_000L, "Every file the manifest names is present.");
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/download -y", TestContext.Current.CancellationToken);

        //Assert
        harness.Stager.StageCalls.Should().Be(1);
        harness.Host.StartCalls.Should().Be(1);
        harness.Host.ModelPath.Should().Be(ModelPath);
        harness.Text.Should().Contain("Test Model is downloaded");
        harness.Text.Should().Contain("The model is loaded and ready.");
    }

    /// <summary>The download's progress reaches the status bar with a caption a person can read.</summary>
    [Fact]
    public async Task download_progress_reaches_the_status_bar()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.Stager.StageStatus = harness.Stager.CreateStatus(ModelStagingState.Ready, ModelPath, 4_000_000_000L);
        harness.Stager.StageReports.Add(new ModelStagingProgress(
            "pulling", "sha256:one", 4_000_000_000L, 0L, 4_000_000_000L, 0L, 0d));
        harness.Stager.StageReports.Add(new ModelStagingProgress(
            "pulling", "sha256:one", 4_000_000_000L, 2_000_000_000L, 4_000_000_000L, 2_000_000_000L, 50d));
        harness.ShellHost.ClearReports();

        //Act
        await harness.SubmitAsync("/download -y", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Reports.Should().Contain(report =>
            report.IsVisible && report.Caption == "Downloading Test Model...");
        harness.ShellHost.Reports.Should().Contain(report => report.IsVisible && report.Percent == 50d);
        harness.ShellHost.Reports.Should().Contain(report =>
            report.IsVisible && report.Caption.Contains("GiB /"));
        harness.ShellHost.Latest.IsVisible.Should().Be(false);
    }

    /// <summary>A download that is stopped keeps what arrived and says how to carry on.</summary>
    [Fact]
    public async Task a_cancelled_download_keeps_what_arrived()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.Stager.StageWaitsToBeCancelled = true;
        harness.ClearText();

        //Act
        harness.Shell.SendInput("/download -y");
        harness.Shell.SendInput("\r");
        await harness.WaitUntilAsync(() => harness.Shell.Busy == ChatBusyKind.Downloading,
            TestContext.Current.CancellationToken);
        harness.Shell.Cancel();
        await harness.WaitAsync(TestContext.Current.CancellationToken);

        //Assert
        //(the line is laid out against the terminal's width, so it is asserted a row at a time;
        //  the command in it is highlighted, so the row is read with the colour changes out)
        harness.PlainText.Should().Contain(
            "The download was stopped. What has arrived is kept - /download -y carries");
        harness.Text.Should().Contain("from there.");
        harness.Host.StartCalls.Should().Be(0);
        harness.ShellHost.Latest.IsVisible.Should().Be(false);
    }

    /// <summary>Asked for a model that is already there and already loaded, a download says so and stops.</summary>
    [Fact]
    public async Task download_with_the_confirmation_says_so_when_the_model_is_already_there()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/download -y", TestContext.Current.CancellationToken);

        //Assert
        harness.Stager.StageCalls.Should().Be(0);
        harness.Host.StartCalls.Should().Be(1); //the one from start-up, and no more
        harness.Text.Should().Contain("Test Model is already downloaded");
    }

    /// <summary>
    /// Asked without the confirmation, a removal describes itself, says how much is there, and
    /// deletes nothing. The check it makes to find that out is read-only.
    /// </summary>
    [Fact]
    public async Task remove_without_the_confirmation_deletes_nothing()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ResetCounts();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/remove", TestContext.Current.CancellationToken);

        //Assert - nothing that CHANGES anything was called; the read-only check was
        AssertNothingWasChanged(harness);
        harness.Stager.CheckCalls.Should().Be(1);

        //Assert
        harness.Text.Should().Contain("This would delete Test Model and every folder made for it");
        harness.Text.Should().Contain(harness.Stager.RootDirectory);
        harness.PlainText.Should().Contain("3.7 GiB of it is on disk now.");
        harness.Text.Should().Contain("Nothing has been deleted.");
    }

    /// <summary>With nothing on disk, a removal says there is nothing to remove and where it looked.</summary>
    [Fact]
    public async Task remove_without_the_confirmation_says_there_is_nothing_to_remove()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.ResetCounts();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/remove", TestContext.Current.CancellationToken);

        //Assert
        AssertNothingWasChanged(harness);
        harness.PlainText.Should().Contain("There is nothing to remove - no part of Test Model is on disk.");
        harness.PlainText.Should().Contain(
            "The folder it would have been in: " + harness.Stager.StoreDirectory);
        harness.PlainText.Should().NotContain("This would delete");
    }

    /// <summary>A store that cannot be read still describes the removal, and says why the size is missing.</summary>
    [Fact]
    public async Task remove_without_the_confirmation_says_when_the_folder_cannot_be_read()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Stager.CheckFailure = new InvalidOperationException("The store folder is not readable.");
        harness.ResetCounts();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/remove", TestContext.Current.CancellationToken);

        //Assert
        AssertNothingWasChanged(harness);
        harness.Text.Should().Contain("This would delete Test Model and every folder made for it");
        harness.PlainText.Should().Contain(
            "The model folder could not be read: The store folder is not readable.");
    }

    /// <summary>With the confirmation the loaded model is stopped first, and then everything goes.</summary>
    [Fact]
    public async Task remove_with_the_confirmation_stops_the_model_first()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/remove -y", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.StopCalls.Should().Be(1);
        harness.Stager.RemoveCalls.Should().Be(1);
        harness.Text.Should().Contain("Stopping the model first");
        harness.Text.Should().Contain("Test Model is gone, and so is every folder made for it.");
    }

    /// <summary>What a removal could not take away is reported as it was found.</summary>
    [Fact]
    public async Task remove_reports_what_it_could_not_take_away()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.Stager.RemoveStatus = harness.Stager.CreateStatus(
            ModelStagingState.Incomplete, bytesOnDisk: 4096L, detail: "A file of someone else's is in the folder.");
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/remove -y", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("What is left: Incomplete");
        harness.Text.Should().Contain("A file of someone else's is in the folder.");
    }

    /// <summary>Asked without the confirmation, a context change describes itself and reloads nothing.</summary>
    [Fact]
    public async Task set_context_size_without_the_confirmation_reloads_nothing()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.TrainedContextSize = 262144;
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/set-context-size 16384", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.ReloadCalls.Should().Be(0);
        harness.Text.Should().Contain("This would unload the model and load it again with a context of 16384 tokens");
        harness.Text.Should().Contain("Nothing has changed.");
    }

    /// <summary>With the confirmation the model is loaded again at the new size.</summary>
    [Fact]
    public async Task set_context_size_with_the_confirmation_reloads()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.TrainedContextSize = 262144;
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/set-context-size -y 16384", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.ReloadCalls.Should().Be(1);
        harness.Host.ReloadSizes.Should().ContainSingle();
        harness.Host.ReloadSizes[0].Should().Be(16384u);
        harness.Text.Should().Contain("The model is loaded again with a context of 16384 tokens");
        harness.ShellHost.Reports.Should().Contain(report =>
            report.IsVisible && report.Caption == "Reloading Test Model...");
    }

    /// <summary>Something that is not a number is refused, and nothing is reloaded.</summary>
    [Fact]
    public async Task set_context_size_refuses_what_is_not_a_number()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.TrainedContextSize = 262144;
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/set-context-size -y lots", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.ReloadCalls.Should().Be(0);
        harness.Text.Should().Contain("'lots' is not a whole number of tokens");
    }

    /// <summary>A number the model was never trained for is refused, and nothing is reloaded.</summary>
    [Fact]
    public async Task set_context_size_refuses_a_number_outside_the_model()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.TrainedContextSize = 32768;
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/set-context-size -y 65536", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.ReloadCalls.Should().Be(0);
        harness.Text.Should().Contain("A context size is between 512 and 32768 tokens");
    }

    /// <summary>A reload that had to fall back is reported in the words the library used.</summary>
    [Fact]
    public async Task set_context_size_reports_a_fallback_in_the_words_it_was_given()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.TrainedContextSize = 262144;
        harness.Host.ReloadFailure = new ModelRunningException(
            "The model would not load with a context size of 131072 (not enough memory). It is loaded again at "
            + "8192, with a new conversation.");
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/set-context-size -y 131072", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("\x1b[31mThe model would not load with a context size of 131072");
        harness.Text.Should().Contain("is loaded again at 8192, with a new conversation.");
    }

    /// <summary>Asked with no number at all, it says what the size is now.</summary>
    [Fact]
    public async Task set_context_size_without_a_number_says_what_it_is()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/set-context-size", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.ReloadCalls.Should().Be(0);
        harness.Text.Should().Contain("The context size is 8192 tokens.");
    }

    /// <summary>
    /// A line typed while a download is running waits for it: the session runs submitted lines
    /// one after another, so the question is asked of the model the download just obtained
    /// rather than being refused.
    /// </summary>
    [Fact]
    public async Task a_line_typed_during_a_download_is_asked_once_the_download_has_finished()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.Stager.StageStatus = harness.Stager.CreateStatus(
            ModelStagingState.Ready, ModelPath, 4_000_000_000L);

        //Act
        harness.Shell.SendInput("/download -y");
        harness.Shell.SendInput("\r");
        harness.Shell.SendInput("what is a proton?");
        harness.Shell.SendInput("\r");
        await harness.WaitAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Stager.StageCalls.Should().Be(1);
        harness.Host.StartCalls.Should().Be(1);
        harness.Host.Sent.Should().ContainSingle();
        harness.Host.Sent[0].Should().Be("what is a proton?");
    }

    /// <summary>Ctrl+C stops a download as surely as the Cancel button does.</summary>
    [Fact]
    public async Task Ctrl_C_stops_a_download()
    {
        //Arrange
        using var harness = await EmptyStoreAsync();
        harness.Stager.StageWaitsToBeCancelled = true;
        harness.ClearText();

        //Act
        harness.Shell.SendInput("/download -y");
        harness.Shell.SendInput("\r");
        await harness.WaitUntilAsync(() => harness.Shell.Busy == ChatBusyKind.Downloading,
            TestContext.Current.CancellationToken);
        harness.Shell.SendInput("\x03");
        await harness.WaitAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("The download was stopped.");
        harness.ShellHost.Latest.IsVisible.Should().Be(false);
    }

    /// <summary>
    /// Asserts the rule the confirmation exists for: an unconfirmed command makes no call that
    /// changes anything - not on the store, not on the model. A read-only check is not a change.
    /// </summary>
    private static void AssertNothingWasChanged(ChatShellHarness harness)
    {
        harness.Stager.StageCalls.Should().Be(0);
        harness.Stager.RemoveCalls.Should().Be(0);
        harness.Host.StartCalls.Should().Be(0);
        harness.Host.StopCalls.Should().Be(0);
        harness.Host.ReloadCalls.Should().Be(0);
        harness.Host.SystemPromptCalls.Should().Be(0);
        harness.Host.ClearCalls.Should().Be(0);
    }

    private static async Task<ChatShellHarness> EmptyStoreAsync()
    {
        var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(ModelStagingState.NotPresent);
        await harness.StartAsync(TestContext.Current.CancellationToken);

        return harness;
    }

    private static async Task<ChatShellHarness> LoadedAsync()
    {
        var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(
            ModelStagingState.Ready, ModelPath, 4_000_000_000L, "Every file the manifest names is present.");
        await harness.StartAsync(TestContext.Current.CancellationToken);

        return harness;
    }
}
