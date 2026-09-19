using System;
using ModelQueryTool.ChatTerminal.Editing;
using SilverAssertions;

namespace ModelQueryTool.ChatTerminal.Tests.Infrastructure;

/// <summary>
/// A line editor wired to a real headless terminal, the way the application
/// wires it to the control: the editor's echo goes straight into
/// <c>Feed</c>. After any operation, <see cref="AssertScreenMatchesModel"/>
/// checks the two things byte assertions cannot - that the SCREEN reads
/// correctly and that the CURSOR is where the editor thinks it is.
/// </summary>
internal sealed class EditorHarness
{
    private readonly HeadlessTerminal _terminal;
    private string _prompt;

    /// <summary>Creates the harness, writing the prompt to the terminal as the session would.</summary>
    /// <param name="columns">The terminal's column count.</param>
    /// <param name="prompt">The prompt in front of the line.</param>
    /// <param name="rows">The terminal's row count.</param>
    public EditorHarness(int columns, string prompt = "> ", int rows = 24)
    {
        _terminal = new HeadlessTerminal(columns, rows);
        _prompt = prompt ?? string.Empty;
        Editor = new LineEditor { Columns = columns, PromptWidth = CellWidth.OfText(_prompt) };
        _terminal.Feed(_prompt);
    }

    /// <summary>The editor under test.</summary>
    public LineEditor Editor { get; }

    /// <summary>The terminal the editor is writing to.</summary>
    public HeadlessTerminal Terminal => _terminal;

    /// <summary>Runs one editing operation and feeds what it returns to the terminal.</summary>
    /// <param name="operation">The operation, e.g. <c>e =&gt; e.Insert('a')</c>.</param>
    public void Do(Func<LineEditor, string> operation) => _terminal.Feed(operation(Editor));

    /// <summary>Types the characters one at a time, as a user would.</summary>
    /// <param name="text">The characters to type.</param>
    public void Type(string text)
    {
        foreach (var c in text) { Do(e => e.Insert(c)); }
    }

    /// <summary>
    /// Clears the screen and repaints prompt and line, as Ctrl+L does.
    /// </summary>
    public void ClearScreenAndRedraw()
    {
        _terminal.Feed("\x1b[2J\x1b[H");
        _terminal.Feed(_prompt);
        _terminal.Feed(Editor.Redraw());
    }

    /// <summary>
    /// Resizes the grid the way the session does: wind back with the old
    /// layout, change the width, write the prompt again, redraw.
    /// </summary>
    /// <param name="columns">The new column count.</param>
    /// <param name="prompt">A new prompt, or null to keep the current one.</param>
    public void Resize(int columns, string prompt = null)
    {
        var rewind = Editor.BeginRepaint(columns);
        Editor.Columns = columns;
        _terminal.Resize(columns, _terminal.Rows);
        _prompt = prompt ?? _prompt;
        Editor.PromptWidth = CellWidth.OfText(_prompt);
        _terminal.Feed(rewind);
        _terminal.Feed(_prompt);
        _terminal.Feed(Editor.Redraw());
    }

    /// <summary>
    /// Asserts that the screen shows the prompt and the text laid out as
    /// <see cref="ScreenModel"/> says they should be, and that the terminal's
    /// cursor is exactly where the editor's logical cursor is.
    /// </summary>
    public void AssertScreenMatchesModel()
    {
        var (rows, cursorRow, cursorColumn) =
            ScreenModel.LayOut(_prompt, Editor.Text, _terminal.Columns, Editor.CursorPosition);

        _terminal.Screen().Should().Equal(rows, "the screen should read as the model lays it out. Actual:\n" + _terminal.Describe());
        _terminal.CursorRow.Should().Be(cursorRow, "the cursor's row. Actual:\n" + _terminal.Describe());
        _terminal.CursorColumn.Should().Be(cursorColumn, "the cursor's column. Actual:\n" + _terminal.Describe());

        //And the editor's own idea of where the cursor is agrees with the model
        Editor.CursorRow.Should().Be(cursorRow);
        Editor.CursorColumn.Should().Be(cursorColumn);
    }
}
