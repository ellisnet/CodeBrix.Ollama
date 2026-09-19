using ModelQueryTool.ChatTerminal.Editing;
using ModelQueryTool.ChatTerminal.Output;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>
/// Tests for <see cref="TerminalText"/>. An escape sequence is only ever right or wrong byte for
/// byte, so every test here spells the bytes it expects.
/// </summary>
public class TerminalTextTests
{
    private const string Dim = "\x1b[2m";
    private const string Red = "\x1b[31m";
    private const string Highlight = "\x1b[1;33m";
    private const string Reset = "\x1b[0m";

    /// <summary>The commands this application has, for the lines that mention one.</summary>
    private static CommandHighlights Commands() =>
        new(["help", "think", "download", "remove", "set-context-size"]);

    /// <summary>Dimmed text is started with the dim attribute on its own.</summary>
    [Fact]
    public void DimOn_is_the_dim_attribute() => TerminalText.DimOn.Should().Be("\x1b[2m");

    /// <summary>Dimmed text is ended by resetting every attribute, not only the dim one.</summary>
    [Fact]
    public void DimOff_resets_every_attribute() => TerminalText.DimOff.Should().Be("\x1b[0m");

    /// <summary>A remark is a dimmed line of its own, reset and ended with CR+LF.</summary>
    [Fact]
    public void Notice_wraps_a_remark_in_its_own_dimmed_line() =>
        TerminalText.Notice("The model is loading.").Should().Be("\x1b[2mThe model is loading.\x1b[0m\r\n");

    /// <summary>A remark that is not there produces no bytes, rather than a blank dimmed row.</summary>
    [Fact]
    public void Notice_writes_nothing_when_there_is_no_remark() =>
        TerminalText.Notice(string.Empty).Should().Be(string.Empty);

    /// <summary>A null remark produces no bytes either.</summary>
    [Fact]
    public void Notice_writes_nothing_when_the_remark_is_null() =>
        TerminalText.Notice(null).Should().Be(string.Empty);

    /// <summary>A failure is a red line of its own, reset and ended with CR+LF.</summary>
    [Fact]
    public void Error_wraps_a_failure_in_its_own_red_line() =>
        TerminalText.Error("The model would not load.").Should().Be("\x1b[31mThe model would not load.\x1b[0m\r\n");

    /// <summary>A failure that is not there produces no bytes.</summary>
    [Fact]
    public void Error_writes_nothing_when_there_is_no_failure() =>
        TerminalText.Error(string.Empty).Should().Be(string.Empty);

    /// <summary>A null failure produces no bytes either.</summary>
    [Fact]
    public void Error_writes_nothing_when_the_failure_is_null() =>
        TerminalText.Error(null).Should().Be(string.Empty);

    /// <summary>The prompt colours its label and leaves the bracket and the space plain.</summary>
    [Fact]
    public void Prompt_colours_the_label_and_leaves_the_bracket_plain() =>
        TerminalText.Prompt("chat").Should().Be("\x1b[1;36mchat\x1b[0m> ");

    /// <summary>A prompt with no label is the bracket and the space alone.</summary>
    [Fact]
    public void Prompt_without_a_label_is_the_bracket_alone() =>
        TerminalText.Prompt(" ").Should().Be("> ");

    /// <summary>A null label is the bracket and the space alone as well.</summary>
    [Fact]
    public void Prompt_with_a_null_label_is_the_bracket_alone() =>
        TerminalText.Prompt(null).Should().Be("> ");

    /// <summary>The prompt's colour costs no cells, so the editor lays the line out from the text's width.</summary>
    [Fact]
    public void Prompt_measures_as_the_cells_a_reader_sees() =>
        CellWidth.OfText(TerminalText.Prompt("chat")).Should().Be(6);

    /// <summary>Clearing the screen erases it and homes the cursor.</summary>
    [Fact]
    public void ClearScreen_erases_everything_and_homes_the_cursor() =>
        TerminalText.ClearScreen().Should().Be("\x1b[2J\x1b[H");

    /// <summary>
    /// A dimmed line that names a command: the dim stops, the command is written in the highlight,
    /// and the dim goes back on for the rest of the sentence.
    /// </summary>
    [Fact]
    public void Notice_writes_the_command_it_names_in_the_highlight() =>
        TerminalText.Notice("Thinking is on; /think off turns it off.", Commands()).Should().Be(
            Dim
            + "Thinking is on; "
            + Reset + Highlight + "/think off" + Reset + Dim
            + " turns it off."
            + Reset
            + "\r\n");

    /// <summary>Two commands in one line are two highlights, and the dim comes back after each.</summary>
    [Fact]
    public void Notice_writes_every_command_it_names_in_the_highlight() =>
        TerminalText.Notice("Type /download -y to repair it, or /remove -y first.", Commands()).Should().Be(
            Dim
            + "Type "
            + Reset + Highlight + "/download -y" + Reset + Dim
            + " to repair it, or "
            + Reset + Highlight + "/remove -y" + Reset + Dim
            + " first."
            + Reset
            + "\r\n");

