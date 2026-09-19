using System;
using System.Text;
using ModelQueryTool.ChatTerminal.Editing;

namespace ModelQueryTool.ChatTerminal.Output;

/// <summary>
/// Wraps a model's answer to the terminal's width AS IT ARRIVES. The answer
/// comes in as small deltas that have to be on screen immediately, so the
/// wrapper is a state machine over the text rather than a function of a whole
/// paragraph: it holds back at most the word it is in the middle of, and emits
/// everything else at once.
/// </summary>
/// <remarks>
/// <para>
/// WHAT IT GUARANTEES. The output is IDENTICAL whether a text arrives in one
/// delta or one character at a time. No word is split across rows unless the
/// word is longer than a whole row, in which case it is broken hard. The text's
/// own line breaks are kept (<c>\n</c> and <c>\r\n</c> both become CR+LF), so a
/// list or a fenced code block keeps its shape, and runs of spaces inside a row
/// are written as they came. A surrogate pair is never split, and a wide
/// character is never half-written at the right edge.
/// </para>
/// <para>
/// WHAT IT DOES NOT DO. It never interprets Markdown: asterisks, backticks and
/// hashes are text like any other. It adds no indentation and no hyphenation,
/// and it never re-wraps what it has already written - a terminal's rows are
/// final once they are on screen.
/// </para>
/// <para>
/// A space that falls where a row ends is consumed by the row break rather than
/// written at the start of the next row, which is what keeps a wrapped paragraph
/// flush left. A tab is expanded to spaces up to the next multiple of eight,
/// so the column arithmetic here never depends on the terminal's tab stops.
/// </para>
/// </remarks>
public sealed class StreamingWordWrapper
{
    private const int TabWidth = 8;

    private readonly StringBuilder _word = new();
    private int _columns;
    private int _wordWidth;
    private bool _pendingCarriageReturn;

    /// <summary>Creates a wrapper for a terminal of the given width.</summary>
    /// <param name="columns">The terminal's column count.</param>
    /// <param name="startColumn">
    /// The column the first character will land in - not zero when the
    /// application has just written a prefix on the same row.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="columns"/> is below 1, or <paramref name="startColumn"/>
    /// is negative.
    /// </exception>
    public StreamingWordWrapper(int columns, int startColumn = 0)
    {
        Columns = columns;
        Reset(startColumn);
    }

    /// <summary>
    /// The terminal's column count. A new value applies to the text still to
    /// come; what is already on screen is never re-wrapped.
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

    /// <summary>The column the next written character will land in.</summary>
    public int Column { get; private set; }

    /// <summary>Whether anything is being held back (a partial word, or a CR waiting to see whether an LF follows).</summary>
    public bool HasPendingText => _word.Length > 0 || _pendingCarriageReturn;

    /// <summary>
    /// Takes the next piece of the answer and returns what to write to the
    /// terminal now.
    /// </summary>
    /// <param name="delta">The text that just arrived; null or empty writes nothing.</param>
    /// <returns>The VT data to write, which may be empty while a word is being held.</returns>
    public string Write(string delta)
    {
        if (string.IsNullOrEmpty(delta)) { return string.Empty; }

        var output = new StringBuilder(delta.Length + 8);
        foreach (var c in delta) { Process(c, output); }

        return output.ToString();
    }

    /// <summary>
    /// Returns whatever is still held back - the last word of the answer, and a
    /// trailing CR that never got its LF. Call it when the answer is complete.
    /// </summary>
    /// <returns>The VT data to write, empty when nothing was held.</returns>
    public string Flush()
    {
        var output = new StringBuilder();
        if (_pendingCarriageReturn)
        {
            _pendingCarriageReturn = false;
            EmitLineBreak(output);
        }
        else
        {
            EmitWord(output);
        }

        return output.ToString();
    }

