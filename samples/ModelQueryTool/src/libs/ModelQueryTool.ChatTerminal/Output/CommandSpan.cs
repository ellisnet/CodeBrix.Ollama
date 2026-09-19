namespace ModelQueryTool.ChatTerminal.Output;

/// <summary>
/// Where one mention of a command sits in a piece of plain text - the command's name and the
/// arguments written with it, as one run of characters.
/// </summary>
/// <remarks>
/// The span is an offset and a length into the text it was found in, and nothing else: it carries
/// no escape sequence and no styling decision, so the same span can be drawn one way inside a
/// dimmed line and another way inside a red one.
/// </remarks>
public readonly struct CommandSpan
{
    /// <summary>Creates a span over a piece of text.</summary>
    /// <param name="start">The index of the span's first character.</param>
    /// <param name="length">How many characters the span covers.</param>
    public CommandSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>Gets the index of the span's first character.</summary>
    public int Start { get; }

    /// <summary>Gets how many characters the span covers.</summary>
    public int Length { get; }

    /// <summary>Gets the index one past the span's last character.</summary>
    public int End => Start + Length;
}
