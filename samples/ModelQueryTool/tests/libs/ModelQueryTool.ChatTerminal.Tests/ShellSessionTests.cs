using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.ChatTerminal.Commands;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>Tests for <see cref="ShellSession"/>.</summary>
public class ShellSessionTests
{
    private sealed class ProbeCommand : IShellCommand
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public List<string> RawCalls { get; } = [];
        public string Name => "probe";
        public string Summary => "Test probe.";
        public string Usage => "probe [args]";

        public Task ExecuteAsync(ShellCommandContext context)
        {
            Calls.Add(context.Arguments);
            RawCalls.Add(context.RawArguments);
            context.IO.WriteLine("probed");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingInterpreter : ILineInterpreter
    {
        public List<string> Lines { get; } = [];
        public string Prompt => "sub> ";

        public Task HandleLineAsync(ShellSession session, string line,
            CancellationToken cancellationToken)
        {
            Lines.Add(line);
            return Task.CompletedTask;
        }
    }

    private static (ShellSession Session, StringBuilder Output, object Gate) CreateSession(
        params IShellCommand[] commands)
    {
        var registry = new CommandRegistry();
        foreach (var command in commands) { registry.Register(command); }

        var session = new ShellSession(registry, new ShellSessionOptions { Prompt = "chat> " });
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };
        return (session, output, gate);
    }

    private static string Drain(StringBuilder output, object gate)
    {
        lock (gate) { return output.ToString(); }
    }

    /// <summary>
    /// Types a line and presses Enter, the way the control delivers them: the
    /// typing in one chunk, the Enter key in a chunk of its own. A chunk that is
    /// a line of text AND a line ending is a paste, not a keystroke - see
    /// <see cref="PasteRuleTests"/>.
    /// </summary>
    private static void TypeLine(ShellSession session, string line)
    {
        if (line.Length > 0) { session.SendInput(line); }
        session.SendInput("\r");
    }

    [Fact]
    public void start_writes_banner_then_prompt()
    {
        //Arrange
        var registry = new CommandRegistry();
        var session = new ShellSession(registry,
            new ShellSessionOptions { Prompt = "chat> ", Banner = ["Welcome"] });
        var output = new StringBuilder();
        session.OutputProduced += text => output.Append(text);

        //Act
        session.Start();

        //Assert
        output.ToString().Should().Be("Welcome\r\nchat> ");
    }

    [Fact]
    public void typed_characters_are_echoed()
    {
        //Arrange
        var (session, output, gate) = CreateSession();

        //Act
        session.SendInput("hi");

        //Assert
        Drain(output, gate).Should().Be("hi");
    }

