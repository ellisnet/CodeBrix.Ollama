using System.Collections.Generic;
using System.Text;

namespace ModelQueryTool.Core.Tests.Infrastructure;

/// <summary>
/// What a terminal would be showing after everything the chat has written - the rows, with the
/// escape sequences applied rather than merely stripped out. It is what makes "the screen holds
/// exactly one prompt" a question a test can ask: the bytes cannot tell a prompt that has been
/// erased by a wind-back from one that is still sitting there.
/// </summary>
/// <remarks>
/// <para>
/// It understands the little that the chat writes: printable text, CR, LF, the relative cursor
/// moves (CUU / CUD / CUF / CUB), erase-to-the-end (ED) and erase-in-line (EL), the clear-screen
/// pair, and the colour changes, which it drops. Anything else is ignored.
/// </para>
/// <para>
/// IT NEVER SCROLLS AND NEVER WRAPS. The screen is as tall as the transcript needs, and a row is
/// as wide as what was written on it - the chat lays its own text out against the terminal's width
/// and writes its own row breaks, so nothing here has to. That is also this model's limit: it
/// would not show what a terminal does to a row longer than its grid. The rows a REAL engine shows
/// are asserted in the ChatTerminal tests, which drive the same engine the control renders with.
/// </para>
/// </remarks>
internal static class TranscriptScreen
{
    /// <summary>Renders a transcript as the rows a reader would see, joined by newlines.</summary>
    /// <param name="transcript">Everything the chat wrote, escape sequences and all.</param>
    /// <returns>The rows, with the empty ones at the bottom left off.</returns>
    internal static string Render(string transcript)
    {
        var rows = new List<StringBuilder> { new() };
        var row = 0;
        var column = 0;

        void EnsureRow()
        {
            while (rows.Count <= row) { rows.Add(new StringBuilder()); }
        }

        void Put(char c)
        {
            EnsureRow();
            var line = rows[row];
            while (line.Length < column) { line.Append(' '); }

            if (column < line.Length) { line[column] = c; } else { line.Append(c); }

            column++;
        }

        var index = 0;
        while (index < transcript.Length)
        {
            var c = transcript[index];

            if (c == '\x1b' && index + 1 < transcript.Length && transcript[index + 1] == '[')
            {
                var end = index + 2;
                while (end < transcript.Length && !char.IsLetter(transcript[end])) { end++; }

                if (end >= transcript.Length) { break; }

                var parameters = transcript.Substring(index + 2, end - index - 2);
                var final = transcript[end];
                var count = Count(parameters);
                index = end + 1;

                switch (final)
                {
                    case 'A': row = row > count ? row - count : 0; break;
                    case 'B': row += count; EnsureRow(); break;
                    case 'C': column += count; break;
                    case 'D': column = column > count ? column - count : 0; break;
                    case 'H': row = 0; column = 0; break;

                    case 'J':
                        EnsureRow();
                        if (parameters == "2")
                        {
                            foreach (var line in rows) { line.Clear(); }
                        }
                        else
                        {
                            if (rows[row].Length > column) { rows[row].Length = column; }

                            rows.RemoveRange(row + 1, rows.Count - row - 1);
                        }

                        break;

                    case 'K':
                        EnsureRow();
                        if (rows[row].Length > column) { rows[row].Length = column; }

                        break;

                    //'m' is a colour change, which a reader does not see as text; anything else
                    //  this chat never writes
                }

                continue;
            }

            switch (c)
            {
                case '\r': column = 0; break;
                case '\n': row++; EnsureRow(); break;
                default: Put(c); break;
            }

            index++;
        }

        while (rows.Count > 0 && rows[rows.Count - 1].Length == 0) { rows.RemoveAt(rows.Count - 1); }

        var text = new StringBuilder();
        for (var line = 0; line < rows.Count; line++)
        {
            if (line > 0) { text.Append('\n'); }

            text.Append(rows[line]);
        }

        return text.ToString();
    }

    /// <summary>The first parameter of a sequence, which defaults to one when it was left out.</summary>
    private static int Count(string parameters)
    {
        if (parameters.Length == 0) { return 1; }

        var digits = 0;
        foreach (var c in parameters)
        {
            if (c < '0' || c > '9') { break; }

            digits = (digits * 10) + (c - '0');
        }

        return digits == 0 ? 1 : digits;
    }
}
