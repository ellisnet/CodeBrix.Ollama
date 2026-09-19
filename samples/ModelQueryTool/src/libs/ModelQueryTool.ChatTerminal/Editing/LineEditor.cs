using System;
using System.Collections.Generic;
using System.Text;

namespace ModelQueryTool.ChatTerminal.Editing;

/// <summary>
/// The input-line editor. Each editing operation mutates the buffer and returns
/// the VT string that makes the terminal display match - the caller writes that
/// string to the terminal verbatim. The editor lays the line out across as many
/// terminal rows as it needs, so it has to know the grid's
/// <see cref="Columns"/> and the width of the prompt in front of the text
/// (<see cref="PromptWidth"/>); everything it emits is RELATIVE cursor movement
/// (CUU / CUD / CR / CUF) and relative erasure (ED), so a screen that scrolls
/// under the line does not disturb it.
/// </summary>
/// <remarks>
/// <para>
/// LINE BREAKS. The text may contain <see cref="LineBreak"/> characters - what a
/// multi-line paste leaves behind. A break is drawn as CR+LF and starts a new
/// row at column 0. CR and CR+LF handed to <see cref="Insert(string)"/> are
/// normalized to it.
/// </para>
/// <para>
/// DEFERRED WRAP. A terminal that receives a character in its last column does
/// NOT move the cursor to the next row until the following character arrives,
/// which would make the cursor's whereabouts depend on what comes next. The
/// editor never relies on that: whenever its own text fills a row exactly it
/// emits CR+LF itself, so the cursor is always at a column it chose, and the
/// terminal's automatic wrap is never the thing that moves it. The same rule
/// applies after the prompt: a prompt whose width is an exact multiple of the
/// column count is followed by a CR+LF in <see cref="Redraw"/>.
/// </para>
/// <para>
/// CHARACTERS THAT ARE NOT ONE UTF-16 UNIT. Cell arithmetic goes through
/// <see cref="CellWidth"/>, which counts what the grid counts: one cell per code
/// point, so a surrogate pair is ONE character in one cell. Cursor movement
/// steps over the pair as one character, and Backspace and Delete take it whole.
/// The layout asks for a width rather than assuming one, and moves a character
/// that would not fit in what is left of a row to the next row whole, so a grid
/// that one day gives some characters two cells needs no change here.
/// </para>
/// <para>
/// LIMITATION. A line that is taller than the terminal's screen cannot be
/// repainted correctly: its first row has scrolled off the top, and no relative
/// movement reaches it any more. Everything shorter than the screen is right,
/// including while the screen scrolls.
/// </para>
/// </remarks>
public sealed class LineEditor
{
    /// <summary>The character that stands for a line break inside the edited text.</summary>
    public const char LineBreak = '\n';

    private const int DefaultColumns = 80;

    private readonly StringBuilder _buffer = new();
    private int _cursor;
    private int _columns = DefaultColumns;
    private int _promptWidth;
    private int _row;
    private int _column;
    private bool _positionKnown;

    /// <summary>The current text of the line being edited.</summary>
    public string Text => _buffer.ToString();

    /// <summary>The cursor position within <see cref="Text"/> (0 = before the first character).</summary>
    public int CursorPosition => _cursor;

    /// <summary>
    /// The terminal's column count. Default 80. Changing it changes how the
    /// line is laid out but writes nothing: the caller repaints (see
    /// <see cref="BeginRepaint"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 1.</exception>
    public int Columns
    {
        get => _columns;
        set
        {
            if (value < 1) { throw new ArgumentOutOfRangeException(nameof(value), "A terminal has at least one column."); }
            _columns = value;
        }
    }

    /// <summary>
    /// How many cells the prompt in front of the text occupies. The prompt
    /// itself is written by the session; the editor only needs its width to
    /// know where the first character of the line sits.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int PromptWidth
    {
        get => _promptWidth;
        set
        {
            if (value < 0) { throw new ArgumentOutOfRangeException(nameof(value), "A prompt cannot be narrower than nothing."); }
            _promptWidth = value;
        }
    }

    /// <summary>
    /// How many terminal rows the prompt and the text occupy together (at least one).
    /// </summary>
    public int RowCount => PositionOf(_buffer.Length).Row + 1;

    /// <summary>The row the cursor sits on, counted from the prompt's first row.</summary>
    public int CursorRow => PositionOf(_cursor).Row;

    /// <summary>The column the cursor sits in.</summary>
    public int CursorColumn => PositionOf(_cursor).Column;

    /// <summary>Inserts a character at the cursor. Returns the echo string.</summary>
    /// <param name="c">The character to insert.</param>
    /// <returns>The VT data to write to the terminal.</returns>
    public string Insert(char c) => InsertCore(c.ToString());

