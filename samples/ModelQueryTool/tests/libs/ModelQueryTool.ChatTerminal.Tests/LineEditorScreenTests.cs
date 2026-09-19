using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.ChatTerminal.Editing;
using ModelQueryTool.ChatTerminal.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>
/// Drives <see cref="LineEditor"/> against the real terminal engine - the one
/// the application's control renders - and asserts what is ON THE SCREEN and
/// where the CURSOR sits. Byte assertions can only say that the escape codes
/// have not changed; these say that they are right.
/// </summary>
public class LineEditorScreenTests
{
    /// <summary>The widths every scripted case is run at, narrow ones included.</summary>
    public static TheoryData<int> Widths => new(20, 24, 40, 80);

    [Theory]
    [MemberData(nameof(Widths))]
    public void typing_a_line_longer_than_a_row_reads_correctly(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");

        //Act
        harness.Type("the quick brown fox jumps over the lazy dog and keeps going");

        //Assert
        harness.AssertScreenMatchesModel();
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void a_line_that_lands_exactly_on_the_last_column_keeps_the_cursor_where_it_says(int columns)
    {
        //Arrange - the prompt plus the text fill the first row to the last cell
        var harness = new EditorHarness(columns, "chat> ");
        var text = new string('x', columns - 6);

        //Act
        harness.Type(text);

        //Assert - deferred wrap would leave the cursor a row up from where the model says
        harness.AssertScreenMatchesModel();
        harness.Editor.CursorRow.Should().Be(1);
        harness.Editor.CursorColumn.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void inserting_in_the_middle_reflows_every_row_below(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");
        harness.Type("alpha beta gamma delta epsilon zeta eta theta");
        for (var i = 0; i < 20; i++) { harness.Do(e => e.MoveLeft()); }

        //Act
        harness.Type("INSERTED ");

        //Assert
        harness.AssertScreenMatchesModel();
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void backspacing_through_a_row_boundary_leaves_no_stale_cells(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");
        harness.Type(new string('a', columns + 5));

        //Act
        for (var i = 0; i < 8; i++) { harness.Do(e => e.Backspace()); }

        //Assert
        harness.AssertScreenMatchesModel();
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void deleting_in_the_middle_of_a_wrapped_line_pulls_the_tail_up(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");
        harness.Type("one two three four five six seven eight nine ten");
        harness.Do(e => e.MoveHome());
        for (var i = 0; i < 4; i++) { harness.Do(e => e.MoveRight()); }

        //Act
        for (var i = 0; i < 6; i++) { harness.Do(e => e.Delete()); }

        //Assert
        harness.AssertScreenMatchesModel();
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void a_pasted_block_shows_one_row_per_line(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");

        //Act
        harness.Do(e => e.Insert("first line\rsecond line\r\nthird line"));

        //Assert
        harness.AssertScreenMatchesModel();
        harness.Terminal.Row(0).Should().Be("chat> first line");
        harness.Terminal.Row(1).Should().Be("second line");
        harness.Terminal.Row(2).Should().Be("third line");
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void home_and_end_cross_rows_and_line_breaks(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");
        harness.Do(e => e.Insert("a line that is quite long\nand a second one after it"));

        //Act
        harness.Do(e => e.MoveHome());

        //Assert
        harness.AssertScreenMatchesModel();

        //Act
        harness.Do(e => e.MoveEnd());

        //Assert
        harness.AssertScreenMatchesModel();
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void left_and_right_walk_the_whole_line_one_character_at_a_time(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");
        var text = "wrapping\nacross rows and breaks alike, several times over";
        harness.Do(e => e.Insert(text));

        //Act and Assert - every stop on the way back
        for (var i = 0; i < text.Length; i++)
        {
            harness.Do(e => e.MoveLeft());
            harness.AssertScreenMatchesModel();
        }

        //Act and Assert - and every stop on the way out again
        for (var i = 0; i < text.Length; i++)
        {
            harness.Do(e => e.MoveRight());
            harness.AssertScreenMatchesModel();
        }
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void replacing_a_tall_line_with_a_short_one_clears_the_rows_below(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");
        harness.Type(new string('z', columns * 3));
        harness.Terminal.Screen().Count.Should().BeGreaterThan(2);

        //Act
        harness.Do(e => e.ReplaceWith("short"));

        //Assert
        harness.AssertScreenMatchesModel();
        harness.Terminal.Screen().Should().Equal(new[] { "chat> short" });
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void replacing_a_short_line_with_a_tall_one_draws_all_of_it(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");
        harness.Type("short");

        //Act
        harness.Do(e => e.ReplaceWith(new string('q', columns * 2 + 3)));

        //Assert
        harness.AssertScreenMatchesModel();
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void a_redraw_after_a_clear_screen_puts_everything_back(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");
        harness.Type("a line long enough to wrap onto a second row, and then some");
        for (var i = 0; i < 12; i++) { harness.Do(e => e.MoveLeft()); }

        //Act
        harness.ClearScreenAndRedraw();

        //Assert
        harness.AssertScreenMatchesModel();
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void a_resize_mid_line_relays_it_at_the_new_width(int columns)
    {
        //Arrange
        var harness = new EditorHarness(columns, "chat> ");
        harness.Type("a line that has to be laid out again when the window changes size");
        for (var i = 0; i < 10; i++) { harness.Do(e => e.MoveLeft()); }

        //Act
        harness.Resize(columns == 20 ? 60 : 20);

        //Assert
        harness.AssertScreenMatchesModel();
    }

    [Fact]
    public void a_prompt_that_fills_the_row_exactly_still_starts_the_text_below_it()
    {
        //Arrange - a twenty-cell prompt on a twenty-column terminal
        var harness = new EditorHarness(20, new string('p', 20));

        //Act
        harness.Type("text");

        //Assert
        harness.AssertScreenMatchesModel();
        harness.Terminal.Row(0).Should().Be(new string('p', 20));
        harness.Terminal.Row(1).Should().Be("text");
    }

    [Fact]
    public void a_prompt_longer_than_a_row_is_counted_in_full()
    {
        //Arrange
        var harness = new EditorHarness(20, new string('p', 26));

        //Act
        harness.Type("hello");

        //Assert
        harness.AssertScreenMatchesModel();
        harness.Terminal.Row(1).Should().Be("pppppphello");
    }

    [Fact]
    public void ideographs_are_laid_out_the_way_the_grid_counts_them()
    {
        //Arrange
        var harness = new EditorHarness(20, "chat> ");

        //Act - enough ideographs to run past the right edge
        harness.Type("中文文字測試中文文字測試中文文字測試");

        //Assert
        harness.AssertScreenMatchesModel();
    }

    [Fact]
    public void an_emoji_is_never_split_across_rows()
    {
        //Arrange - ten cells of text fit on the first row beside the prompt
        var harness = new EditorHarness(12, "> ");

        //Act - each emoji is a surrogate pair: two units of the string, one cell
        for (var i = 0; i < 14; i++) { harness.Do(e => e.Insert("\U0001F600")); }

        //Assert - the screen holds whole emoji, and the second row starts with one
        harness.AssertScreenMatchesModel();
        harness.Terminal.Row(0).Should().Be("> " + string.Concat(Enumerable.Repeat("\U0001F600", 10)));
        harness.Terminal.Row(1).Should().Be(string.Concat(Enumerable.Repeat("\U0001F600", 4)));
    }

    [Fact]
    public void a_scripted_editing_session_keeps_the_screen_and_the_cursor_together()
    {
        //Arrange
        var harness = new EditorHarness(24, "chat> ");

        //Act and Assert - a plausible sequence, checked at every step
        harness.Type("explain what a terminal ");
        harness.AssertScreenMatchesModel();

        harness.Do(e => e.Insert("emulator does"));
        harness.AssertScreenMatchesModel();

        harness.Do(e => e.MoveHome());
        harness.AssertScreenMatchesModel();

        harness.Type("please ");
        harness.AssertScreenMatchesModel();

        harness.Do(e => e.MoveEnd());
        harness.Do(e => e.InsertLineBreak());
        harness.Type("in two sentences");
        harness.AssertScreenMatchesModel();

        for (var i = 0; i < 30; i++) { harness.Do(e => e.Backspace()); }
        harness.AssertScreenMatchesModel();

        harness.Do(e => e.ReplaceWith("a recalled line from the history"));
        harness.AssertScreenMatchesModel();

        harness.ClearScreenAndRedraw();
        harness.AssertScreenMatchesModel();
    }

    [Theory]
    [InlineData(20, 1)]
    [InlineData(20, 2)]
    [InlineData(24, 3)]
    [InlineData(37, 4)]
    [InlineData(80, 5)]
    public void a_random_sequence_of_operations_never_loses_the_screen(int columns, int seed)
    {
        //Arrange
        var random = new Random(seed);
        var harness = new EditorHarness(columns, "chat> ");
        var alphabet = "abcdefghijklmnopqrstuvwxyz ".ToCharArray();
        var log = new StringBuilder();

        //Act and Assert
        for (var step = 0; step < 400; step++)
        {
            var choice = random.Next(100);
            if (choice < 40)
            {
                var c = alphabet[random.Next(alphabet.Length)];
                log.Append("insert '").Append(c).Append("'; ");
                harness.Do(e => e.Insert(c));
            }
            else if (choice < 46)
            {
                log.Append("break; ");
                harness.Do(e => e.InsertLineBreak());
            }
            else if (choice < 60)
            {
                log.Append("backspace; ");
                harness.Do(e => e.Backspace());
            }
            else if (choice < 68)
            {
                log.Append("delete; ");
                harness.Do(e => e.Delete());
            }
            else if (choice < 80)
            {
                log.Append("left; ");
                harness.Do(e => e.MoveLeft());
            }
            else if (choice < 90)
            {
                log.Append("right; ");
                harness.Do(e => e.MoveRight());
            }
            else if (choice < 94)
            {
                log.Append("home; ");
                harness.Do(e => e.MoveHome());
            }
            else if (choice < 98)
            {
                log.Append("end; ");
                harness.Do(e => e.MoveEnd());
            }
            else
            {
                var replacement = new string(alphabet[random.Next(alphabet.Length)], random.Next(1, 40));
                log.Append("replace(").Append(replacement.Length).Append("); ");
                harness.Do(e => e.ReplaceWith(replacement));
            }

            var (rows, cursorRow, cursorColumn) = ScreenModel.LayOut(
                "chat> ", harness.Editor.Text, columns, harness.Editor.CursorPosition);

            harness.Terminal.Screen().Should().Equal(rows, "after: " + log);
            harness.Terminal.CursorRow.Should().Be(cursorRow, "after: " + log);
            harness.Terminal.CursorColumn.Should().Be(cursorColumn, "after: " + log);
            log.Clear();
        }
    }

    [Fact]
    public async Task the_whole_session_draws_the_prompt_and_the_line_it_is_given()
    {
        //Arrange - the session and the terminal, wired as the application wires them
        var terminal = new HeadlessTerminal(24, 10);
        var asked = new List<string>();
        var interpreter = new ChatLineInterpreter(new CommandRegistry(),
            (_, line, _) => { asked.Add(line); return Task.CompletedTask; }, "chat> ");
        var session = new ShellSession(interpreter);
        session.OutputProduced += terminal.Feed;
        session.SetGridSize(24, 10);
        session.Start();

        //Act - a paste of three lines, then Enter
        session.SendInput("first\rsecond\rthird");
        session.SendInput("\r");
        await session.ExecutionChain;

        //Assert - the three lines were one question, and the next prompt is below them
        asked.Should().Equal("first\nsecond\nthird");
        var screen = new List<string>(terminal.Screen());
        screen.Should().Equal(new[] { "chat> first", "second", "third", "chat> " });
    }
}
