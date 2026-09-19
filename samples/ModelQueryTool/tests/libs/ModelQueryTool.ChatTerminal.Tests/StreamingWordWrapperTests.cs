using System;
using System.Text;
using ModelQueryTool.ChatTerminal.Output;
using ModelQueryTool.ChatTerminal.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>Tests for <see cref="StreamingWordWrapper"/>.</summary>
public class StreamingWordWrapperTests
{
    /// <summary>Writes a text in one call and returns everything the wrapper emitted.</summary>
    private static string WriteWhole(string text, int columns, int startColumn = 0)
    {
        var wrapper = new StreamingWordWrapper(columns, startColumn);
        return wrapper.Write(text) + wrapper.Flush();
    }

    /// <summary>Writes a text one character at a time and returns everything the wrapper emitted.</summary>
    private static string WriteByCharacter(string text, int columns, int startColumn = 0)
    {
        var wrapper = new StreamingWordWrapper(columns, startColumn);
        var builder = new StringBuilder();
        foreach (var c in text) { builder.Append(wrapper.Write(c.ToString())); }

        return builder.Append(wrapper.Flush()).ToString();
    }

    [Fact]
    public void text_that_fits_comes_out_unchanged() =>
        WriteWhole("a short answer", 40).Should().Be("a short answer");

    [Fact]
    public void a_long_line_breaks_between_words()
    {
        //Act
        var result = WriteWhole("the quick brown fox jumps over the lazy dog", 20);

        //Assert - no word is cut; the space that ended the row stays on it, where
        //  nobody can see it, and the break comes after it
        result.Should().Be("the quick brown fox \r\njumps over the lazy \r\ndog");
    }

    [Fact]
    public void a_word_longer_than_a_row_is_broken_hard()
    {
        //Act
        var result = WriteWhole("short supercalifragilisticexpialidocious end", 20);

        //Assert - the word moves to a row of its own first, then fills it
        result.Should().Be("short \r\nsupercalifragilistic\r\nexpialidocious end");
    }

    [Fact]
    public void a_word_longer_than_a_row_starting_at_column_zero_fills_the_row()
    {
        //Act
        var result = WriteWhole("aaaaaaaaaaaaaaaaaaaaaaaaa", 10);

        //Assert
        result.Should().Be("aaaaaaaaaa\r\naaaaaaaaaa\r\naaaaa");
    }

    [Fact]
    public void the_model_s_own_line_breaks_are_kept_as_crlf()
    {
        //Act
        var result = WriteWhole("one\ntwo\r\nthree", 40);

        //Assert
        result.Should().Be("one\r\ntwo\r\nthree");
    }

    [Fact]
    public void a_blank_line_between_paragraphs_survives()
    {
        //Act
        var result = WriteWhole("first\n\nsecond", 40);

        //Assert
        result.Should().Be("first\r\n\r\nsecond");
    }

    [Fact]
    public void runs_of_spaces_inside_a_row_are_preserved()
    {
        //Act - the shape of a list or an indented code block depends on this
        var result = WriteWhole("  - item     spaced", 40);

        //Assert
        result.Should().Be("  - item     spaced");
    }

    [Fact]
    public void markdown_characters_are_text_like_any_other()
    {
        //Act
        var result = WriteWhole("**bold** `code` # heading", 40);

        //Assert
        result.Should().Be("**bold** `code` # heading");
    }

    [Fact]
    public void a_reply_that_starts_after_a_prefix_wraps_from_that_column()
    {
        //Act - "answer: " has already been written on the row
        var result = WriteWhole("one two three four", 20, startColumn: 8);

        //Assert
        result.Should().Be("one two \r\nthree four");
    }

    [Fact]
    public void reset_moves_the_start_column_and_forgets_what_was_held()
    {
        //Arrange
        var wrapper = new StreamingWordWrapper(20);
        wrapper.Write("held");

        //Act
        wrapper.Reset(5);

        //Assert
        wrapper.HasPendingText.Should().BeFalse();
        wrapper.Column.Should().Be(5);
        wrapper.Flush().Should().Be("");
    }

    [Fact]
    public void the_last_word_is_held_until_flush()
    {
        //Arrange
        var wrapper = new StreamingWordWrapper(40);

        //Act
        var written = wrapper.Write("two words");

        //Assert
        written.Should().Be("two ");
        wrapper.HasPendingText.Should().BeTrue();
        wrapper.Flush().Should().Be("words");
    }

