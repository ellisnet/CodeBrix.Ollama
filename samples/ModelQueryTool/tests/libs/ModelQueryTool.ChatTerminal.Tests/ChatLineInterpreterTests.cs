using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.ChatTerminal.Output;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>Tests for <see cref="ChatLineInterpreter"/> - the conversation is the root.</summary>
public class ChatLineInterpreterTests
{
    /// <summary>A command that records what it was given and says so.</summary>
    private sealed class ThinkCommand : IShellCommand
    {
        public List<string> RawCalls { get; } = [];
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public string Name => "think";
        public string Summary => "Turns the model's reasoning on or off.";
        public string Usage => "think [on|off]";

        public Task ExecuteAsync(ShellCommandContext context)
        {
            RawCalls.Add(context.RawArguments);
            Calls.Add(context.Arguments);
            context.IO.WriteLine("thinking changed");
            return Task.CompletedTask;
        }
    }

    /// <summary>A command that waits to be released, so a turn can be cancelled mid-flight.</summary>
    private sealed class WaitingCommand : IShellCommand
    {
        public TaskCompletionSource Started { get; } = new();
        public string Name => "wait";
        public string Summary => "Waits for its token.";
        public string Usage => "wait";

        public async Task ExecuteAsync(ShellCommandContext context)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
    }