    /// <summary>
    /// Forgets everything held back and starts again at a known column - for the
    /// next answer, or after the application has written something of its own.
    /// </summary>
    /// <param name="startColumn">The column the next character will land in.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startColumn"/> is negative.</exception>
    public void Reset(int startColumn = 0)
    {
        if (startColumn < 0) { throw new ArgumentOutOfRangeException(nameof(startColumn), "A column is not negative."); }

        _word.Clear();
        _wordWidth = 0;
        _pendingCarriageReturn = false;
        Column = startColumn;
    }

    private void Process(char c, StringBuilder output)
    {
        if (_pendingCarriageReturn)
        {
            _pendingCarriageReturn = false;
            EmitLineBreak(output);
            if (c == '\n') { return; }
        }

        switch (c)
        {
            case '\r':
                //Held: a CR+LF pair is one break, and a lone CR is one too
                _pendingCarriageReturn = true;
                return;

            case '\n':
                EmitLineBreak(output);
                return;

            case '\t':
                EmitTab(output);
                return;

            case ' ':
                EmitWord(output);
                if (Column >= _columns)
                {
                    //The row break takes the place of the space
                    output.Append("\r\n");
                    Column = 0;
                }
                else
                {
                    output.Append(' ');
                    Column++;
                }

                return;

            default:
                AppendToWord(c, output);
                return;
        }
    }

    private void AppendToWord(char c, StringBuilder output)
    {
        var previousIsHighSurrogate = _word.Length > 0 && char.IsHighSurrogate(_word[_word.Length - 1]);
        _word.Append(c);

        if (char.IsHighSurrogate(c))
        {
            //Its width is not known until the low half arrives
            return;
        }

        _wordWidth += previousIsHighSurrogate && char.IsLowSurrogate(c)
            ? CellWidth.OfCodePoint(char.ConvertToUtf32(_word[_word.Length - 2], c))
            : CellWidth.OfCodePoint(c);

        MakeRoom(output);
    }

    /// <summary>
    /// Keeps the held word short enough to fit in what is left of the current
    /// row: a word that does not fit moves to the next row whole, and a word
    /// that fits on no row at all is written a row at a time.
    /// </summary>
    private void MakeRoom(StringBuilder output)
    {
        while (_wordWidth > _columns - Column)
        {
            if (Column > 0)
            {
                output.Append("\r\n");
                Column = 0;
                continue;
            }

            EmitHardBreak(output);
        }
    }

    private void EmitHardBreak(StringBuilder output)
    {
        var used = 0;
        var i = 0;
        while (i < _word.Length)
        {
            var length = LengthAt(i);
            var width = WidthAt(i, length);

            //The first character always goes, so a one-column terminal still
            //  makes progress instead of looping
            if (i > 0 && used + width > _columns) { break; }

            used += width;
            i += length;
        }

        output.Append(_word.ToString(0, i)).Append("\r\n");
        _word.Remove(0, i);
        _wordWidth -= used;
        Column = 0;
    }

    private void EmitWord(StringBuilder output)
    {
        if (_word.Length == 0) { return; }

        output.Append(_word);
        Column += _wordWidth;
        _word.Clear();
        _wordWidth = 0;
    }

    private void EmitLineBreak(StringBuilder output)
    {
        EmitWord(output);
        output.Append("\r\n");
        Column = 0;
    }

    private void EmitTab(StringBuilder output)
    {
        EmitWord(output);

        var target = ((Column / TabWidth) + 1) * TabWidth;
        if (target > _columns)
        {
            if (Column > 0)
            {
                output.Append("\r\n");
                Column = 0;
            }

            target = Math.Min(TabWidth, _columns);
        }

        output.Append(' ', target - Column);
        Column = target;
    }

    private int LengthAt(int index) =>
        char.IsHighSurrogate(_word[index])
        && index + 1 < _word.Length
        && char.IsLowSurrogate(_word[index + 1])
            ? 2
            : 1;

    private int WidthAt(int index, int length) =>
        length == 2
            ? CellWidth.OfCodePoint(char.ConvertToUtf32(_word[index], _word[index + 1]))
            : CellWidth.OfCodePoint(_word[index]);
}