    [Fact]
    public void a_carriage_return_at_the_end_of_a_delta_is_held_and_never_doubled()
    {
        //Arrange
        var wrapper = new StreamingWordWrapper(40);

        //Act - the model's "\r\n" arrives split across two deltas
        var first = wrapper.Write("line\r");
        var second = wrapper.Write("\nnext");

        //Assert - "line" is still the current word when the CR arrives, so both
        //  are held until the LF settles what the CR meant
        first.Should().Be("");
        second.Should().Be("line\r\n");
        wrapper.Flush().Should().Be("next");
    }

    [Fact]
    public void a_lone_carriage_return_is_a_line_break()
    {
        //Act
        var result = WriteWhole("line\rnext", 40);

        //Assert
        result.Should().Be("line\r\nnext");
    }

    [Fact]
    public void a_tab_is_expanded_to_the_next_stop()
    {
        //Act
        var result = WriteWhole("ab\tcd", 40);

        //Assert - column 2 to column 8
        result.Should().Be("ab      cd");
    }

    [Fact]
    public void a_surrogate_pair_is_never_split()
    {
        //Arrange - ten cells wide, so the emoji lands right at the edge
        var text = "aaaaaaaaa\U0001F600\U0001F600";

        //Act
        var result = WriteWhole(text, 10);

        //Assert
        result.Should().Be("aaaaaaaaa\U0001F600\r\n\U0001F600");
    }

    [Fact]
    public void a_wider_terminal_applies_to_the_text_still_to_come()
    {
        //Arrange
        var wrapper = new StreamingWordWrapper(10);
        var first = wrapper.Write("aaaa bbbbbb ");

        //Act
        wrapper.Columns = 30;
        var second = wrapper.Write("cccc dddd eeee ffff") + wrapper.Flush();

        //Assert - the narrow width broke the first part, the wide one does not
        //  break the second, and nothing already written is laid out again
        first.Should().Be("aaaa \r\nbbbbbb ");
        second.Should().Be("cccc dddd eeee ffff");
    }

    [Theory]
    [InlineData("the quick brown fox jumps over the lazy dog", 20)]
    [InlineData("a paragraph with\nembedded breaks and    runs of spaces", 17)]
    [InlineData("supercalifragilisticexpialidocious and short ones", 12)]
    [InlineData("```\ncode block\n  indented line\n```", 24)]
    [InlineData("emoji \U0001F600 and 中文 mixed into the text", 15)]
    [InlineData("trailing space at the end ", 10)]
    public void the_output_is_the_same_however_the_text_is_chopped_up(string text, int columns)
    {
        //Act
        var whole = WriteWhole(text, columns);
        var oneAtATime = WriteByCharacter(text, columns);

        //Assert
        oneAtATime.Should().Be(whole);
    }

    [Theory]
    [InlineData("the quick brown fox jumps over the lazy dog", 20)]
    [InlineData("a paragraph with\nembedded breaks and    runs of spaces", 17)]
    [InlineData("supercalifragilisticexpialidocious and short ones", 12)]
    public void the_output_is_the_same_in_random_sized_deltas(string text, int columns)
    {
        //Arrange
        var random = new Random(7);
        var wrapper = new StreamingWordWrapper(columns);
        var builder = new StringBuilder();

        //Act
        var index = 0;
        while (index < text.Length)
        {
            var length = Math.Min(random.Next(1, 6), text.Length - index);
            builder.Append(wrapper.Write(text.Substring(index, length)));
            index += length;
        }

        builder.Append(wrapper.Flush());

        //Assert
        builder.ToString().Should().Be(WriteWhole(text, columns));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(40)]
    public void what_it_writes_lands_on_the_screen_where_it_says(int columns)
    {
        //Arrange
        var terminal = new HeadlessTerminal(columns, 24);
        var wrapper = new StreamingWordWrapper(columns);
        var text = "The wrapper is the only thing that decides where a row ends, so the "
            + "terminal's own wrap never fires and every row it writes is one row.\n"
            + "  an indented line\nand a verylongwordthatcannotfitonanyrowatall after it";

        //Act - a delta at a time, as the model produces them
        foreach (var c in text) { terminal.Feed(wrapper.Write(c.ToString())); }

        terminal.Feed(wrapper.Flush());

        //Assert - every row is within the grid, and no word was cut in half by
        //  the terminal rather than by the wrapper
        var screen = terminal.Screen();
        foreach (var row in screen) { row.Length.Should().BeLessThanOrEqualTo(columns); }

        terminal.CursorColumn.Should().Be(wrapper.Column);
        string.Concat(screen).Replace(" ", string.Empty)
            .Should().Contain("verylongwordthatcannotfitonanyrowatall".Substring(0, 10));
    }

    [Fact]
    public void the_column_count_must_be_positive()
    {
        //Act
        Action act = () => new StreamingWordWrapper(0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_start_column_must_not_be_negative()
    {
        //Act
        Action act = () => new StreamingWordWrapper(10, -1);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
