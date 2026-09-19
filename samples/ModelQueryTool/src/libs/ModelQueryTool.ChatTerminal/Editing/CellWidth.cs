namespace ModelQueryTool.ChatTerminal.Editing;

/// <summary>
/// How many cells of the terminal's grid a character or a run of text takes up.
/// Every piece of cursor arithmetic in this library counts cells, so this is the
/// one place that decides what a cell is.
/// </summary>
/// <remarks>
/// <para>
/// ONE CELL PER CODE POINT. That is what the terminal engine behind the chat's
/// control does: measured against it, an ideograph and an emoji each advance the
/// cursor by exactly one column, and a surrogate pair advances it by one, not by
/// two. The library counts the same way, because a cursor that disagrees with
/// the grid lands under the wrong character. What matters most - and what is NOT
/// one cell per UTF-16 unit - is the surrogate pair: an emoji is two units of a
/// string and one character on the screen.
/// </para>
/// <para>
/// If the grid ever starts giving a wide character two cells, this is the method
/// to change: the layout code around it already asks for a width instead of
/// assuming one, and moves a character that will not fit to the next row whole.
/// </para>
/// </remarks>
public static class CellWidth
{
    /// <summary>
    /// The number of cells one Unicode code point occupies: 0 for a control
    /// character, which is never drawn, and 1 for everything else.
    /// </summary>
    /// <param name="codePoint">The Unicode code point to measure.</param>
    /// <returns>0 or 1.</returns>
    public static int OfCodePoint(int codePoint) =>
        codePoint < 0x20 || (codePoint >= 0x7f && codePoint < 0xa0) ? 0 : 1;

    /// <summary>
    /// The number of cells a string occupies. A surrogate pair counts once, and
    /// escape sequences (CSI, OSC and the two-character ESC forms) count for
    /// nothing, so a prompt that carries its own colour measures the same as the
    /// text a reader sees.
    /// </summary>
    /// <param name="text">The text to measure; null or empty measures 0.</param>
    /// <returns>The total number of cells.</returns>
    /// <remarks>
    /// A line ending inside <paramref name="text"/> counts as zero cells; this
    /// method measures ONE row's worth of text, such as a prompt.
    /// </remarks>
    public static int OfText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var total = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\x1b')
            {
                i = SkipEscape(text, i);
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                total += OfCodePoint(char.ConvertToUtf32(c, text[i + 1]));
                i++;
                continue;
            }

            total += OfCodePoint(c);
        }

        return total;
    }

    /// <summary>
    /// Returns the index of the LAST character of the escape sequence that
    /// starts at <paramref name="start"/>, so that a for-loop's increment lands
    /// on the character after it.
    /// </summary>
    private static int SkipEscape(string text, int start)
    {
        var i = start + 1;
        if (i >= text.Length)
        {
            return start;
        }

        if (text[i] == '[')
        {
            //CSI: parameter and intermediate bytes, then a final byte 0x40-0x7e
            i++;
            while (i < text.Length && (text[i] < '\x40' || text[i] > '\x7e')) { i++; }
            return i < text.Length ? i : text.Length - 1;
        }

        if (text[i] == ']')
        {
            //OSC: runs to BEL or to the string terminator ESC \
            i++;
            while (i < text.Length && text[i] != '\x07')
            {
                if (text[i] == '\x1b' && i + 1 < text.Length && text[i + 1] == '\\') { return i + 1; }
                i++;
            }

            return i < text.Length ? i : text.Length - 1;
        }

        //An escape with intermediate bytes then one final byte: charset
        //  selection (ESC ( B) is three characters, a reset (ESC c) is two
        while (i < text.Length && text[i] >= '\x20' && text[i] <= '\x2f') { i++; }

        return i < text.Length ? i : text.Length - 1;
    }
}
