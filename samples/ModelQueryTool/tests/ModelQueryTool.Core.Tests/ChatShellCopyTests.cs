using CodeBrix.Ollama.ModelRunner;
using ModelQueryTool.Core.Tests.Infrastructure;
using ModelQueryTool.ModelAccess.Models;
using ModelQueryTool.ModelRunning;
using SilverAssertions;
using System.Threading.Tasks;
using Xunit;

namespace ModelQueryTool.Core.Tests;

/// <summary>
/// Tests for <c>/copy</c>: the model's own words reach the clipboard exactly as the model wrote
/// them - not as the terminal wrapped them - and the chat says in one line what it copied without
/// ever writing the text out again.
/// </summary>
public class ChatShellCopyTests
{
    private const string ModelPath = "/tmp/fake-model-query-tool/models/blobs/sha256-weights";

    /// <summary>
    /// A paragraph longer than the terminal is wide reaches the clipboard in one piece: the row
    /// breaks the word wrapper put on the screen are the screen's, not the model's.
    /// </summary>
    [Fact]
    public async Task copy_hands_over_the_answer_as_the_model_wrote_it()
    {
        //Arrange - one paragraph, delivered in the pieces a model streams it in
        using var harness = await LoadedAsync();
        var paragraph = "A proton is a subatomic particle with a positive electric charge, found in the "
            + "nucleus of every atom, and its count is what makes one element different from another.";
        harness.Host.Think = false;
        harness.Host.Updates.Add(Content(paragraph.Substring(0, 40)));
        harness.Host.Updates.Add(Content(paragraph.Substring(40)));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        await harness.SubmitAsync("what is a proton?", TestContext.Current.CancellationToken);
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/copy", TestContext.Current.CancellationToken);

        //Assert - the deltas joined, exactly
        harness.ShellHost.LastCopied.Should().Be(paragraph);
        harness.ShellHost.LastCopied.Should().NotContain("\r");
        harness.ShellHost.LastCopied.Should().NotContain("\x1b");

        //Assert - and the SCREEN shows the same words broken across rows, which is the thing a
        //  mouse copy would have picked up
        harness.PlainText.Should().NotContain(paragraph);
    }