    [Fact]
    public async Task a_submitted_line_dispatches_the_command_with_its_arguments()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, output, gate) = CreateSession(probe);

        //Act
        TypeLine(session, "probe one \"two words\"");
        await session.ExecutionChain;

        //Assert
        probe.Calls.Should().HaveCount(1);
        probe.Calls[0].Should().Equal("one", "two words");
        Drain(output, gate).Should().Be("probe one \"two words\"\r\nprobed\r\nchat> ");
    }

    [Fact]
    public async Task an_unknown_command_is_reported_and_the_prompt_returns()
    {
        //Arrange
        var (session, output, gate) = CreateSession();

        //Act
        TypeLine(session, "nosuch");
        await session.ExecutionChain;

        //Assert
        Drain(output, gate).Should().Contain("Unknown command: nosuch");
        Drain(output, gate).Should().EndWith("chat> ");
    }

    [Fact]
    public async Task crlf_submits_the_line_once()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, _, _) = CreateSession(probe);

        //Act
        session.SendInput("probe");
        session.SendInput("\r\n");
        await session.ExecutionChain;

        //Assert
        probe.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task a_bare_lf_also_submits()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, _, _) = CreateSession(probe);

        //Act
        session.SendInput("probe");
        session.SendInput("\n");
        await session.ExecutionChain;

        //Assert
        probe.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task up_arrow_recalls_the_previous_line()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, output, gate) = CreateSession(probe);
        TypeLine(session, "probe alpha");
        await session.ExecutionChain;

        //Act
        session.SendInput("\x1b[A");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert
        probe.Calls.Should().HaveCount(2);
        probe.Calls[1].Should().Equal("alpha");
        Drain(output, gate).Should().Contain("chat> probe alpha");
    }

    [Fact]
    public void ctrl_c_on_an_idle_line_abandons_it_and_reprompts()
    {
        //Arrange
        var (session, output, gate) = CreateSession();
        session.SendInput("half a line");

        //Act
        session.SendInput("\x03");

        //Assert
        Drain(output, gate).Should().Be("half a line^C\r\nchat> ");
    }

    [Fact]
    public async Task a_command_exception_is_reported_not_thrown()
    {
        //Arrange
        var registry = new CommandRegistry();
        registry.Register(new ThrowingCommand());
        var session = new ShellSession(registry, new ShellSessionOptions { Prompt = "chat> " });
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };

        //Act
        TypeLine(session, "boom");
        await session.ExecutionChain;

        //Assert
        Drain(output, gate).Should().Contain("error: it broke");
        Drain(output, gate).Should().EndWith("chat> ");
    }

    private sealed class ThrowingCommand : IShellCommand
    {
        public string Name => "boom";
        public string Summary => "Throws.";
        public string Usage => "boom";

        public Task ExecuteAsync(ShellCommandContext context) =>
            throw new System.InvalidOperationException("it broke");
    }

    [Fact]
    public async Task a_pushed_interpreter_receives_lines_and_ctrl_d_pops_it()
    {
        //Arrange
        var recorder = new RecordingInterpreter();
        var (session, output, gate) = CreateSession();
        session.PushInterpreter(recorder);

        //Act
        TypeLine(session, "ask something");
        await session.ExecutionChain;
        session.SendInput("\x04");

        //Assert
        recorder.Lines.Should().Equal("ask something");
        session.Prompt.Should().Be("chat> ");
        Drain(output, gate).Should().EndWith("\r\nchat> ");
    }

    private sealed class WaitingCommand : IShellCommand
    {
        public TaskCompletionSource Gate { get; } = new();
        public string Name => "wait";
        public string Summary => "Waits until released.";
        public string Usage => "wait";

        public Task ExecuteAsync(ShellCommandContext context) => Gate.Task;
    }

    [Fact]
    public void write_out_of_band_while_idle_repaints_the_prompt_and_input()
    {
        //Arrange
        var (session, output, gate) = CreateSession();
        session.Start();
        session.SendInput("hal");

        //Act
        session.WriteOutOfBand("Engine ready.");

        //Assert - the input line is wound back and erased, the message takes its
        //  place, and prompt + half-typed input come back underneath it
        Drain(output, gate).Should().Be("chat> hal\r\x1b[JEngine ready.\r\nchat> hal");
    }

    [Fact]
    public void write_out_of_band_before_the_session_starts_writes_the_lines_only()
    {
        //Arrange - nothing has been written yet, so there is no prompt to take the place of
        var (session, output, gate) = CreateSession();

        //Act
        session.WriteOutOfBand("Engine ready.");

        //Assert
        Drain(output, gate).Should().Be("Engine ready.\r\n");
    }

    [Fact]
    public async Task write_out_of_band_while_a_command_runs_writes_the_message_only()
    {
        //Arrange
        var waiting = new WaitingCommand();
        var (session, output, gate) = CreateSession(waiting);
        TypeLine(session, "wait");

        //Act - the message arrives mid-command; the command then completes
        session.WriteOutOfBand("Engine ready.");
        waiting.Gate.SetResult();
        await session.ExecutionChain;

        //Assert - exactly ONE prompt, printed by the command's completion
        var text = Drain(output, gate);
        text.Should().Be("wait\r\nEngine ready.\r\nchat> ");
    }

    [Fact]
    public async Task blank_lines_are_not_added_to_history()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, output, gate) = CreateSession(probe);

        //Act - submit a blank, then a real line, then recall with Up twice
        session.SendInput("\r");
        await session.ExecutionChain;
        TypeLine(session, "probe x");
        await session.ExecutionChain;
        session.SendInput("\x1b[A\x1b[A");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert - the recalled line is "probe x", not the blank
        probe.Calls.Should().HaveCount(2);
        probe.Calls[1].Should().Equal("x");
    }

    [Fact]
    public async Task raw_arguments_keep_what_tokenizing_throws_away()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, _, _) = CreateSession(probe);

        //Act
        //A prompt such as  say "hello" twice  is the case this exists for: the quotes
        //belong to the text the user is sending, not to the command line, and a token
        //list cannot tell the session that.
        TypeLine(session, "probe say \"hello\" twice");
        await session.ExecutionChain;

        //Assert
        //The control is the token list from the very same line, which lost them.
        probe.RawCalls.Should().Equal("say \"hello\" twice");
        probe.Calls[0].Should().Equal("say", "hello", "twice");
    }

    [Fact]
    public async Task raw_arguments_are_the_line_after_the_command_word()
    {
        //Arrange
        var probe = new ProbeCommand();
        var (session, _, _) = CreateSession(probe);

        //Act
        //Leading and trailing whitespace is not an argument; the whitespace BETWEEN
        //arguments is part of what was typed and stays.
        TypeLine(session, "   probe   one    two   ");
        TypeLine(session, "probe");
        await session.ExecutionChain;

        //Assert
        probe.RawCalls.Should().Equal("one    two", string.Empty);
    }

    [Fact]
    public async Task a_multi_line_paste_is_one_submitted_line()
    {
        //Arrange
        var recorder = new RecordingInterpreter();
        var session = new ShellSession(recorder);
        session.Start();

        //Act - the control delivers a paste as one chunk with CR line endings
        session.SendInput("first\rsecond\r\nthird\r");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert - one turn, with the line endings kept as breaks inside it
        recorder.Lines.Should().Equal("first\nsecond\nthird\n");
    }

    [Fact]
    public async Task a_paste_is_echoed_with_a_row_per_line()
    {
        //Arrange
        var recorder = new RecordingInterpreter();
        var session = new ShellSession(recorder);
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };
        session.Start();

        //Act
        session.SendInput("one\rtwo");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert
        Drain(output, gate).Should().Be("sub> one\r\ntwo\r\nsub> ");
    }

    [Fact]
    public async Task an_enter_key_of_its_own_still_submits()
    {
        //Arrange
        var recorder = new RecordingInterpreter();
        var session = new ShellSession(recorder);

        //Act - a keystroke at a time, which is how a typed line arrives
        foreach (var c in "typed") { session.SendInput(c.ToString()); }
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert
        recorder.Lines.Should().Equal("typed");
    }

    [Fact]
    public void the_grid_size_defaults_to_eighty_columns()
    {
        //Arrange
        var recorder = new RecordingInterpreter();

        //Act
        var session = new ShellSession(recorder);

        //Assert
        session.Columns.Should().Be(80);
        session.Rows.Should().Be(0);
    }

    [Fact]
    public void the_grid_size_before_the_session_starts_writes_nothing()
    {
        //Arrange
        var recorder = new RecordingInterpreter();
        var session = new ShellSession(recorder);
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };

        //Act
        session.SetGridSize(40, 12);

        //Assert - the first prompt has not been written yet, so there is nothing to repaint
        Drain(output, gate).Should().Be("");
        session.Columns.Should().Be(40);
        session.Rows.Should().Be(12);
    }

    [Fact]
    public void a_resize_repaints_the_prompt_and_the_half_typed_line()
    {
        //Arrange
        var recorder = new RecordingInterpreter();
        var session = new ShellSession(recorder);
        var output = new StringBuilder();
        var gate = new object();
        session.SetGridSize(40, 12);
        session.Start();
        session.SendInput("half a line");
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };

        //Act
        session.SetGridSize(30, 12);

        //Assert - wind back, clear, prompt, line
        Drain(output, gate).Should().Be("\r\x1b[Jsub> half a line");
        session.Columns.Should().Be(30);
    }

    [Fact]
    public async Task a_resize_while_a_line_is_running_writes_nothing_into_its_output()
    {
        //Arrange - a terminal control shows its scrollbar the first time its content
        //  scrolls, and loses a column or two in the middle of whatever is being written
        var waiting = new WaitingCommand();
        var (session, output, gate) = CreateSession(waiting);
        session.SetGridSize(40, 12);
        session.Start();
        TypeLine(session, "wait");

        //Act
        session.SetGridSize(38, 12);
        var whileRunning = Drain(output, gate);
        waiting.Gate.SetResult();
        await session.ExecutionChain;

        //Assert - no prompt, and no attribute reset, in the middle of the running
        //  line's output; the new width is kept; one prompt when the line ends
        whileRunning.Should().Be("chat> wait\r\n");
        session.Columns.Should().Be(38);
        Drain(output, gate).Should().Be("chat> wait\r\nchat> ");
    }

    [Fact]
    public void a_resize_to_the_same_width_writes_nothing()
    {
        //Arrange
        var recorder = new RecordingInterpreter();
        var session = new ShellSession(recorder);
        session.SetGridSize(40, 12);
        session.Start();
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };

        //Act - the control raises its event for a row change too
        session.SetGridSize(40, 20);

        //Assert
        Drain(output, gate).Should().Be("");
        session.Rows.Should().Be(20);
    }

    [Fact]
    public async Task a_surrogate_pair_arrives_as_two_tokens_and_is_inserted_once()
    {
        //Arrange
        var recorder = new RecordingInterpreter();
        var session = new ShellSession(recorder);
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };
        session.Start();

        //Act - the tokenizer works in UTF-16 units, so the emoji is two of them
        session.SendInput("\U0001F600");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert - one character on screen, and the whole pair in the line
        Drain(output, gate).Should().Be("sub> \U0001F600\r\nsub> ");
        recorder.Lines.Should().Equal("\U0001F600");
    }

    [Fact]
    public void half_a_surrogate_pair_is_held_until_the_chunk_ends()
    {
        //Arrange
        var recorder = new RecordingInterpreter();
        var session = new ShellSession(recorder);
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };
        session.Start();

        //Act - the high half alone, then the low half in the next chunk
        session.SendInput("\ud83d");
        var afterHigh = Drain(output, gate);
        session.SendInput("\ude00");

        //Assert - nothing was drawn until the pair was whole, and then it went
        //  down as one character
        afterHigh.Should().Be("sub> ");
        Drain(output, gate).Should().Be("sub> \U0001F600");
    }

    [Fact]
    public void ctrl_c_on_a_wrapped_line_goes_past_its_last_row_first()
    {
        //Arrange - a line that wraps onto a second row
        var recorder = new RecordingInterpreter();
        var session = new ShellSession(recorder);
        session.SetGridSize(20, 10);
        session.Start();
        session.SendInput("0123456789abcdefgh");
        for (var i = 0; i < 5; i++) { session.SendInput("\x1b[D"); }
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };

        //Act
        session.SendInput("\x03");

        //Assert - the cursor is taken to the end of the line before the newline,
        //  so the abandoned rows are above the next prompt and not beside it
        Drain(output, gate).Should().StartWith("\x1b[1B\r\x1b[3C^C\r\n");
        Drain(output, gate).Should().EndWith("sub> ");
    }
}
