using CodeBrix.Ollama.ModelRunner;
using ModelQueryTool.Core.Tests.Infrastructure;
using ModelQueryTool.ModelAccess.Models;
using ModelQueryTool.ModelRunning;
using SilverAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace ModelQueryTool.Core.Tests;

/// <summary>
/// Tests for the commands that do not touch the store: help, status, reasoning, a new
/// conversation, the system prompt, and leaving.
/// </summary>
public class ChatShellCommandTests
{
    private const string ModelPath = "/tmp/fake-model-query-tool/models/blobs/sha256-weights";

    /// <summary>The help lists the commands the way the user has to type them.</summary>
    [Fact]
    public async Task help_lists_the_commands_with_the_slash_in_front_of_them()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/help", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("Available commands:");
        harness.Text.Should().Contain("/status");
        harness.Text.Should().Contain("/set-system-prompt");
        harness.Text.Should().Contain("/set-context-size");
        harness.Text.Should().Contain("/download");
        harness.Text.Should().Contain("/remove");
        harness.Text.Should().Contain("/think");
        harness.Text.Should().Contain("/clear");
        harness.Text.Should().Contain("/copy");
        harness.Text.Should().Contain("/exit");
    }

    /// <summary>A slash that names nothing points at the help rather than guessing.</summary>
    [Fact]
    public async Task an_unknown_command_points_at_the_help()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/wibble", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("There is no /wibble command");
        harness.Host.Sent.Should().BeEmpty();
    }

    /// <summary>The status reports the store, the loaded model and the conversation.</summary>
    [Fact]
    public async Task status_reports_the_store_and_the_loaded_model()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.ContextSize = 8192;
        harness.Host.TrainedContextSize = 262144;
        harness.Host.ConversationTokens = 2100;
        harness.Host.Details = new ModelDetails { Description = "qwen35moe 35B Q4_K_M" };
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/status", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("Model on disk");
        harness.Text.Should().Contain("Ready");
        harness.Text.Should().Contain(harness.Stager.StoreDirectory);
        harness.Text.Should().Contain("Model in memory");
        harness.Text.Should().Contain("qwen35moe 35B Q4_K_M");
        harness.Text.Should().Contain("8,192 tokens (2,100 used by the conversation)");
        harness.Text.Should().Contain("262,144 tokens");
        harness.Text.Should().Contain("thinking");
        harness.Text.Should().Contain("(none)");
        harness.Text.Should().Contain("(none yet)");
    }

    /// <summary>
    /// The folder the status reports is a path, not something to type, so nothing in it lights up
    /// - and neither does the command the user typed to ask for it.
    /// </summary>
    [Fact]
    public async Task status_highlights_nothing_at_all()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/status", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain(harness.Stager.StoreDirectory);
        harness.Text.Should().NotContain("\x1b[1;33m");
    }

    /// <summary>The commands the help lists are the ones a person types, so each one stands out.</summary>
    [Fact]
    public async Task help_highlights_the_names_it_lists()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/help", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("\x1b[0m\x1b[1;33m/status\x1b[0m");
        harness.Text.Should().Contain("\x1b[0m\x1b[1;33m/set-system-prompt\x1b[0m");

        //Assert - and the summaries still start in the same column, because the names were padded
        //  as plain text and coloured afterwards (the column is the longest name, its slash, and
        //  two spaces)
        var column = "/set-system-prompt".Length + 2;
        harness.PlainText.Should().Contain("  " + "/help".PadRight(column) + "Lists commands");
        harness.PlainText.Should().Contain("  " + "/set-system-prompt".PadRight(column) + "Sets the instruction");
    }

    /// <summary>
    /// The name of a command that does not exist is quoted back as it was typed; only the one that
    /// does exist is worth showing as something to type.
    /// </summary>
    [Fact]
    public async Task the_unknown_command_hint_highlights_only_the_help()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/wibble", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain(
            "There is no /wibble command - \x1b[0m\x1b[1;33m/help\x1b[0m lists the ones there are.");
    }

    /// <summary>Once a turn has finished, the status says what it cost and how fast it ran.</summary>
    [Fact]
    public async Task status_reports_the_last_turn_when_there_is_one()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(new ChatTurnUpdate { Kind = ChatTurnUpdateKind.Content, Text = "Earth" });
        harness.Host.Updates.Add(new ChatTurnUpdate
        {
            Kind = ChatTurnUpdateKind.Completed,
            FinishReason = FinishReason.Stop,
            Statistics = new GenerationStatistics
            {
                PromptTokens = 2100,
                CachedPromptTokens = 0,
                GeneratedTokens = 300,
                PromptDuration = TimeSpan.FromSeconds(42),
                GenerationDuration = TimeSpan.FromSeconds(20),
                TotalDuration = TimeSpan.FromSeconds(62),
            },
        });
        await harness.SubmitAsync("which planet?", TestContext.Current.CancellationToken);
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/status", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("2,100 tokens (0 of them cached) read in 42.0 s");
        harness.Text.Should().Contain("300 tokens at 15.0 a second");
        harness.Text.Should().Contain("62.0 s");
        harness.Text.Should().Contain("Stop");
    }

    /// <summary>Asked with no argument, the reasoning switch says which way it is set.</summary>
    [Fact]
    public async Task think_without_an_argument_says_which_way_it_is_set()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/think", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("Thinking is on.");
        harness.Host.Think.Should().Be(true);
    }

    /// <summary>Turning reasoning off turns it off on the host.</summary>
    [Fact]
    public async Task think_off_turns_the_reasoning_off()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/think off", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.Think.Should().Be(false);
        harness.Text.Should().Contain("Thinking is off");
    }

    /// <summary>And turning it on again turns it on.</summary>
    [Fact]
    public async Task think_on_turns_the_reasoning_on()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Think = false;
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/think on", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.Think.Should().Be(true);
        harness.Text.Should().Contain("Thinking is on");
    }

    /// <summary>A word that is neither on nor off changes nothing and says what is accepted.</summary>
    [Fact]
    public async Task think_with_a_word_that_is_neither_changes_nothing()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/think maybe", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.Think.Should().Be(true);
        harness.Text.Should().Contain("'maybe' is neither on nor off");
    }

    /// <summary>Clearing starts a new conversation and wipes the screen.</summary>
    [Fact]
    public async Task clear_starts_a_new_conversation_and_wipes_the_screen()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/clear", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.ClearCalls.Should().Be(1);
        harness.Text.Should().Contain("\x1b[2J\x1b[H");
        harness.Text.Should().Contain("New conversation");
    }

    /// <summary>Without the confirmation the system prompt is explained and nothing is set.</summary>
    [Fact]
    public async Task set_system_prompt_without_the_confirmation_changes_nothing()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/set-system-prompt \"be brief\"", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.SystemPromptCalls.Should().Be(0);
        harness.Host.SystemPrompt.Should().BeNull();
        harness.Text.Should().Contain("This would make the system prompt: be brief");
        harness.Text.Should().Contain("starts a new conversation");
        harness.Text.Should().Contain("Nothing has changed.");
    }

    /// <summary>With the confirmation it is set, and the quotes inside the text survive.</summary>
    [Fact]
    public async Task set_system_prompt_with_the_confirmation_sets_it_verbatim()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync(
            "/set-system-prompt -y \"answer like a \"friendly\" tutor\"", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.SystemPromptCalls.Should().Be(1);
        harness.Host.SystemPrompt.Should().Be("answer like a \"friendly\" tutor");
        harness.Text.Should().Contain("The system prompt is set");
    }

    /// <summary>An empty pair of quotes with the confirmation clears the system prompt.</summary>
    [Fact]
    public async Task set_system_prompt_with_empty_quotes_clears_it()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.SetSystemPrompt("be brief");
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/set-system-prompt -y \"\"", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.SystemPrompt.Should().BeNull();
        harness.Text.Should().Contain("The system prompt is cleared");
    }

    /// <summary>Leaving stops the model, unloads it, and closes the application.</summary>
    [Fact]
    public async Task exit_stops_the_model_and_closes_the_application()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/exit", TestContext.Current.CancellationToken);

        //Assert
        harness.Host.DisposeCalls.Should().Be(1);
        harness.ShellHost.ExitCalls.Should().Be(1);
        harness.Text.Should().Contain("Stopping the model and closing");
    }

    /// <summary>Once the model has been put away, the status says so instead of asking a disposed host.</summary>
    [Fact]
    public async Task status_after_leaving_never_reaches_the_host()
    {
        //Arrange
        using var harness = await LoadedAsync();
        await harness.SubmitAsync("/exit", TestContext.Current.CancellationToken);
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/status", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("The model has been unloaded and the application is closing.");
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
