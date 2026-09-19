using System;
using System.Threading.Tasks;
using ModelQueryTool.Core.Tests.Infrastructure;
using ModelQueryTool.ModelAccess.Models;
using ModelQueryTool.ModelRunning;
using ModelQueryTool.Services;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.Core.Tests;

/// <summary>
/// Tests for what <see cref="ChatShell"/> does when the terminal first appears: the banner, the
/// look at what is on disk, and the load that follows when there is something to load.
/// </summary>
public class ChatShellStartUpTests
{
    private const string ModelPath = "/tmp/fake-model-query-tool/models/blobs/sha256-weights";

    /// <summary>The banner names the application and the model, and points at the help.</summary>
    [Fact]
    public async Task Start_writes_a_banner_and_a_prompt()
    {
        //Arrange
        using var harness = new ChatShellHarness();

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.PlainText.Should().Contain("ModelQueryTool - a chat with Test Model");
        harness.PlainText.Should().Contain("Type /help for the commands.");
        harness.Text.Should().Contain("chat");

        //Assert - and the command the banner names stands out from the words around it
        harness.Text.Should().Contain("Type \x1b[0m\x1b[1;33m/help\x1b[0m for the commands.");
    }

    /// <summary>With nothing on disk, start-up says so, says how to fix it, and downloads nothing.</summary>
    [Fact]
    public async Task Start_says_what_to_do_when_nothing_is_on_disk()
    {
        //Arrange
        using var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(ModelStagingState.NotPresent);

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("Test Model has not been downloaded yet.");
        harness.Text.Should().Contain("/download -y");
        harness.Stager.StageCalls.Should().Be(0);
        harness.Host.StartCalls.Should().Be(0);
    }

    /// <summary>An interrupted download is reported as one, with the way to carry it on.</summary>
    [Fact]
    public async Task Start_says_how_to_carry_on_an_interrupted_download()
    {
        //Arrange
        using var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(
            ModelStagingState.Incomplete, bytesOnDisk: 1_073_741_824L, detail: "A partly fetched file is in the store.");

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("A download of Test Model was interrupted");
        harness.Text.Should().Contain("carry on from where it stopped");
        harness.Stager.StageCalls.Should().Be(0);
    }

    /// <summary>
    /// A damaged model offers the repair first and the delete-and-fetch-again only after it,
    /// because a file of the right size that is still wrong cannot be repaired by fetching.
    /// </summary>
    [Fact]
    public async Task Start_offers_the_repair_before_the_removal_for_a_damaged_model()
    {
        //Arrange
        using var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(
            ModelStagingState.Damaged, bytesOnDisk: 12L, detail: "The weights file is not the size the manifest states.");

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("is on disk but is not usable");
        //(the lines are laid out against the terminal's width, so only what fits on one row of
        //  eighty cells is asserted as one piece; the commands in them are highlighted, so the
        //  sentences are read with the colour changes taken out)
        harness.PlainText.Should().Contain("/download -y to repair it");
        harness.PlainText.Should().Contain("/remove -y and then");
        harness.Stager.RemoveCalls.Should().Be(0);
    }

    /// <summary>A model that is there is loaded in the background, and the status bar says so.</summary>
    [Fact]
    public async Task Start_loads_a_ready_model_in_the_background()
    {
        //Arrange
        using var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(
            ModelStagingState.Ready, ModelPath, 4_000_000_000L, "Every file the manifest names is present.");
        harness.Host.LoadReports.Add(0.5f);

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Host.StartCalls.Should().Be(1);
        harness.Host.ModelPath.Should().Be(ModelPath);
        harness.Text.Should().Contain("Test Model is on disk");
        harness.Text.Should().Contain("The model is loaded and ready");
        harness.ShellHost.Reports.Should().Contain(report =>
            report.IsVisible && report.Caption == "Loading Test Model..." && !report.IsIndeterminate);
        harness.ShellHost.Latest.IsVisible.Should().Be(false);
    }

    /// <summary>
    /// THE WHOLE OF START-UP LEAVES ONE PROMPT. Both of the lines start-up writes out of band -
    /// "it is on disk" and "it is loaded" - take the place of the prompt row above them, so the
    /// user meets a banner, the two remarks and a single prompt rather than three stacked ones.
    /// </summary>
    [Fact]
    public async Task the_start_up_of_a_ready_model_leaves_exactly_one_prompt()
    {
        //Arrange
        using var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(
            ModelStagingState.Ready, ModelPath, 4_000_000_000L, "Every file the manifest names is present.");

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert - the rows a reader would see, with the escape sequences applied and taken out
        var screen = harness.ScreenText;

        screen.Should().Contain("ModelQueryTool - a chat with Test Model");
        screen.Should().Contain("Test Model is on disk");
        screen.Should().Contain("The model is loaded and ready");
        CountOf(screen, "chat> ").Should().Be(1);
        screen.Should().EndWith("chat> ");
    }

    /// <summary>A folder that cannot be read is one red line, and nothing else happens.</summary>
    [Fact]
    public async Task Start_reports_a_store_that_cannot_be_read()
    {
        //Arrange
        using var harness = new ChatShellHarness();
        harness.Stager.CheckFailure = new InvalidOperationException("The store folder is not readable.");

        //Act
        await harness.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("The model folder could not be read.");
        harness.Text.Should().Contain("The store folder is not readable.");
        harness.Host.StartCalls.Should().Be(0);
    }

    /// <summary>A line typed while the model is still loading is answered in one line, not queued.</summary>
    [Fact]
    public async Task a_chat_line_while_the_model_loads_is_answered_in_one_line()
    {
        //Arrange
        using var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(ModelStagingState.Ready, ModelPath);
        harness.Host.StartWaitsToBeCancelled = true;
        harness.StartWithoutWaiting();
        await harness.WaitUntilAsync(() => harness.Shell.Busy == ChatBusyKind.Loading,
            TestContext.Current.CancellationToken);
        harness.ClearText();

        //Act
        await harness.SubmitWhileBusyAsync("what is a proton?", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("The model is still loading; ask again in a moment.");
        harness.Host.Sent.Should().BeEmpty();

        //Cleanup - let the load fall over so the background task can finish
        harness.Shell.Cancel();
        await harness.WaitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A line typed with no model at all says where to get one.</summary>
    [Fact]
    public async Task a_chat_line_with_no_model_says_where_to_get_one()
    {
        //Arrange
        using var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(ModelStagingState.NotPresent);
        await harness.StartAsync(TestContext.Current.CancellationToken);
        harness.ClearText();

        //Act
        await harness.SubmitAsync("hello?", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("No model is loaded.");
        harness.Host.Sent.Should().BeEmpty();
        harness.Host.State.Should().Be(ModelHostState.Stopped);
    }

    /// <summary>A stopped load says so and leaves nothing loaded.</summary>
    [Fact]
    public async Task a_cancelled_background_load_says_nothing_is_loaded()
    {
        //Arrange
        using var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(ModelStagingState.Ready, ModelPath);
        harness.Host.StartWaitsToBeCancelled = true;
        harness.StartWithoutWaiting();
        await harness.WaitUntilAsync(() => harness.Shell.Busy == ChatBusyKind.Loading,
            TestContext.Current.CancellationToken);

        //Act
        harness.Shell.Cancel();
        await harness.WaitAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("The load was stopped; nothing is loaded.");
        harness.ShellHost.Latest.IsVisible.Should().Be(false);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
