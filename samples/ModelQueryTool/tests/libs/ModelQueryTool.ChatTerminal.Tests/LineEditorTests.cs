using ModelQueryTool.ChatTerminal.Editing;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>
/// Byte-for-byte tests of <see cref="LineEditor"/>: what every operation writes
/// to the terminal, and where it leaves the logical cursor. That the bytes put
/// the right thing on a real screen is <see cref="LineEditorScreenTests"/>'s
/// job.
/// </summary>
public class LineEditorTests
{
    [Fact]
    public void insert_at_end_echoes_just_the_character()
    {
        //Arrange
        var editor = new LineEditor();

        //Act
        var echo = editor.Insert('a');

        //Assert - nothing was drawn past the cursor, so nothing is erased first
        echo.Should().Be("a");
        editor.Text.Should().Be("a");
        editor.CursorPosition.Should().Be(1);
    }

    [Fact]
    public void insert_mid_line_erases_the_tail_rewrites_it_and_comes_back()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("abc");
        editor.MoveLeft();
        editor.MoveLeft();

        //Act
        var echo = editor.Insert('X');

        //Assert
        editor.Text.Should().Be("aXbc");
        editor.CursorPosition.Should().Be(2);
        echo.Should().Be("\x1b[JXbc\r\x1b[2C");
    }

    [Fact]
    public void backspace_at_end_erases_the_last_character()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("ab");

        //Act
        var echo = editor.Backspace();

        //Assert
        editor.Text.Should().Be("a");
        echo.Should().Be("\r\x1b[1C\x1b[J");
    }

    [Fact]
    public void backspace_at_start_is_a_no_op()
    {
        //Arrange
        var editor = new LineEditor();

        //Act
        var echo = editor.Backspace();

        //Assert
        echo.Should().Be("");
        editor.Text.Should().Be("");
    }

    [Fact]
    public void delete_removes_the_character_under_the_cursor()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("abc");
        editor.MoveHome();

        //Act
        var echo = editor.Delete();

        //Assert
        editor.Text.Should().Be("bc");
        editor.CursorPosition.Should().Be(0);
        echo.Should().Be("\x1b[Jbc\r");
    }

    [Fact]
    public void home_and_end_move_across_the_whole_line()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("hello");

        //Act
        var homeEcho = editor.MoveHome();
        var endEcho = editor.MoveEnd();

        //Assert
        homeEcho.Should().Be("\r");
        endEcho.Should().Be("\r\x1b[5C");
        editor.CursorPosition.Should().Be(5);
    }

    [Fact]
    public void move_right_at_end_is_a_no_op()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("a");

        //Act
        var echo = editor.MoveRight();

        //Assert
        echo.Should().Be("");
        editor.CursorPosition.Should().Be(1);
    }

    [Fact]
    public void replace_with_erases_the_line_and_writes_the_new_text()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("old line");

        //Act
        var echo = editor.ReplaceWith("new");

        //Assert
        editor.Text.Should().Be("new");
        editor.CursorPosition.Should().Be(3);
        echo.Should().Be("\r\x1b[Jnew");
    }

    [Fact]
    public void take_line_returns_the_text_and_resets_the_editor()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("set-system-prompt -y");

        //Act
        var line = editor.TakeLine();

        //Assert
        line.Should().Be("set-system-prompt -y");
        editor.Text.Should().Be("");
        editor.CursorPosition.Should().Be(0);
    }

    [Fact]
    public void redraw_reemits_text_and_restores_the_cursor_column()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("abcd");
        editor.MoveLeft();

        //Act
        var echo = editor.Redraw();

        //Assert
        echo.Should().Be("abcd\r\x1b[3C");
        editor.Text.Should().Be("abcd");
        editor.CursorPosition.Should().Be(3);
    }

    [Fact]
    public void redraw_starts_after_the_prompt()
    {
        //Arrange
        var editor = new LineEditor { Columns = 20, PromptWidth = 6 };
        editor.Insert("hi");

        //Act
        var echo = editor.Redraw();

        //Assert - the prompt has just been written, so the text goes down as it is
        echo.Should().Be("hi");
        editor.CursorRow.Should().Be(0);
        editor.CursorColumn.Should().Be(8);
    }

    [Fact]
    public void paste_insert_mid_line_keeps_the_tail()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("ad");
        editor.MoveLeft();

        //Act
        var echo = editor.Insert("bc");

        //Assert
        editor.Text.Should().Be("abcd");
        editor.CursorPosition.Should().Be(3);
        echo.Should().Be("\x1b[Jbcd\r\x1b[3C");
    }

    [Fact]
    public void the_default_grid_is_eighty_columns_with_no_prompt()
    {
        //Arrange
        var editor = new LineEditor();

        //Assert
        editor.Columns.Should().Be(80);
        editor.PromptWidth.Should().Be(0);
        editor.RowCount.Should().Be(1);
    }

    [Fact]
    public void a_line_that_passes_the_right_edge_starts_a_new_row_itself()
    {
        //Arrange
        var editor = new LineEditor { Columns = 10 };

        //Act - ten characters fill the row exactly, the eleventh starts the next
        var echo = editor.Insert("0123456789A");

        //Assert - the editor breaks the row, so the terminal's own wrap never fires
        echo.Should().Be("0123456789\r\nA");
        editor.RowCount.Should().Be(2);
        editor.CursorRow.Should().Be(1);
        editor.CursorColumn.Should().Be(1);
    }

    [Fact]
    public void a_prompt_pushes_the_text_along_the_first_row()
    {
        //Arrange
        var editor = new LineEditor { Columns = 10, PromptWidth = 4 };

        //Act
        var echo = editor.Insert("0123456789");

        //Assert - six characters fit beside the prompt, the rest go below
        echo.Should().Be("012345\r\n6789");
        editor.RowCount.Should().Be(2);
        editor.CursorRow.Should().Be(1);
        editor.CursorColumn.Should().Be(4);
    }

    [Fact]
    public void left_at_the_start_of_a_row_lands_at_the_end_of_the_one_above()
    {
        //Arrange
        var editor = new LineEditor { Columns = 10 };
        editor.Insert("0123456789A");

        //Act - the first press lands before the "A", the second crosses the boundary
        editor.MoveLeft();
        var echo = editor.MoveLeft();

        //Assert
        echo.Should().Be("\x1b[1A\r\x1b[9C");
        editor.CursorRow.Should().Be(0);
        editor.CursorColumn.Should().Be(9);
    }

    [Fact]
    public void right_at_the_end_of_a_row_lands_at_the_start_of_the_one_below()
    {
        //Arrange
        var editor = new LineEditor { Columns = 10 };
        editor.Insert("0123456789A");
        editor.MoveHome();
        for (var i = 0; i < 9; i++) { editor.MoveRight(); }

        //Act - the tenth character ends the row
        var echo = editor.MoveRight();

        //Assert
        echo.Should().Be("\x1b[1B\r");
        editor.CursorRow.Should().Be(1);
        editor.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void home_from_the_second_row_goes_up_to_the_prompt()
    {
        //Arrange
        var editor = new LineEditor { Columns = 10, PromptWidth = 3 };
        editor.Insert("0123456789A");

        //Act
        var echo = editor.MoveHome();

        //Assert
        echo.Should().Be("\x1b[1A\r\x1b[3C");
        editor.CursorRow.Should().Be(0);
        editor.CursorColumn.Should().Be(3);
    }

    [Fact]
    public void a_line_break_inside_the_text_starts_a_row_at_column_zero()
    {
        //Arrange
        var editor = new LineEditor { Columns = 20, PromptWidth = 4 };

        //Act
        var echo = editor.Insert("one\ntwo");

        //Assert
        echo.Should().Be("one\r\ntwo");
        editor.RowCount.Should().Be(2);
        editor.CursorRow.Should().Be(1);
        editor.CursorColumn.Should().Be(3);
    }

    [Fact]
    public void carriage_returns_in_pasted_text_become_line_breaks()
    {
        //Arrange
        var editor = new LineEditor { Columns = 20 };

        //Act
        editor.Insert("one\r\ntwo\rthree\n");

        //Assert
        editor.Text.Should().Be("one\ntwo\nthree\n");
        editor.RowCount.Should().Be(4);
    }

    [Fact]
    public void inserting_a_line_break_in_the_middle_erases_what_the_tail_left_behind()
    {
        //Arrange
        var editor = new LineEditor { Columns = 20 };
        editor.Insert("abcdef");
        editor.MoveHome();
        for (var i = 0; i < 3; i++) { editor.MoveRight(); }

        //Act
        var echo = editor.InsertLineBreak();

        //Assert - the ED is what stops "def" staying on the first row
        echo.Should().Be("\x1b[J\r\ndef\r");
        editor.Text.Should().Be("abc\ndef");
        editor.CursorRow.Should().Be(1);
        editor.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void replacing_a_three_row_line_with_a_short_one_leaves_no_rows_behind()
    {
        //Arrange
        var editor = new LineEditor { Columns = 10 };
        editor.Insert("0123456789abcdefghijZ");
        editor.RowCount.Should().Be(3);

        //Act
        var echo = editor.ReplaceWith("short");

        //Assert
        echo.Should().Be("\x1b[2A\r\x1b[Jshort");
        editor.Text.Should().Be("short");
        editor.RowCount.Should().Be(1);
        editor.CursorRow.Should().Be(0);
        editor.CursorColumn.Should().Be(5);
    }

    [Fact]
    public void begin_repaint_winds_back_to_the_prompt_row_and_clears_below()
    {
        //Arrange
        var editor = new LineEditor { Columns = 10, PromptWidth = 2 };
        editor.Insert("0123456789abcd");

        //Act
        var echo = editor.BeginRepaint();

        //Assert
        echo.Should().Be("\x1b[1A\r\x1b[J");
    }

    [Fact]
    public void an_ideograph_takes_one_cell_like_the_grid_gives_it()
    {
        //Arrange
        var editor = new LineEditor { Columns = 10 };

        //Act - ten code points fill the row, whatever they are
        var echo = editor.Insert("012345678中");

        //Assert
        echo.Should().Be("012345678中\r\n");
        editor.CursorRow.Should().Be(1);
        editor.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void a_surrogate_pair_is_one_cell_not_two()
    {
        //Arrange
        var editor = new LineEditor { Columns = 10 };

        //Act - nine characters and an emoji fill the row exactly
        var echo = editor.Insert("012345678\U0001F600");

        //Assert
        echo.Should().Be("012345678\U0001F600\r\n");
        editor.CursorRow.Should().Be(1);
        editor.CursorColumn.Should().Be(0);
    }

    [Fact]
    public void backspace_deletes_a_surrogate_pair_as_one_character()
    {
        //Arrange
        var editor = new LineEditor { Columns = 20 };
        editor.Insert("a\U0001F600");
        editor.Text.Length.Should().Be(3);

        //Act
        editor.Backspace();

        //Assert
        editor.Text.Should().Be("a");
        editor.CursorPosition.Should().Be(1);
    }

    [Fact]
    public void left_and_right_step_over_a_surrogate_pair()
    {
        //Arrange
        var editor = new LineEditor { Columns = 20 };
        editor.Insert("\U0001F600b");

        //Act
        editor.MoveHome();
        editor.MoveRight();

        //Assert - one press of Right passed the whole emoji: two UTF-16 units, one cell
        editor.CursorPosition.Should().Be(2);
        editor.CursorColumn.Should().Be(1);
    }

    [Fact]
    public void a_narrower_grid_relays_the_line_on_the_next_redraw()
    {
        //Arrange
        var editor = new LineEditor { Columns = 20 };
        editor.Insert("0123456789abcdefghij");
        editor.RowCount.Should().Be(2);

        //Act
        editor.Columns = 10;

        //Assert
        editor.RowCount.Should().Be(3);
        editor.Redraw().Should().Be("0123456789\r\nabcdefghij\r\n");
    }
}