    /// <summary>
    /// Inserts a string (a paste, or a surrogate pair delivered as one unit) at
    /// the cursor. CR, LF and CR+LF inside the text all become
    /// <see cref="LineBreak"/>. Returns the echo string.
    /// </summary>
    /// <param name="text">The text to insert; null or empty inserts nothing.</param>
    /// <returns>The VT data to write to the terminal.</returns>
    public string Insert(string text) => InsertCore(text);

    /// <summary>Inserts a line break at the cursor. Returns the echo string.</summary>
    /// <returns>The VT data to write to the terminal.</returns>
    public string InsertLineBreak() => InsertCore(LineBreak.ToString());

    /// <summary>
    /// Deletes the character before the cursor - a whole surrogate pair when
    /// that is what is there. Returns the echo string.
    /// </summary>
    /// <returns>The VT data to write to the terminal, or an empty string at the start of the line.</returns>
    public string Backspace()
    {
        if (_cursor == 0) { return string.Empty; }

        var start = PreviousIndex(_cursor);
        _buffer.Remove(start, _cursor - start);
        _cursor = start;
        return Repaint(start, erase: true);
    }

    /// <summary>
    /// Deletes the character under the cursor - a whole surrogate pair when that
    /// is what is there. Returns the echo string.
    /// </summary>
    /// <returns>The VT data to write to the terminal, or an empty string at the end of the line.</returns>
    public string Delete()
    {
        if (_cursor >= _buffer.Length) { return string.Empty; }

        _buffer.Remove(_cursor, CharLength(_cursor));
        return Repaint(_cursor, erase: true);
    }

    /// <summary>Moves the cursor one character left, across a row boundary or a line break. Returns the echo string.</summary>
    /// <returns>The VT data to write to the terminal.</returns>
    public string MoveLeft()
    {
        if (_cursor == 0) { return string.Empty; }

        _cursor = PreviousIndex(_cursor);
        return MoveTo(PositionOf(_cursor));
    }

    /// <summary>Moves the cursor one character right, across a row boundary or a line break. Returns the echo string.</summary>
    /// <returns>The VT data to write to the terminal.</returns>
    public string MoveRight()
    {
        if (_cursor >= _buffer.Length) { return string.Empty; }

        _cursor += CharLength(_cursor);
        return MoveTo(PositionOf(_cursor));
    }

    /// <summary>
    /// Moves the cursor to the start of the whole text - not to the start of
    /// the row it is on, and not to the start of the line an embedded break
    /// began. Returns the echo string.
    /// </summary>
    /// <returns>The VT data to write to the terminal.</returns>
    public string MoveHome()
    {
        _cursor = 0;
        return MoveTo(PositionOf(0));
    }

    /// <summary>Moves the cursor past the last character of the whole text. Returns the echo string.</summary>
    /// <returns>The VT data to write to the terminal.</returns>
    public string MoveEnd()
    {
        _cursor = _buffer.Length;
        return MoveTo(PositionOf(_cursor));
    }

    /// <summary>
    /// Replaces the whole line with new text (history navigation). Returns the
    /// echo string, which leaves no trace of the old line however many rows it
    /// occupied.
    /// </summary>
    /// <param name="text">The replacement text; null is treated as empty.</param>
    /// <returns>The VT data to write to the terminal.</returns>
    public string ReplaceWith(string text)
    {
        var erase = _buffer.Length > 0;
        _buffer.Clear();
        _buffer.Append(Normalize(text));
        _cursor = _buffer.Length;
        return Repaint(0, erase);
    }

    /// <summary>
    /// Returns the finished line and resets the editor for the next one.
    /// Produces no echo - the caller moves the cursor to the end of the line
    /// (<see cref="MoveEnd"/>) and writes the line ending itself.
    /// </summary>
    /// <returns>The text that was being edited.</returns>
    public string TakeLine()
    {
        var line = _buffer.ToString();
        Reset();
        return line;
    }

    /// <summary>
    /// Draws the text and puts the cursor back where it belongs, assuming the
    /// terminal's cursor is where the prompt has just finished being written.
    /// Call it after a screen clear, after out-of-band output, and after a
    /// resize (see <see cref="BeginRepaint"/>).
    /// </summary>
    /// <returns>The VT data to write to the terminal.</returns>
    public string Redraw()
    {
        var start = new Position(_promptWidth / _columns, _promptWidth % _columns);
        var builder = new StringBuilder();

        //A prompt that exactly filled its last row has left the terminal in
        //  deferred wrap; CR+LF settles it on the row the layout expects.
        if (_promptWidth > 0 && start.Column == 0) { builder.Append("\r\n"); }

        _positionKnown = true;
        _row = start.Row;
        _column = start.Column;
        builder.Append(Render(0, start, out var end));
        _row = end.Row;
        _column = end.Column;
        builder.Append(MoveTo(PositionOf(_cursor)));
        return builder.ToString();
    }