    /// <summary>A line that ends on a command ends with one reset, and no style nobody uses.</summary>
    [Fact]
    public void Notice_ending_on_a_command_ends_with_one_reset() =>
        TerminalText.Notice("Everything is listed by /help", Commands()).Should().Be(
            Dim
            + "Everything is listed by "
            + Reset + Highlight + "/help" + Reset
            + "\r\n");

    /// <summary>A red line puts the red back on after the command, so the rest of it is still red.</summary>
    [Fact]
    public void Error_writes_the_command_it_names_in_the_highlight() =>
        TerminalText.Error("'lots' is not a number. Usage: /set-context-size -y <tokens>", Commands()).Should().Be(
            Red
            + "'lots' is not a number. Usage: "
            + Reset + Highlight + "/set-context-size -y" + Reset + Red
            + " <tokens>"
            + Reset
            + "\r\n");

    /// <summary>A line in no style of its own carries the highlight and nothing else.</summary>
    [Fact]
    public void Plain_writes_the_command_it_names_in_the_highlight() =>
        TerminalText.Plain("  /help    Lists commands.", Commands()).Should().Be(
            "  " + Reset + Highlight + "/help" + Reset + "    Lists commands.");

    /// <summary>A plain line that names nothing is handed back exactly as it came.</summary>
    [Fact]
    public void Plain_leaves_a_line_that_names_no_command_alone() =>
        TerminalText.Plain("  state:      Ready", Commands()).Should().Be("  state:      Ready");

    /// <summary>A plain line with no highlights at all is handed back exactly as it came too.</summary>
    [Fact]
    public void Plain_without_highlights_leaves_the_line_alone() =>
        TerminalText.Plain("Type /help for the commands.", null).Should().Be("Type /help for the commands.");

    /// <summary>A plain line that is not there produces no bytes.</summary>
    [Fact]
    public void Plain_writes_nothing_when_there_is_no_line() =>
        TerminalText.Plain(null, Commands()).Should().Be(string.Empty);

    /// <summary>A command written by hand is the highlight, the text, and the reset that ends it.</summary>
    [Fact]
    public void Command_is_the_highlight_colour_and_nothing_else() =>
        TerminalText.Command("/think off").Should().Be(Highlight + "/think off" + Reset);

    /// <summary>A command that is not there produces no bytes.</summary>
    [Fact]
    public void Command_writes_nothing_when_there_is_nothing_to_show() =>
        TerminalText.Command(string.Empty).Should().Be(string.Empty);

    /// <summary>With no highlights, a dimmed remark is exactly what it has always been.</summary>
    [Fact]
    public void Dimmed_without_highlights_is_the_bytes_it_always_was() =>
        TerminalText.Dimmed("Thinking is on; /think off turns it off.", null)
            .Should().Be(TerminalText.Dimmed("Thinking is on; /think off turns it off."));

    /// <summary>With no highlights, a dimmed line is exactly what it has always been.</summary>
    [Fact]
    public void Notice_without_highlights_is_the_bytes_it_always_was() =>
        TerminalText.Notice("Thinking is on; /think off turns it off.", null)
            .Should().Be("\x1b[2mThinking is on; /think off turns it off.\x1b[0m\r\n");

    /// <summary>With no highlights, a failure is exactly what it has always been.</summary>
    [Fact]
    public void Failure_without_highlights_is_the_bytes_it_always_was() =>
        TerminalText.Failure("The model would not load.", null)
            .Should().Be(TerminalText.Failure("The model would not load."));

    /// <summary>With no highlights, a red line is exactly what it has always been.</summary>
    [Fact]
    public void Error_without_highlights_is_the_bytes_it_always_was() =>
        TerminalText.Error("Type /help for the commands.", null)
            .Should().Be("\x1b[31mType /help for the commands.\x1b[0m\r\n");

    /// <summary>Highlights that find nothing in the line leave the old bytes alone as well.</summary>
    [Fact]
    public void highlights_that_find_nothing_leave_the_old_bytes_alone() =>
        TerminalText.Notice("The model is loading.", Commands())
            .Should().Be("\x1b[2mThe model is loading.\x1b[0m\r\n");

    /// <summary>A remark that is not there produces no bytes, highlights or no highlights.</summary>
    [Fact]
    public void Notice_with_highlights_writes_nothing_when_there_is_no_remark() =>
        TerminalText.Notice(string.Empty, Commands()).Should().Be(string.Empty);

    /// <summary>A failure that is not there produces no bytes either.</summary>
    [Fact]
    public void Error_with_highlights_writes_nothing_when_there_is_no_failure() =>
        TerminalText.Error(null, Commands()).Should().Be(string.Empty);
}
