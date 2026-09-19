using ModelQueryTool.ChatTerminal.Editing;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>Tests for <see cref="CellWidth"/>.</summary>
public class CellWidthTests
{
    [Theory]
    [InlineData('a')]
    [InlineData(' ')]
    [InlineData('é')]
    [InlineData('中')]
    public void a_printable_code_point_is_one_cell(char c) =>
        CellWidth.OfCodePoint(c).Should().Be(1);

    [Fact]
    public void a_code_point_beyond_the_basic_plane_is_one_cell() =>
        CellWidth.OfCodePoint(0x1F600).Should().Be(1);

    [Theory]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData('\x1b')]
    [InlineData('\x7f')]
    public void a_control_character_is_no_cells(char c) =>
        CellWidth.OfCodePoint(c).Should().Be(0);

    [Fact]
    public void plain_text_measures_its_length() =>
        CellWidth.OfText("chat> ").Should().Be(6);

    [Fact]
    public void a_surrogate_pair_measures_one_cell_not_two() =>
        CellWidth.OfText("a\U0001F600b").Should().Be(3);

    [Fact]
    public void a_colour_sequence_in_a_prompt_measures_nothing() =>
        CellWidth.OfText("\x1b[1;32mchat\x1b[0m> ").Should().Be(6);

    [Fact]
    public void an_osc_sequence_measures_nothing() =>
        CellWidth.OfText("\x1b]0;a title\x07> ").Should().Be(2);

    [Fact]
    public void an_osc_sequence_ended_with_a_string_terminator_measures_nothing() =>
        CellWidth.OfText("\x1b]0;a title\x1b\\> ").Should().Be(2);

    [Fact]
    public void a_two_character_escape_measures_nothing() =>
        CellWidth.OfText("\x1b(B> ").Should().Be(2);

    [Fact]
    public void an_unfinished_escape_sequence_measures_nothing() =>
        CellWidth.OfText("text\x1b[1;").Should().Be(4);

    [Fact]
    public void empty_and_null_text_measure_nothing()
    {
        //Assert
        CellWidth.OfText(string.Empty).Should().Be(0);
        CellWidth.OfText(null).Should().Be(0);
    }
}