    /// <summary>
    /// Returns the cursor to column 0 of the row the prompt starts on and
    /// erases everything from there down, so that the caller can write the
    /// prompt again and call <see cref="Redraw"/>.
    /// </summary>
    /// <returns>The VT data to write to the terminal.</returns>
    public string BeginRepaint() => BeginRepaint(_columns);

    /// <summary>
    /// The same, for a terminal that has just been made a different width. This
    /// is how a grid resize is handled: call this FIRST (it works from the old
    /// layout), then set <see cref="Columns"/>, then write the prompt and call
    /// <see cref="Redraw"/>.
    /// </summary>
    /// <param name="nextColumns">The column count the terminal has just been given.</param>
    /// <returns>The VT data to write to the terminal.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nextColumns"/> is below 1.</exception>
    /// <remarks>
    /// A terminal that is made NARROWER re-lays what is already on screen, one
    /// written row at a time: a row of the old width becomes as many rows of the
    /// new width as it needs, and everything below it - the cursor included -
    /// moves down by the rows that appeared above it. The wind-back counts those
    /// rows, so it reaches the prompt rather than the middle of the old line. A
    /// terminal that is made WIDER leaves the rows as they are, and the same
    /// arithmetic then counts one row per row.
    /// </remarks>
    public string BeginRepaint(int nextColumns)
    {
        if (nextColumns < 1) { throw new ArgumentOutOfRangeException(nameof(nextColumns), "A terminal has at least one column."); }

        var current = Current;
        var cells = RowCellCounts();

        var rowsUp = 0;
        for (var row = 0; row < current.Row && row < cells.Count; row++)
        {
            rowsUp += Math.Max(1, (cells[row] + nextColumns - 1) / nextColumns);
        }

        rowsUp += current.Column / nextColumns;

        var builder = new StringBuilder();
        if (rowsUp > 0) { builder.Append("\x1b[").Append(rowsUp).Append('A'); }
        builder.Append("\r\x1b[J");
        _positionKnown = true;
        _row = 0;
        _column = 0;
        return builder.ToString();
    }

    /// <summary>
    /// Clears the buffer and the cursor without producing any echo. The editor
    /// then expects the next thing on screen to be a freshly written prompt.
    /// </summary>
    public void Reset()
    {
        _buffer.Clear();
        _cursor = 0;
        _row = 0;
        _column = 0;
        _positionKnown = false;
    }

    private string InsertCore(string text)
    {
        text = Normalize(text);
        if (text.Length == 0) { return string.Empty; }

        var anchor = _cursor;
        var hadTail = anchor < _buffer.Length;
        _buffer.Insert(anchor, text);
        _cursor = anchor + text.Length;
        return Repaint(anchor, hadTail);
    }

    /// <summary>
    /// Rewrites the display from <paramref name="anchor"/> - the first index
    /// whose drawing changed - and leaves the cursor where the logical cursor
    /// is. <paramref name="erase"/> says whether anything was drawn from that
    /// index before; when it was, an ED clears it before the new text goes down,
    /// which is what makes a shorter replacement, a deleted character and a
    /// newly inserted line break leave nothing stale behind.
    /// </summary>
    private string Repaint(int anchor, bool erase)
    {
        var builder = new StringBuilder();
        var anchorPosition = PositionOf(anchor);
        builder.Append(MoveTo(anchorPosition));
        if (erase) { builder.Append("\x1b[J"); }

        builder.Append(Render(anchor, anchorPosition, out var end));
        _row = end.Row;
        _column = end.Column;
        builder.Append(MoveTo(PositionOf(_cursor)));
        return builder.ToString();
    }

    /// <summary>
    /// Writes the text from <paramref name="index"/> to the end, starting at
    /// <paramref name="start"/>, breaking rows itself so the terminal's own
    /// wrap never fires.
    /// </summary>
    private string Render(int index, Position start, out Position end)
    {
        var builder = new StringBuilder();
        var row = start.Row;
        var column = start.Column;

        for (var i = index; i < _buffer.Length;)
        {
            var c = _buffer[i];
            if (c == LineBreak)
            {
                builder.Append("\r\n");
                row++;
                column = 0;
                i++;
                continue;
            }

            var length = CharLength(i);
            var width = WidthAt(i, length);
            if (column > 0 && column + width > _columns)
            {
                builder.Append("\r\n");
                row++;
                column = 0;
            }

            builder.Append(_buffer.ToString(i, length));
            column += width;
            if (column >= _columns)
            {
                builder.Append("\r\n");
                row++;
                column = 0;
            }

            i += length;
        }

        end = new Position(row, column);
        return builder.ToString();
    }

