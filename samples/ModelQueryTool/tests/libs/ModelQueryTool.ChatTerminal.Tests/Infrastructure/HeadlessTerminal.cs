using System;
using System.Collections.Generic;
using System.Text;
using CodeBrix.Terminal.Engine;

namespace ModelQueryTool.ChatTerminal.Tests.Infrastructure;

/// <summary>
/// A real terminal, of a known size, with no screen attached: the same engine
/// the application's terminal control renders. The tests feed it exactly what
/// the library emits and then read back what a user would see - the rows of
/// text and where the cursor sits - so that an assertion is about the DISPLAY
/// and not merely about a string of escape codes staying the same.
/// </summary>
internal sealed class HeadlessTerminal
{
    private readonly Terminal _terminal;

    /// <summary>Creates a terminal of the given grid size.</summary>
    /// <param name="columns">The column count.</param>
    /// <param name="rows">The row count.</param>
    public HeadlessTerminal(int columns, int rows = 24)
    {
        //ConvertEol stays off: everything this library writes carries explicit CR+LF,
        //  which is how the control is configured in the application too.
        _terminal = new Terminal(null, new TerminalOptions
        {
            Cols = columns,
            Rows = rows,
            ConvertEol = false,
        });
    }

    /// <summary>The column count.</summary>
    public int Columns => _terminal.Cols;

    /// <summary>The row count.</summary>
    public int Rows => _terminal.Rows;

    /// <summary>The cursor's column, 0-based.</summary>
    public int CursorColumn => _terminal.Buffer.X;

    /// <summary>The cursor's row within the visible screen, 0-based.</summary>
    public int CursorRow => _terminal.Buffer.Y;

    /// <summary>Writes VT data to the terminal, exactly as the control's Feed would.</summary>
    /// <param name="data">The data to write; empty data is dropped.</param>
    /// <remarks>
    /// The empty check is not a nicety: this engine build throws
    /// IndexOutOfRangeException out of its parser when it is fed an empty
    /// string. The control the application uses documents that it drops empty
    /// data before it reaches the engine, so this stands in for that guard.
    /// </remarks>
    public void Feed(string data)
    {
        if (string.IsNullOrEmpty(data)) { return; }

        _terminal.Feed(data);
    }

    /// <summary>Resizes the grid, as the control does when its window changes size.</summary>
    /// <param name="columns">The new column count.</param>
    /// <param name="rows">The new row count.</param>
    public void Resize(int columns, int rows) => _terminal.Resize(columns, rows);

    /// <summary>One row of the visible screen, with its trailing blanks removed.</summary>
    /// <param name="row">The row index, 0-based.</param>
    /// <returns>The text on that row.</returns>
    public string Row(int row) =>
        _terminal.Buffer.Lines[_terminal.Buffer.YBase + row]
            .TranslateToString(trimRight: true)
            .ToString();

    /// <summary>
    /// The visible screen as a list of rows, with the empty rows at the bottom
    /// left off - what a reader would say is "on the screen".
    /// </summary>
    /// <returns>The non-empty rows, top to bottom.</returns>
    public IReadOnlyList<string> Screen()
    {
        var rows = new List<string>();
        for (var row = 0; row < Rows; row++) { rows.Add(Row(row)); }

        while (rows.Count > 0 && rows[rows.Count - 1].Length == 0) { rows.RemoveAt(rows.Count - 1); }

        return rows;
    }

    /// <summary>The visible screen as one string, rows joined by a newline.</summary>
    /// <returns>The screen text.</returns>
    public string ScreenText() => string.Join("\n", Screen());

    /// <summary>
    /// A picture of the screen with the cursor marked by a caret under it -
    /// what a failing assertion should print to be worth reading.
    /// </summary>
    /// <returns>The screen text with a cursor marker row.</returns>
    public string Describe()
    {
        var builder = new StringBuilder();
        for (var row = 0; row < Rows; row++)
        {
            var text = Row(row);
            if (text.Length == 0 && row > CursorRow) { continue; }

            builder.Append(row).Append(": [").Append(text).Append(']');
            if (row == CursorRow)
            {
                builder.Append("   cursor column ").Append(CursorColumn);
            }

            builder.Append(Environment.NewLine);
        }

        return builder.ToString();
    }
}
