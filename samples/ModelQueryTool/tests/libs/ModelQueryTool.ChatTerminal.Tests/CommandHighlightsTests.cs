using System.Collections.Generic;
using System.Threading.Tasks;
using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.ChatTerminal.Output;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>
/// Tests for <see cref="CommandHighlights"/> - what counts as a mention of a command in a line the
/// application wrote, and what is only a folder path, a word with a slash in it, or a name nobody
/// registered.
/// </summary>
public class CommandHighlightsTests
{
    /// <summary>The names the application in these tests has registered.</summary>
    private static readonly string[] ChatCommands =
        ["help", "status", "think", "clear", "download", "remove", "set-system-prompt", "set-context-size", "exit"];

    /// <summary>A command with nothing in it but a name, for filling a registry.</summary>
    private sealed class NamedCommand : IShellCommand
    {
        public NamedCommand(string name) => Name = name;

        public string Name { get; }

        public string Summary => "Does something.";

        public string Usage => Name;

        public Task ExecuteAsync(ShellCommandContext context) => Task.CompletedTask;
    }

    /// <summary>The highlights over the usual set of chat commands.</summary>
    private static CommandHighlights Chat() => new(ChatCommands);

    /// <summary>What the found spans actually cover, which is what a reader of the terminal sees.</summary>
    private static List<string> Found(CommandHighlights highlights, string text)
    {
        var found = new List<string>();

        foreach (var span in highlights.FindSpans(text))
        {
            found.Add(text.Substring(span.Start, span.Length));
        }

        return found;
    }

    /// <summary>A registered command inside a sentence is one span, the name alone.</summary>
    [Fact]
    public void FindSpans_finds_a_registered_command_in_a_sentence() =>
        Found(Chat(), "Type /help for the commands.").Should().Equal("/help");

    /// <summary>The span says where the mention is, not only what it is.</summary>
    [Fact]
    public void FindSpans_reports_where_the_mention_starts_and_how_long_it_is()
    {
        //Act
        var spans = Chat().FindSpans("Type /help for the commands.");

        //Assert
        spans.Should().ContainSingle();
        spans[0].Start.Should().Be(5);
        spans[0].Length.Should().Be(5);
        spans[0].End.Should().Be(10);
    }

    /// <summary>A folder path is text: none of its parts is a command, whatever the slashes suggest.</summary>
    [Fact]
    public void FindSpans_leaves_a_folder_path_alone() =>
        Found(Chat(), "The model is in /home/someone/.local/share/ModelQueryTool/models.").Should().BeEmpty();

    /// <summary>A path whose last part IS a command name is still a path, because a letter is in front of it.</summary>
    [Fact]
    public void FindSpans_leaves_a_path_that_ends_in_a_command_name_alone() =>
        Found(Chat(), "/var/tmp/status").Should().BeEmpty();

    /// <summary>A name nobody registered is quoted back to the user as it was typed, not as something to type.</summary>
    [Fact]
    public void FindSpans_leaves_an_unknown_name_alone() =>
        Found(Chat(), "There is no /foo command - /help lists the ones there are.").Should().Equal("/help");

    /// <summary>A slash inside a word is not the start of anything.</summary>
    /// <param name="text">The line the application would write.</param>
    [Theory]
    [InlineData("a/help")]
    [InlineData("and/or")]
    [InlineData("compare this/status with that")]
    public void FindSpans_leaves_a_slash_inside_a_word_alone(string text) =>
        Found(Chat(), text).Should().BeEmpty();

    /// <summary>A name that merely begins with a command's name is a different word.</summary>
    /// <param name="text">The line the application would write.</param>
    [Theory]
    [InlineData("/helpful")]
    [InlineData("/think-tank")]
    [InlineData("/status_quo")]
    [InlineData("/exit2")]
    public void FindSpans_leaves_a_longer_word_alone(string text) =>
        Found(Chat(), text).Should().BeEmpty();

    /// <summary>The longest name wins, so a command whose name starts with another one is found whole.</summary>
    [Fact]
    public void FindSpans_finds_the_longest_name_first()
    {
        //Arrange
        var highlights = new CommandHighlights(["set", "set-system-prompt"]);

        //Act
        var found = Found(highlights, "Type /set-system-prompt to change it.");

        //Assert
        found.Should().Equal("/set-system-prompt");
    }

    /// <summary>A name is matched as it was registered, letter for letter.</summary>
    [Fact]
    public void FindSpans_matches_a_name_case_sensitively() =>
        Found(Chat(), "Type /HELP or /Help.").Should().BeEmpty();

    /// <summary>The argument written with a command belongs to it: what a person types is "/think off".</summary>
    [Fact]
    public void FindSpans_takes_in_the_argument_written_with_the_command() =>
        Found(Chat(), "Thinking is on; /think off turns it off.").Should().Equal("/think off");