    /// <summary>An indented code block keeps its indentation and its own line breaks, byte for byte.</summary>
    [Fact]
    public async Task copy_keeps_an_indented_code_block_exactly_as_it_arrived()
    {
        //Arrange
        using var harness = await LoadedAsync();
        const string code = "static int Add(int a, int b)\n{\n    return a + b;\n}\n";
        harness.Host.Think = false;
        harness.Host.Updates.Add(Content("static int Add(int a, int b)\n"));
        harness.Host.Updates.Add(Content("{\n    return a + b;\n"));
        harness.Host.Updates.Add(Content("}\n"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        await harness.SubmitAsync("write me an adder", TestContext.Current.CancellationToken);
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/copy", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.LastCopied.Should().Be(code);
    }

    /// <summary>The reasoning is shown on the screen and is never part of what is copied.</summary>
    [Fact]
    public async Task copy_never_carries_the_reasoning()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Updates.Add(Thinking("The user wants a planet. "));
        harness.Host.Updates.Add(Thinking("It is Earth."));
        harness.Host.Updates.Add(Content("Earth"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        await harness.SubmitAsync("which planet?", TestContext.Current.CancellationToken);
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/copy", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.LastCopied.Should().Be("Earth");
    }

    /// <summary>With no answer behind it, the command says so in one dimmed line and copies nothing.</summary>
    [Fact]
    public async Task copy_before_any_turn_copies_nothing()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/copy", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Copied.Should().BeEmpty();
        harness.PlainText.Should().Contain("There is nothing to copy yet - the model has not answered anything.");
    }

    /// <summary>The same for the whole conversation when there is not one yet.</summary>
    [Fact]
    public async Task copy_all_before_any_turn_copies_nothing()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/copy all", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Copied.Should().BeEmpty();
        harness.PlainText.Should().Contain("There is nothing to copy yet - this conversation has no answers in it.");
    }

    /// <summary>A turn stopped part way through copies what was written and says it was incomplete.</summary>
    [Fact]
    public async Task copy_after_a_cancelled_turn_says_it_was_incomplete()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Think = false;
        harness.Host.Updates.Add(Content("The answer is "));
        harness.Host.Updates.Add(Content("forty-two"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        harness.Host.PauseBeforeUpdate = 1;

        harness.Shell.SendInput("how many?");
        harness.Shell.SendInput("\r");
        await harness.Host.ReachedPause.WaitAsync(TestContext.Current.CancellationToken);
        harness.Shell.SendInput("\x03");
        await harness.WaitAsync(TestContext.Current.CancellationToken);
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/copy", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.LastCopied.Should().Be("The answer is ");
        harness.PlainText.Should().Contain(
            "The last answer is on the clipboard - 14 characters, and it was incomplete.");
    }

    /// <summary>
    /// Three turns come out as three labelled blocks, the prompts and the answers alternating,
    /// with one blank row inside a turn and one between turns.
    /// </summary>
    [Fact]
    public async Task copy_all_lays_the_conversation_out_as_labelled_blocks()
    {
        //Arrange
        using var harness = await LoadedAsync();
        harness.Host.Think = false;
        harness.Host.Updates.Add(Content("Yes."));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        await harness.SubmitAsync("first question", TestContext.Current.CancellationToken);
        await harness.SubmitAsync("second question", TestContext.Current.CancellationToken);
        await harness.SubmitAsync("third question", TestContext.Current.CancellationToken);
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/copy all", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.LastCopied.Should().Be(
            "You:\nfirst question\n\nModel:\nYes.\n\n"
            + "You:\nsecond question\n\nModel:\nYes.\n\n"
            + "You:\nthird question\n\nModel:\nYes.");
        harness.PlainText.Should().Contain("The conversation is on the clipboard - 3 turns,");
    }

    /// <summary>A new conversation leaves nothing of the old one to copy.</summary>
    [Fact]
    public async Task clear_starts_the_copied_conversation_afresh()
    {
        //Arrange
        using var harness = await AnsweredAsync();

        //Act
        await harness.SubmitAsync("/clear", TestContext.Current.CancellationToken);
        harness.ClearText();
        await harness.SubmitAsync("/copy all", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Copied.Should().BeEmpty();
        harness.PlainText.Should().Contain("There is nothing to copy yet");
    }

    /// <summary>So does a new system prompt, which starts a new conversation.</summary>
    [Fact]
    public async Task a_new_system_prompt_starts_the_copied_conversation_afresh()
    {
        //Arrange
        using var harness = await AnsweredAsync();

        //Act
        await harness.SubmitAsync("/set-system-prompt -y \"be brief\"", TestContext.Current.CancellationToken);
        harness.ClearText();
        await harness.SubmitAsync("/copy", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Copied.Should().BeEmpty();
        harness.PlainText.Should().Contain("There is nothing to copy yet");
    }

    /// <summary>And so does loading the model again at another context size.</summary>
    [Fact]
    public async Task a_reload_starts_the_copied_conversation_afresh()
    {
        //Arrange
        using var harness = await AnsweredAsync();
        harness.Host.TrainedContextSize = 262144;

        //Act
        await harness.SubmitAsync("/set-context-size -y 16384", TestContext.Current.CancellationToken);
        harness.ClearText();
        await harness.SubmitAsync("/copy", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Copied.Should().BeEmpty();
        harness.PlainText.Should().Contain("There is nothing to copy yet");
    }

    /// <summary>A head that cannot reach a clipboard is reported in one red line.</summary>
    [Fact]
    public async Task copy_says_so_when_the_head_could_not_do_it()
    {
        //Arrange
        using var harness = await AnsweredAsync();
        harness.ShellHost.CopySucceeds = false;
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/copy", TestContext.Current.CancellationToken);

        //Assert
        harness.Text.Should().Contain("\x1b[31mThe clipboard could not be reached, so nothing was copied.");
        harness.PlainText.Should().NotContain("is on the clipboard");
    }

    /// <summary>Anything other than "all" is one error line with the usage, and nothing is copied.</summary>
    [Fact]
    public async Task copy_refuses_an_argument_that_is_not_all()
    {
        //Arrange
        using var harness = await AnsweredAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/copy everything", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.Copied.Should().BeEmpty();
        harness.PlainText.Should().Contain("'everything' is not something to copy. Usage: /copy [all]");

        //Assert - and because it is a registered command like any other, the highlighter lights
        //  its name up wherever a message of the application's own names it
        harness.Text.Should().Contain("\x1b[0m\x1b[1;33m/copy\x1b[0m");
    }

    /// <summary>
    /// The confirmation says what was copied and how much of it, and never repeats the text - the
    /// whole point of the command is that the words are on the clipboard rather than on the screen
    /// a second time.
    /// </summary>
    [Fact]
    public async Task the_confirmation_never_repeats_the_copied_text()
    {
        //Arrange
        using var harness = await AnsweredAsync();
        harness.ClearText();

        //Act
        await harness.SubmitAsync("/copy", TestContext.Current.CancellationToken);

        //Assert
        harness.ShellHost.LastCopied.Should().Be("forty-two");
        harness.PlainText.Should().Contain("The last answer is on the clipboard - 9 characters.");
        harness.PlainText.Should().NotContain("forty-two");
    }

    /// <summary>A chat with a loaded model and nothing said yet.</summary>
    private static async Task<ChatShellHarness> LoadedAsync()
    {
        var harness = new ChatShellHarness();
        harness.Stager.CheckStatus = harness.Stager.CreateStatus(
            ModelStagingState.Ready, ModelPath, 4_000_000_000L, "Every file the manifest names is present.");
        await harness.StartAsync(TestContext.Current.CancellationToken);

        return harness;
    }

    /// <summary>A chat that has had one complete turn, whose answer is "forty-two".</summary>
    private static async Task<ChatShellHarness> AnsweredAsync()
    {
        var harness = await LoadedAsync();
        harness.Host.Think = false;
        harness.Host.Updates.Add(Content("forty-two"));
        harness.Host.Updates.Add(Completed(FinishReason.Stop));
        await harness.SubmitAsync("how many?", TestContext.Current.CancellationToken);

        return harness;
    }

    private static ChatTurnUpdate Thinking(string text) =>
        new() { Kind = ChatTurnUpdateKind.Thinking, Text = text };

    private static ChatTurnUpdate Content(string text) =>
        new() { Kind = ChatTurnUpdateKind.Content, Text = text };

    private static ChatTurnUpdate Completed(FinishReason reason) =>
        new() { Kind = ChatTurnUpdateKind.Completed, FinishReason = reason };
}
