using System;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;
using ModelQueryTool.Core.Tests.Infrastructure;
using ModelQueryTool.ModelAccess.Models;
using ModelQueryTool.ModelRunning;
using ModelQueryTool.Services;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.Core.Tests;

/// <summary>
/// Tests for the turn itself: what the chat writes while the model reasons and answers, what it
/// writes when the turn is interrupted, and what the status bar does throughout.
/// </summary>
public class ChatShellChatTests
{
    private const string ModelPath = "/tmp/fake-model-query-tool/models/blobs/sha256-weights";
    private const string Prompt = "\x1b[1;36mchat\x1b[0m> ";
    private const string DimOn = "\x1b[2m";
    private const string Reset = "\x1b[0m";
    private const string Highlight = "\x1b[1;33m";

    /// <summary>
    /// The whole of a reasoning turn, byte for byte: the reminder above the block, the dim that
    /// opens it, the reasoning laid out as it streams, the reset and the blank row before the
    /// answer, and the prompt that follows.
    /// </summary>
    [Fact]
    public async Task a_turn_with_reasoning_is_written_byte_for_byte()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(Thinking("Let me think. "));
        harness.Host.Updates.Add(Thinking("Done."));
        harness.Host.Updates.Add(Content("Hello"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        harness.ClearText();

        //Act
        await harness.SubmitAsync("hi", TestContext.Current.CancellationToken);

        //Assert
        //(the reminder names a command, so the dim stops for it, the highlight writes it, and the
        //  dim goes back on for the rest of the sentence)
        harness.Text.Should().Be(
            "hi"
            + "\r\n"
            + DimOn + "Thinking is on; " + Reset + Highlight + "/think off" + Reset + DimOn
            + " turns it off." + Reset + "\r\n"
            + DimOn
            + "Let me think. "
            + "Done."
            + Reset
            + "\r\n"
            + "\r\n"
            + "Hello"
            + "\r\n"
            + Prompt);
    }

    /// <summary>With no reasoning in the turn there is no reminder and no dimmed block at all.</summary>
    [Fact]
    public async Task a_turn_without_reasoning_writes_neither_the_reminder_nor_a_dimmed_block()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Think = false;
        harness.Host.Updates.Add(Content("Earth"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        harness.ClearText();

        //Act
        await harness.SubmitAsync("which planet?", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Be("which planet?" + "\r\n" + "Earth" + "\r\n" + Prompt);
    }

    /// <summary>The reminder is written once a turn, however many pieces of reasoning arrive.</summary>
    [Fact]
    public async Task the_reminder_is_written_once_a_turn()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(Thinking("one "));
        harness.Host.Updates.Add(Thinking("two "));
        harness.Host.Updates.Add(Thinking("three"));
        harness.Host.Updates.Add(Content("done"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        harness.ClearText();

        //Act
        await harness.SubmitAsync("hi", TestContext.Current.CancellationToken);

        //Assert
        //(the reminder carries a highlight now, so it is counted with the colour changes out)
        CountOf(harness.PlainText, ChatShell.ThinkingReminder).Should().Be(1);
    }

    /// <summary>Something the host had to do to make room is one dimmed line of its own.</summary>
    [Fact]
    public async Task a_notice_from_the_host_is_one_dimmed_line()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(new ChatTurnUpdate
        {
            Kind = ChatTurnUpdateKind.Notice,
            Text = "1 earlier turn was left out of this request.",
        });
        harness.Host.Updates.Add(Content("fine"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        harness.ClearText();

        //Act
        await harness.SubmitAsync("hi", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain(DimOn + "1 earlier turn was left out of this request." + Reset + "\r\n");
    }

    /// <summary>
    /// THE MODEL'S TEXT IS NEVER TOUCHED. A model that writes "/help" in its answer is quoting
    /// itself, not the application speaking - so the answer is shown exactly as it was written,
    /// with no highlight anywhere in it.
    /// </summary>
    [Fact]
    public async Task a_command_the_model_names_in_its_answer_is_not_highlighted()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Think = false;
        harness.Host.Updates.Add(Content("If you get stuck, type /help for the commands."));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        harness.ClearText();

        //Act
        await harness.SubmitAsync("what should I do?", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Be(
            "what should I do?" + "\r\n"
            + "If you get stuck, type /help for the commands." + "\r\n"
            + Prompt);
        harness.Text.Should().NotContain(Highlight);
    }

    /// <summary>The line reaches the model exactly as it was typed, quotes and all.</summary>
    [Fact]
    public async Task the_line_reaches_the_model_verbatim()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(Content("ok"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));

        //Act
        await harness.SubmitAsync("say \"hello\"  twice", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.Sent.Should().ContainSingle();
        harness.Host.Sent[0].Should().Be("say \"hello\"  twice");
    }

    /// <summary>
    /// The wait every turn opens with is reported the moment the turn starts - the engine reads
    /// the whole conversation back before its first word, which on a long conversation is a long
    /// silence - and the bar goes the moment the first text arrives.
    /// </summary>
    [Fact]
    public async Task the_status_bar_reports_the_wait_before_the_first_text()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(Content("Earth"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        harness.Host.PauseBeforeUpdate = 0;
        harness.ShellHost.ClearReports();

        //Act
        harness.Shell.SendInput("which planet?");
        harness.Shell.SendInput("\r");
        await harness.Host.ReachedPause.WaitAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Latest.IsVisible.Should().Be(true);
        harness.ShellHost.Latest.IsIndeterminate.Should().Be(true);
        harness.ShellHost.Latest.Caption.Should().Be("Reading the conversation...");

        //Act - let the answer through
        harness.Host.Release();
        await harness.WaitAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Latest.IsVisible.Should().Be(false);
        harness.Text.Should().Contain("Earth");
    }

    /// <summary>The wait says how much there is to read, from what the host says the conversation costs.</summary>
    [Fact]
    public async Task the_wait_caption_counts_the_conversation()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.ConversationTokens = 2100;
        harness.Host.Updates.Add(Content("Earth"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        harness.Host.PauseBeforeUpdate = 0;
        harness.ShellHost.ClearReports();

        //Act
        harness.Shell.SendInput("which planet?");
        harness.Shell.SendInput("\r");
        await harness.Host.ReachedPause.WaitAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Latest.Caption.Should().Be("Reading the conversation (about 2,100 tokens)...");

        //Cleanup
        harness.Host.Release();
        await harness.WaitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A turn stopped during that wait leaves no bar behind and says it was interrupted.</summary>
    [Fact]
    public async Task a_turn_cancelled_during_the_wait_hides_the_bar()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(Content("Earth"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        harness.Host.PauseBeforeUpdate = 0;
        harness.ClearText();

        //Act
        harness.Shell.SendInput("which planet?");
        harness.Shell.SendInput("\r");
        await harness.Host.ReachedPause.WaitAsync(TestContext.Current.CancellationToken);
        harness.Shell.Cancel();
        await harness.WaitAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Latest.IsVisible.Should().Be(false);
        harness.Text.Should().Contain("^C");
        harness.Text.Should().NotContain("Earth");
    }

    /// <summary>
    /// Ctrl+C part way through an answer keeps what has been written, closes the dimmed block it
    /// was in, and marks the interruption.
    /// </summary>
    [Fact]
    public async Task Ctrl_C_part_way_through_a_turn_keeps_what_was_written()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(Thinking("weighing it up "));
        harness.Host.Updates.Add(Content("The answer is "));
        harness.Host.Updates.Add(Content("forty-two"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        harness.Host.PauseBeforeUpdate = 2;
        harness.ClearText();

        //Act
        harness.Shell.SendInput("how many?");
        harness.Shell.SendInput("\r");
        await harness.Host.ReachedPause.WaitAsync(TestContext.Current.CancellationToken);
        harness.Shell.SendInput("\x03");
        await harness.WaitAsync(TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("weighing it up");
        harness.Text.Should().Contain("The answer is");
        harness.Text.Should().NotContain("forty-two");
        harness.Text.Should().Contain("^C");
        harness.Host.State.Should().Be(ModelHostState.Ready);
    }

    /// <summary>A failure from the model is one red line, never a stack trace.</summary>
    [Fact]
    public async Task a_failure_from_the_model_is_one_red_line()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(Content("starting"));
        harness.Host.TurnFailure = new ModelRunningException("The model stopped part way through its answer.");
        harness.ClearText();

        //Act
        await harness.SubmitAsync("hi", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("\x1b[31mThe model stopped part way through its answer." + Reset + "\r\n");
        harness.Text.Should().NotContain("   at ");
    }

    /// <summary>A turn that ran out of context says so, in the chat's own dimmed voice.</summary>
    [Fact]
    public async Task an_answer_cut_off_by_a_full_context_says_why()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(Content("as far as I got"));
        harness.Host.Updates.Add(Completed(FinishReason.ContextFull));
        harness.ClearText();

        //Act
        await harness.SubmitAsync("hi", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("The answer stopped because the context filled up.");
        harness.Text.Should().Contain("/set-context-size -y");
    }

    /// <summary>A turn that reached the request's length limit says that instead.</summary>
    [Fact]
    public async Task an_answer_cut_off_by_its_length_says_why()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(Content("as far as I got"));
        harness.Host.Updates.Add(Completed(FinishReason.Length));
        harness.ClearText();

        //Act
        await harness.SubmitAsync("hi", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("The answer stopped because it reached the length the request allowed.");
    }

    /// <summary>
    /// The line that says the model is ready arrives while the user is typing, and the prompt and
    /// the half-typed line are put back underneath it.
    /// </summary>
    [Fact]
    public async Task the_model_ready_line_repaints_a_half_typed_line()
    {
        //Arrange
        using var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(ModelStagingState.Ready, ModelPath);
        harness.Host.StartWaitsForRelease = true;
        harness.StartWithoutWaiting();
        await harness.WaitUntilAsync(() => harness.Shell.Busy == ChatBusyKind.Loading,
            TestContext.Current.CancellationToken);
        harness.ClearText();
        harness.Shell.SendInput("hal");

        //Act
        harness.Host.Release();
        await harness.WaitAsync(TestContext.Current.CancellationToken);

        //Assert
        var index = harness.Text.IndexOf("The model is loaded and ready", StringComparison.Ordinal);
        index.Should().BeGreaterThan(-1);
        harness.Text.Substring(index).Should().Contain("hal");
    }

    private static async Task<ChatShellHarness> LoadedAsync()
    {
        var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(
            ModelStagingState.Ready, ModelPath, 4_000_000_000L, "Every file the manifest names is present.");
        await harness.StartAsync(TestContext.Current.CancellationToken);

        return harness;
    }

    private static ChatTurnUpdate Thinking(string text) =>
        new() { Kind = ChatTurnUpdateKind.Thinking, Text = text };

    private static ChatTurnUpdate Content(string text) =>
        new() { Kind = ChatTurnUpdateKind.Content, Text = text };

    private static ChatTurnUpdate Completed(FinishReason reason) =>
        new() { Kind = ChatTurnUpdateKind.Completed, FinishReason = reason };

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
