using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.ChatTerminal.Output;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>Tests for <see cref="HelpCommand"/>, which shows the names as they are typed.</summary>
public class HelpCommandTests
{
    /// <summary>A command with nothing in it but a name to list.</summary>
    private sealed class NamedCommand : IShellCommand
    {
        public NamedCommand(string name, string summary, string usage)
        {
            Name = name;
            Summary = summary;
            Usage = usage;
        }

        public string Name { get; }

        public string Summary { get; }

        public string Usage { get; }

        public Task ExecuteAsync(ShellCommandContext context) => Task.CompletedTask;
    }

    /// <summary>Runs one help line through a chat session and returns what it printed.</summary>
    private static async Task<List<string>> RunAsync(string line, string prefix, bool highlighted = false)
    {
        var registry = new CommandRegistry();
        var highlights = highlighted ? new CommandHighlights(registry, prefix) : null;
        registry.Register(new HelpCommand(registry, prefix, highlights));
        registry.Register(new NamedCommand("think", "Turns reasoning on or off.", "think [on|off]"));
        registry.Register(new NamedCommand("set-system-prompt",
            "Sets the instructions the model is given.", "set-system-prompt [-y] \"<text>\""));

        var interpreter = new ChatLineInterpreter(registry,
            (_, _, _) => Task.CompletedTask, "chat> ");
        var session = new ShellSession(interpreter);
        var output = new StringBuilder();
        session.OutputProduced += text => output.Append(text);
        session.SendInput(line);
        session.SendInput("\r");
        await session.ExecutionChain;

        return new List<string>(output.ToString().Split("\r\n"));
    }

    [Fact]
    public async Task the_listing_shows_the_names_with_the_slash_the_user_types()
    {
        //Act
        var lines = await RunAsync("/help", "/");

        //Assert
        lines.Should().Contain(l => l.Contains("/help"));
        lines.Should().Contain(l => l.Contains("/think") && l.Contains("Turns reasoning on or off."));
        lines.Should().Contain(l => l.Contains("/set-system-prompt"));
        lines.Should().Contain(l => l.Contains("Type '/help <command>' for usage."));
    }

    [Fact]
    public async Task one_command_s_usage_carries_the_slash_too()
    {
        //Act
        var lines = await RunAsync("/help think", "/");

        //Assert
        lines.Should().Contain("/think - Turns reasoning on or off.");
        lines.Should().Contain("usage: /think [on|off]");
    }

    [Fact]
    public async Task the_name_may_be_asked_for_with_its_slash()
    {
        //Act
        var lines = await RunAsync("/help /think", "/");

        //Assert
        lines.Should().Contain("/think - Turns reasoning on or off.");
    }

    [Fact]
    public async Task an_unknown_name_is_reported_as_the_user_would_type_it()
    {
        //Act
        var lines = await RunAsync("/help nosuch", "/");

        //Assert
        lines.Should().Contain("Unknown command: /nosuch");
    }

    [Fact]
    public async Task without_a_prefix_the_names_are_bare()
    {
        //Act
        var lines = await RunAsync("/help", string.Empty);

        //Assert
        lines.Should().Contain(l => l.Contains("think") && !l.Contains("/think"));
        lines.Should().Contain(l => l.Contains("Type 'help <command>' for usage."));
    }

    /// <summary>
    /// Given highlights, every name in the listing is written in the highlight colour - and the
    /// summaries still line up, because the names were padded before they were coloured.
    /// </summary>
    [Fact]
    public async Task the_listing_highlights_the_names_and_still_lines_the_summaries_up()
    {
        //Act
        var plain = await RunAsync("/help", "/");
        var highlighted = await RunAsync("/help", "/", highlighted: true);

        //Assert - what a reader sees is the same text, column for column
        Strip(highlighted).Should().Equal(plain);

        //Assert - and one row's bytes say where the colour went
        highlighted.Should().Contain("  \x1b[0m\x1b[1;33m/think\x1b[0m"
            + "              Turns reasoning on or off.");
    }

    /// <summary>One command's usage carries the highlight too.</summary>
    [Fact]
    public async Task one_command_s_usage_is_highlighted_as_well()
    {
        //Act
        var lines = await RunAsync("/help think", "/", highlighted: true);

        //Assert
        lines.Should().Contain("\x1b[0m\x1b[1;33m/think\x1b[0m - Turns reasoning on or off.");
        lines.Should().Contain("usage: \x1b[0m\x1b[1;33m/think\x1b[0m [on|off]");
    }

    /// <summary>A name nobody registered is not a command, so the line that says so stays plain.</summary>
    [Fact]
    public async Task an_unknown_name_is_not_highlighted()
    {
        //Act
        var lines = await RunAsync("/help nosuch", "/", highlighted: true);

        //Assert
        lines.Should().Contain("Unknown command: /nosuch");
    }

    /// <summary>With no highlights the listing is what it has always been, byte for byte.</summary>
    [Fact]
    public async Task without_highlights_the_listing_carries_no_escape_sequence()
    {
        //Act
        var lines = await RunAsync("/help", "/");

        //Assert
        lines.Should().NotContain(line => line.Contains('\x1b'));
    }

    /// <summary>What a reader of the terminal sees, with the colour changes taken out.</summary>
    private static List<string> Strip(List<string> lines)
    {
        var stripped = new List<string>();

        foreach (var line in lines)
        {
            stripped.Add(Regex.Replace(line, "\x1b\\[[0-9;]*m", string.Empty));
        }

        return stripped;
    }

    [Fact]
    public async Task an_empty_registry_says_so_instead_of_failing()
    {
        //Arrange
        var registry = new CommandRegistry();
        var help = new HelpCommand(registry, "/");
        var interpreter = new ChatLineInterpreter(registry, (_, _, _) => Task.CompletedTask);
        var session = new ShellSession(interpreter);
        var output = new StringBuilder();
        session.OutputProduced += text => output.Append(text);

        //Act - the help command is not registered, so it has nothing to list
        await help.ExecuteAsync(new ShellCommandContextProbe(session).Context);

        //Assert
        output.ToString().Should().Contain("No commands are registered.");
    }

    /// <summary>Builds a <see cref="ShellCommandContext"/> for a command called directly.</summary>
    private sealed class ShellCommandContextProbe
    {
        public ShellCommandContextProbe(ShellSession session) =>
            Context = new ShellCommandContext(session, [], string.Empty, CancellationToken.None);

        public ShellCommandContext Context { get; }
    }
}