    /// <summary>An option belongs to the command in front of it too.</summary>
    /// <param name="text">The line the application would write.</param>
    /// <param name="expected">The one mention in it.</param>
    [Theory]
    [InlineData("Add -y to start it: /download -y", "/download -y")]
    [InlineData("Nothing has been deleted: /remove -y", "/remove -y")]
    [InlineData("usage: /set-context-size -y 16384", "/set-context-size -y")]
    [InlineData("Turn it back on with /think on.", "/think on")]
    [InlineData("usage: /think on|off", "/think on|off")]
    public void FindSpans_takes_in_an_option_or_a_switch(string text, string expected) =>
        Found(Chat(), text).Should().Equal(expected);

    /// <summary>A word that is neither an option nor a switch ends the mention.</summary>
    [Fact]
    public void FindSpans_stops_at_the_first_word_that_is_not_an_argument() =>
        Found(Chat(), "/download the model").Should().Equal("/download");

    /// <summary>
    /// The text has already been laid out against the terminal's width, so a row break can fall
    /// between a command and its argument - and the mention is still one mention.
    /// </summary>
    [Fact]
    public void FindSpans_reads_an_argument_across_a_row_break() =>
        Found(Chat(), "Thinking is on; /think\r\noff turns it off.").Should().Equal("/think\r\noff");

    /// <summary>Punctuation around a command is punctuation, and the command is still found.</summary>
    /// <param name="text">The line the application would write.</param>
    [Theory]
    [InlineData("Everything is listed by /help.")]
    [InlineData("Everything is listed by '/help'.")]
    [InlineData("Everything is listed by (/help).")]
    [InlineData("Everything is listed by /help, as it happens.")]
    [InlineData("Everything is listed by \"/help\".")]
    public void FindSpans_finds_a_command_written_in_punctuation(string text) =>
        Found(Chat(), text).Should().Equal("/help");

    /// <summary>A command at the very start of the text is found: there is nothing in front of it.</summary>
    [Fact]
    public void FindSpans_finds_a_command_at_the_very_start() =>
        Found(Chat(), "/help lists the commands.").Should().Equal("/help");

    /// <summary>A command at the very end of the text is found: there is nothing after it either.</summary>
    [Fact]
    public void FindSpans_finds_a_command_at_the_very_end() =>
        Found(Chat(), "The commands are listed by /help").Should().Equal("/help");

    /// <summary>A sentence that names two commands has two mentions, in the order they were written.</summary>
    [Fact]
    public void FindSpans_finds_two_commands_in_one_sentence() =>
        Found(Chat(), "/clear starts a new conversation, and /set-context-size -y 16384 makes more room.")
            .Should().Equal("/clear", "/set-context-size -y");

    /// <summary>
    /// The registry is read when a line is looked at, not when the highlights were made - an
    /// application registers its commands after it has built the pieces that write text.
    /// </summary>
    [Fact]
    public void FindSpans_counts_a_command_registered_after_it_was_created()
    {
        //Arrange
        var registry = new CommandRegistry();
        var highlights = new CommandHighlights(registry);
        var before = Found(highlights, "Type /think off to stop it.");

        //Act
        registry.Register(new NamedCommand("think"));
        var after = Found(highlights, "Type /think off to stop it.");

        //Assert
        before.Should().BeEmpty();
        after.Should().Equal("/think off");
    }

    /// <summary>A registry with nothing in it highlights nothing, and says so rather than failing.</summary>
    [Fact]
    public void FindSpans_finds_nothing_when_nothing_is_registered() =>
        new CommandHighlights(new CommandRegistry()).FindSpans("Type /help for the commands.").Should().BeEmpty();

    /// <summary>An empty text has nothing in it to find.</summary>
    [Fact]
    public void FindSpans_finds_nothing_in_an_empty_text() =>
        Chat().FindSpans(string.Empty).Should().BeEmpty();

    /// <summary>A text that is not there is not an error either.</summary>
    [Fact]
    public void FindSpans_finds_nothing_in_a_null_text() =>
        Chat().FindSpans(null).Should().BeEmpty();

    /// <summary>Built over a registry, the highlights find what the registry holds.</summary>
    [Fact]
    public void a_registry_is_what_the_application_builds_it_over()
    {
        //Arrange
        var registry = new CommandRegistry();
        registry.Register(new NamedCommand("status"));
        registry.Register(new NamedCommand("download"));
        var highlights = new CommandHighlights(registry);

        //Act
        var found = Found(highlights, "/status says what is there, /download -y fetches it, /nosuch does nothing.");

        //Assert
        found.Should().Equal("/status", "/download -y");
    }

    /// <summary>The prefix is the slash a chat is typed with unless the caller says otherwise.</summary>
    [Fact]
    public void Prefix_is_the_chat_s_slash_by_default() =>
        Chat().Prefix.Should().Be(ChatLineInterpreter.CommandPrefix);

    /// <summary>A caller that types its commands another way says so, and only that way is found.</summary>
    [Fact]
    public void a_prefix_of_the_caller_s_own_is_what_is_looked_for()
    {
        //Arrange
        var highlights = new CommandHighlights(["help"], ":");

        //Act
        var found = Found(highlights, "Type :help for the commands, not /help.");

        //Assert
        highlights.Prefix.Should().Be(":");
        found.Should().Equal(":help");
    }
}