    /// <summary>
    /// How many cells each row of the current layout has written on it, from the
    /// prompt's first row to the last row of the text.
    /// </summary>
    private List<int> RowCellCounts()
    {
        var counts = new List<int>();
        var row = _promptWidth / _columns;
        var column = _promptWidth % _columns;

        void Record(int atRow, int cells)
        {
            while (counts.Count <= atRow) { counts.Add(0); }
            counts[atRow] = Math.Max(counts[atRow], cells);
        }

        //The prompt's own rows are full up to the row it ends on
        for (var filled = 0; filled < row; filled++) { Record(filled, _columns); }
        Record(row, column);

        for (var i = 0; i < _buffer.Length;)
        {
            if (_buffer[i] == LineBreak)
            {
                Record(row, column);
                row++;
                column = 0;
                Record(row, 0);
                i++;
                continue;
            }

            var length = CharLength(i);
            var width = WidthAt(i, length);
            if (column > 0 && column + width > _columns)
            {
                row++;
                column = 0;
            }

            column += width;
            Record(row, column);
            if (column >= _columns)
            {
                row++;
                column = 0;
                Record(row, 0);
            }

            i += length;
        }

        return counts;
    }

    /// <summary>Where the character at <paramref name="index"/> is drawn, counting from the prompt's first row.</summary>
    private Position PositionOf(int index)
    {
        var row = _promptWidth / _columns;
        var column = _promptWidth % _columns;

        for (var i = 0; i < index;)
        {
            var c = _buffer[i];
            if (c == LineBreak)
            {
                row++;
                column = 0;
                i++;
                continue;
            }

            var length = CharLength(i);
            var width = WidthAt(i, length);
            if (column > 0 && column + width > _columns)
            {
                row++;
                column = 0;
            }

            column += width;
            if (column >= _columns)
            {
                row++;
                column = 0;
            }

            i += length;
        }

        return new Position(row, column);
    }

    /// <summary>
    /// Where the terminal's cursor is. Until the first operation of a line that
    /// is where the prompt has just finished being written, which is what the
    /// session leaves behind when it writes a prompt and the user starts typing.
    /// </summary>
    private Position Current =>
        _positionKnown
            ? new Position(_row, _column)
            : new Position(_promptWidth / _columns, _promptWidth % _columns);

    private string MoveTo(Position target)
    {
        var current = Current;
        _positionKnown = true;
        if (target.Row == current.Row && target.Column == current.Column)
        {
            _row = target.Row;
            _column = target.Column;
            return string.Empty;
        }

        var builder = new StringBuilder();
        var rows = target.Row - current.Row;
        if (rows < 0) { builder.Append("\x1b[").Append(-rows).Append('A'); }
        else if (rows > 0) { builder.Append("\x1b[").Append(rows).Append('B'); }

        builder.Append('\r');
        if (target.Column > 0) { builder.Append("\x1b[").Append(target.Column).Append('C'); }

        _row = target.Row;
        _column = target.Column;
        return builder.ToString();
    }

    private int WidthAt(int index, int length) =>
        length == 2
            ? CellWidth.OfCodePoint(char.ConvertToUtf32(_buffer[index], _buffer[index + 1]))
            : CellWidth.OfCodePoint(_buffer[index]);

    private int CharLength(int index) =>
        char.IsHighSurrogate(_buffer[index])
        && index + 1 < _buffer.Length
        && char.IsLowSurrogate(_buffer[index + 1])
            ? 2
            : 1;

    private int PreviousIndex(int index)
    {
        var previous = index - 1;
        if (previous > 0 && char.IsLowSurrogate(_buffer[previous]) && char.IsHighSurrogate(_buffer[previous - 1]))
        {
            previous--;
        }

        return previous;
    }

    /// <summary>Turns every CR, LF and CR+LF in inserted text into one <see cref="LineBreak"/>.</summary>
    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) { return string.Empty; }
        if (text.IndexOf('\r') < 0) { return text; }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                builder.Append(LineBreak);
                if (i + 1 < text.Length && text[i + 1] == '\n') { i++; }
                continue;
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    private readonly struct Position
    {
        public Position(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public int Row { get; }

        public int Column { get; }
    }
}