    private static (ShellSession Session, List<string> Asked, StringBuilder Output, object Gate, ChatLineInterpreter Interpreter)
        CreateChat(params IShellCommand[] commands)
    {
        var registry = new CommandRegistry();
        foreach (var command in commands) { registry.Register(command); }

        var asked = new List<string>();
        var interpreter = new ChatLineInterpreter(registry,
            (_, line, _) => { asked.Add(line); return Task.CompletedTask; }, "chat> ");
        var session = new ShellSession(interpreter);
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };
        return (session, asked, output, gate, interpreter);
    }

    private static string Drain(StringBuilder output, object gate)
    {
        lock (gate) { return output.ToString(); }
    }

    [Fact]
    public async Task an_ordinary_line_goes_to_the_model_verbatim()
    {
        //Arrange
        var (session, asked, _, _, _) = CreateChat();

        //Act - quotes, backslashes and slashes that are not the first character all survive
        session.SendInput("what does \"a/b\\c\" mean?");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert
        asked.Should().Equal("what does \"a/b\\c\" mean?");
    }

    [Fact]
    public async Task a_pasted_question_reaches_the_model_with_its_line_breaks()
    {
        //Arrange
        var (session, asked, _, _, _) = CreateChat();

        //Act
        session.SendInput("first line\rsecond line\r");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert
        asked.Should().Equal("first line\nsecond line\n");
    }

    [Fact]
    public async Task a_line_that_starts_with_a_slash_is_a_command()
    {
        //Arrange
        var think = new ThinkCommand();
        var (session, asked, output, gate, _) = CreateChat(think);

        //Act
        session.SendInput("/think off");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert - the registry holds it under its bare name
        think.Calls.Should().HaveCount(1);
        think.Calls[0].Should().Equal("off");
        asked.Should().BeEmpty();
        Drain(output, gate).Should().Contain("thinking changed");
    }

    [Fact]
    public async Task leading_whitespace_before_the_slash_is_still_a_command()
    {
        //Arrange
        var think = new ThinkCommand();
        var (session, asked, _, _, _) = CreateChat(think);

        //Act
        session.SendInput("   /think on");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert
        think.Calls.Should().HaveCount(1);
        asked.Should().BeEmpty();
    }

    [Fact]
    public async Task a_slash_inside_a_question_is_not_a_command()
    {
        //Arrange
        var think = new ThinkCommand();
        var (session, asked, _, _, _) = CreateChat(think);

        //Act
        session.SendInput("compare and/or contrast");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert
        asked.Should().Equal("compare and/or contrast");
        think.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task an_unknown_command_prints_one_line_naming_help()
    {
        //Arrange
        var (session, asked, output, gate, _) = CreateChat(new ThinkCommand());

        //Act
        session.SendInput("/nosuch");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert
        asked.Should().BeEmpty();
        Drain(output, gate).Should().Contain("There is no /nosuch command - /help lists the ones there are.");
    }

    /// <summary>
    /// Given highlights, the one command the hint names lights up - and the name the user typed,
    /// which is a command nowhere, does not.
    /// </summary>
    [Fact]
    public async Task the_unknown_command_hint_highlights_only_the_command_that_exists()
    {
        //Arrange
        var registry = new CommandRegistry();
        registry.Register(new HelpCommand(registry, ChatLineInterpreter.CommandPrefix));
        var interpreter = new ChatLineInterpreter(registry, (_, _, _) => Task.CompletedTask, "chat> ",
            new CommandHighlights(registry));
        var session = new ShellSession(interpreter);
        var output = new StringBuilder();
        var gate = new object();
        session.OutputProduced += text => { lock (gate) { output.Append(text); } };

        //Act
        session.SendInput("/nosuch");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert
        Drain(output, gate).Should().Contain(
            "There is no /nosuch command - \x1b[0m\x1b[1;33m/help\x1b[0m lists the ones there are.");
    }

    [Fact]
    public async Task a_command_keeps_the_quotes_its_argument_needs()
    {
        //Arrange
        var think = new ThinkCommand();
        var (session, _, _, _, _) = CreateChat(think);

        //Act
        session.SendInput("/think -y \"answer as a \\\"friendly\\\" tutor\"");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert - the raw arguments are what the user typed after the command word
        think.RawCalls.Should().Equal("-y \"answer as a \\\"friendly\\\" tutor\"");
    }

    [Fact]
    public async Task a_blank_line_asks_nothing()
    {
        //Arrange
        var (session, asked, _, _, _) = CreateChat();

        //Act
        session.SendInput("   ");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert
        asked.Should().BeEmpty();
    }

    [Fact]
    public async Task ctrl_c_cancels_the_running_line()
    {
        //Arrange
        var waiting = new WaitingCommand();
        var (session, _, output, gate, _) = CreateChat(waiting);
        session.SendInput("/wait");
        session.SendInput("\r");
        await waiting.Started.Task;

        //Act
        session.SendInput("\x03");
        await session.ExecutionChain;

        //Assert - the cancellation is reported and the prompt comes back
        Drain(output, gate).Should().Contain("^C");
        Drain(output, gate).Should().EndWith("chat> ");
    }

    [Fact]
    public void the_prompt_is_the_interpreter_s_own()
    {
        //Arrange
        var (session, _, _, _, _) = CreateChat();

        //Assert
        session.Prompt.Should().Be("chat> ");
    }

    [Fact]
    public void the_commands_are_reachable_through_the_interpreter()
    {
        //Arrange
        var think = new ThinkCommand();
        var (_, _, _, _, interpreter) = CreateChat(think);

        //Assert
        interpreter.Commands.TryGet("think", out var found).Should().BeTrue();
        found.Should().BeSameAs(think);
    }

    [Theory]
    [InlineData("/help", true)]
    [InlineData("  /help", true)]
    [InlineData("/", true)]
    [InlineData("ask /help", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void is_command_is_about_the_first_non_blank_character(string line, bool expected) =>
        ChatLineInterpreter.IsCommand(line).Should().Be(expected);

    [Fact]
    public void the_commands_must_not_be_null()
    {
        //Act
        Action act = () => new ChatLineInterpreter(null, (_, _, _) => Task.CompletedTask);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void the_chat_handler_must_not_be_null()
    {
        //Act
        Action act = () => new ChatLineInterpreter(new CommandRegistry(), null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void the_default_prompt_is_the_chat_one() =>
        new ChatLineInterpreter(new CommandRegistry(), (_, _, _) => Task.CompletedTask)
            .Prompt.Should().Be("> ");
}
