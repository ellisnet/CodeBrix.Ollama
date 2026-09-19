using System.Text;

namespace ModelQueryTool.ChatTerminal.Editing;

/// <summary>
/// Tells the Enter key apart from a pasted block of text, so that pasting five
/// lines into a chat asks the model ONE question instead of five.
/// </summary>
/// <remarks>
/// <para>
/// The terminal control hands the application one string per input event, with
/// pasted line endings already normalized to CR. The rule is therefore about the
/// CHUNK, not about the characters in it: a chunk that is exactly one line
/// ending is the Enter key and submits the line; in any other chunk a line
/// ending is part of the text and becomes a line break in the edited line -
/// including a trailing one, so a paste that ends with a newline still waits for
/// the user to press Enter.
/// </para>
/// <para>
/// A chunk that carries no line ending at all (a typed character, a Ctrl chord,
/// an escape sequence from an arrow key) is unaffected by the rule: there is
/// nothing in it to reinterpret.
/// </para>
/// </remarks>
public static class PasteRule
{
    /// <summary>
    /// Whether this chunk of input is the Enter key - exactly one line ending,
    /// "\r", "\n" or "\r\n", and nothing else.
    /// </summary>
    /// <param name="chunk">One chunk of input as the terminal delivered it.</param>
    /// <returns>True when the chunk submits the line; false when its line endings are text.</returns>
    public static bool IsEnterKey(string chunk) =>
        chunk == "\r" || chunk == "\n" || chunk == "\r\n";

    /// <summary>
    /// Turns every CR, LF and CR+LF in pasted text into the single line-break
    /// character the line editor keeps in its buffer.
    /// </summary>
    /// <param name="text">The pasted text; null or empty returns an empty string.</param>
    /// <returns>The text with one <see cref="LineEditor.LineBreak"/> per line ending.</returns>
    public static string ToLineBreaks(string text)
    {
        if (string.IsNullOrEmpty(text)) { return string.Empty; }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                builder.Append(LineEditor.LineBreak);
                if (i + 1 < text.Length && text[i + 1] == '\n') { i++; }
                continue;
            }

            builder.Append(c == '\n' ? LineEditor.LineBreak : c);
        }

        return builder.ToString();
    }
}
