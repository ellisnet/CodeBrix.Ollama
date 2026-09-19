using System.Collections.Generic;
using System.Text;

namespace ModelQueryTool.ChatTerminal.Tests.Infrastructure;

/// <summary>
/// What a prompt and a line of text OUGHT to look like on a terminal of a given
/// width, worked out the plainest way there is - place one character after
/// another, start a new row when the row is full or the text says so. It shares
/// no code with the editor, so agreeing with it means something.
/// </summary>
internal static class ScreenModel
{
    /// <summary>
    /// Lays the prompt and the text out, and says where the cursor belongs.
    /// </summary>
    /// <param name="prompt">The prompt, which occupies the first cells.</param>
    /// <param name="text">The text being edited; '\n' starts a new row.</param>
    /// <param name="columns">The terminal's column count.</param>
    /// <param name="cursorIndex">The cursor's index within <paramref name="text"/>.</param>
    /// <returns>The rows (trailing blanks trimmed) and the cursor's row and column.</returns>
    public static (IReadOnlyList<string> Rows, int CursorRow, int CursorColumn) LayOut(
        string prompt, string text, int columns, int cursorIndex)
    {
        var rows = new List<StringBuilder> { new() };
        var column = 0;
        var cursorRow = 0;
        var cursorColumn = 0;

        void NewRow()
        {
            rows.Add(new StringBuilder());
            column = 0;
        }

        void Place(string glyph, int width)
        {
            if (column > 0 && column + width > columns) { NewRow(); }

            rows[rows.Count - 1].Append(glyph);
            column += width;
            if (column >= columns) { NewRow(); }
        }

        foreach (var (glyph, width) in Characters(prompt ?? string.Empty))
        {
            Place(glyph, width);
        }

        text ??= string.Empty;
        var index = 0;
        foreach (var (glyph, width) in Characters(text))
        {
            if (index == cursorIndex)
            {
                cursorRow = rows.Count - 1;
                cursorColumn = column;
            }

            if (glyph == "\n")
            {
                NewRow();
            }
            else
            {
                Place(glyph, width);
            }

            index += glyph.Length;
        }

        if (index == cursorIndex)
        {
            cursorRow = rows.Count - 1;
            cursorColumn = column;
        }

        //Rows are compared against the engine's own trimRight, which drops the
        //  cells that were never written and keeps the spaces that were typed
        var lines = new List<string>();
        foreach (var row in rows) { lines.Add(row.ToString()); }
        while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) { lines.RemoveAt(lines.Count - 1); }

        return (lines, cursorRow, cursorColumn);
    }

    /// <summary>
    /// The characters of a string with their cell widths: one cell per code
    /// point, which is what the engine gives them, so a surrogate pair is one
    /// character and one cell. Written out here rather than taken from the
    /// library's own <c>CellWidth</c>, so that the two can disagree.
    /// </summary>
    private static IEnumerable<(string Glyph, int Width)> Characters(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return (text.Substring(i, 2), 1);
                i++;
                continue;
            }

            yield return (text[i].ToString(), text[i] == '\n' ? 0 : 1);
        }
    }
}
